using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace OpenUtau.Core.Vst {
    /// <summary>
    /// 专用 VST 线程：controller 创建 → createView/attached → 编辑器窗口消息循环
    /// 全部在同一线程（VST3 插件线程敏感检查 + 插件在 attached 中等待消息泵的需要）。
    ///
    /// 线程主体 = GetMessage 阻塞式消息循环（与桥接层直弹验证一致——对 OTT 等
    /// 用跨线程 SendMessage 通知渲染线程的插件可靠；MsgWaitForMultipleObjects +
    /// PeekMessage 循环会漏掉跨线程 SendMessage 的唤醒 → 插件渲染线程卡死）。
    ///
    /// 宿主命令唤醒：隐藏哨兵窗口 + PostMessage(HWND)——不依赖线程 ID
    /// （PostThreadMessageW 有线程未就绪/已退出的 1444 竞态）。
    ///
    /// 为什么必须专用线程（桥接层实测 Persistent Q attached 593ms 证明桥接本身
    /// 正常，卡死根因 = controller 创建线程（Task.Run 任意线程）与 attached 线程
    /// （UI）不匹配）：本方案 vst_load 也在本线程 → controller 线程 = attached
    /// 线程 = 消息泵线程，三者合一，线程敏感检查通过且插件消息由自身线程泵出。
    /// </summary>
    public sealed class VstThread : IDisposable {
        // VSTGUI and other module-level GUI systems retain their first UI thread.
        // Every instance, including reloads and export instances, must use that same thread.
        static readonly Lazy<VstThread> shared = new(() => new VstThread("VST host"));
        internal static VstThread Shared => shared.Value;
        const uint WM_QUIT = 0x0012;
        const uint WM_DESTROY = 0x0002;
        const uint WM_APP_COMMAND = 0x8000;

        [StructLayout(LayoutKind.Sequential)]
        struct MSG {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int pt_x;
            public int pt_y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WNDCLASSEX {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);

        [DllImport("user32.dll")]
        static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);

        [DllImport("user32.dll")]
        static extern bool TranslateMessage(ref MSG msg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessageW(ref MSG msg);

        [DllImport("user32.dll")]
        static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern ushort RegisterClassExW(ref WNDCLASSEX wc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateWindowExW(uint exStyle, string cls, string name, uint style,
            int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

        [DllImport("user32.dll")]
        static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool UnregisterClassW(string cls, IntPtr inst);

        [DllImport("user32.dll")]
        static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr GetModuleHandleW(string? name);

        readonly Thread _thread;
        readonly ConcurrentQueue<(Action run, ManualResetEventSlim done)> _queue = new();
        volatile bool _disposed;
        readonly string _name;
        readonly string _class;
        readonly WndProcDelegate _wndProc;
        readonly GCHandle _wndProcHandle;
        IntPtr _sentinel;
        readonly ManualResetEventSlim _ready = new(false);
        static int _classSeq;

        public VstThread(string name) {
            _name = name;
            _class = $"OUVstThread{Interlocked.Increment(ref _classSeq)}";
            _wndProc = SentinelWndProc;
            _wndProcHandle = GCHandle.Alloc(_wndProc); // 防止委托被 GC（窗口过程引用）
            _thread = new Thread(ThreadMain) {
                IsBackground = true,
                Name = name,
            };
            _thread.SetApartmentState(ApartmentState.STA); // 插件内部 COM 组件（OLE 控件等）需要 STA
            _thread.Start();
            _ready.Wait(); // 等待哨兵窗口就绪（命令唤醒依赖它）
        }

        /// <summary>Marshal 到专用线程同步执行，返回结果。调用线程已在专用线程时直接执行。</summary>
        public T Invoke<T>(Func<T> action) {
            if (Thread.CurrentThread == _thread) {
                return action();
            }
            if (_disposed) {
                throw new ObjectDisposedException(nameof(VstThread));
            }
            var done = new ManualResetEventSlim(false);
            T? result = default;
            Exception? error = null;
            _queue.Enqueue((() => {
                try {
                    result = action();
                } catch (Exception ex) {
                    error = ex;
                }
                done.Set();
            }, done));
            if (!PostMessageW(_sentinel, WM_APP_COMMAND, IntPtr.Zero, IntPtr.Zero)) {
                // 哨兵窗口已销毁（线程退出中）——无法投递，通知等待者避免永久挂起
                error = new InvalidOperationException("VST host command could not be posted.");
                done.Set();
            }
            done.Wait();
            if (error != null) {
                throw error;
            }
            return result!;
        }

        public void Invoke(Action action) => Invoke(() => { action(); return true; });

        /// <summary>Cancellation skips queued work; it never interrupts an active native call.</summary>
        public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellation = default) {
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_disposed) return Task.FromException<T>(new ObjectDisposedException(nameof(VstThread)));
            if (Thread.CurrentThread == _thread) {
                try { cancellation.ThrowIfCancellationRequested(); completion.SetResult(action()); }
                catch (Exception e) { completion.SetException(e); }
                return completion.Task;
            }
            var done = new ManualResetEventSlim();
            _queue.Enqueue((() => {
                try {
                    if (cancellation.IsCancellationRequested) completion.TrySetCanceled(cancellation);
                    else completion.TrySetResult(action());
                } catch (Exception e) { completion.TrySetException(e); }
            }, done));
            if (!PostMessageW(_sentinel, WM_APP_COMMAND, IntPtr.Zero, IntPtr.Zero))
                completion.TrySetException(new InvalidOperationException("VST host command could not be posted."));
            return completion.Task;
        }

        // 哨兵窗口过程：WM_APP_COMMAND → 执行宿主命令队列
        IntPtr SentinelWndProc(IntPtr hwnd, uint msg, IntPtr wp, IntPtr lp) {
            if (msg == WM_APP_COMMAND) {
                while (_queue.TryDequeue(out var item)) {
                    item.run();
                    item.done.Set();
                }
                return IntPtr.Zero;
            }
            return DefWindowProcW(hwnd, msg, wp, lp);
        }

        void ThreadMain() {
            // 注册隐藏哨兵窗口类（命令唤醒用——PostMessage 到 HWND，与线程 ID 无关）
            var wc = new WNDCLASSEX {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandleW(null),
                lpszClassName = _class,
            };
            RegisterClassExW(ref wc);
            _sentinel = CreateWindowExW(0, _class, "", 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
            _ready.Set();
            try {
                // GetMessage 阻塞式循环——泵编辑器窗口 + 哨兵窗口的消息；
                // 对跨线程 SendMessage（OTT 等插件通知渲染）天然可靠
                while (true) {
                    int status = GetMessageW(out MSG msg, IntPtr.Zero, 0, 0);
                    if (status <= 0) {
                        break; // WM_QUIT
                    }
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                    if (_disposed) {
                        break; // Dispose 已投递唤醒消息，命令处理完后退出
                    }
                }
            } catch (Exception ex) {
                Log.Error(ex, $"[VstThread] {_name} message loop crashed");
            } finally {
                if (_sentinel != IntPtr.Zero) {
                    DestroyWindow(_sentinel);
                    _sentinel = IntPtr.Zero;
                }
                UnregisterClassW(_class, wc.hInstance);
                // 线程退出：唤醒所有等待者（防止 Invoke 挂死）
                while (_queue.TryDequeue(out var item)) {
                    item.done.Set();
                }
                _wndProcHandle.Free();
            }
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            // 唤醒消息循环（GetMessage 阻塞中）检查退出标志
            PostMessageW(_sentinel, WM_APP_COMMAND, IntPtr.Zero, IntPtr.Zero);
            if (!_thread.Join(5000)) {
                Log.Warning($"[VstThread] {_name} did not exit within 5s");
            }
        }
    }
}
