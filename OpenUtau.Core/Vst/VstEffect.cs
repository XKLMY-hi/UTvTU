using System;
using System.Threading.Tasks;
using OpenUtau.Core.SignalChain.Effects;
using Serilog;

namespace OpenUtau.Core.Vst {
    /// <summary>
    /// A loaded VST plugin instance — the single source of truth.
    /// Owns the native bridge handle.  Shared between RenderEngine
    /// (audio processing) and VstEditorWindow (native GUI).
    /// </summary>
    public class VstEffect : IEffect, IDisposable {
        private readonly VstPluginSlot _slot;
        private readonly VstPluginEntry _entry;
        private readonly IVstBridge _bridge;
        private IntPtr _handle;
        private bool _isSetup;
        private bool _isActivated;
        private int _maxBlockSize = 4096;
        private float[] _processScratch = Array.Empty<float>();
        // 原生编辑器窗口是否打开——Dispose 时必须先关闭（否则窗口悬空引用已卸载 handle）
        private bool _editorOpen;
        // 专用 VST 线程：controller 创建/attached/编辑器消息循环同线程（插件线程敏感 + 消息泵）
        private VstThread? _vstThread;
        private VstThread VstThread => _vstThread ??= new VstThread($"VST:{_entry.Name}");
        public VstPluginSlot Slot => _slot;
        public VstPluginEntry Entry => _entry;
        public string DisplayName => _entry.Name;
        public bool IsLoaded => _handle != IntPtr.Zero;
        public IntPtr GetBridgeHandle() => _handle;

        public VstEffect(VstPluginSlot slot, IVstBridge? bridge = null) {
            _slot = slot;
            _bridge = bridge ?? RealVstBridge.Instance;
            _entry = VstPluginRegistry.Inst.TryGet(slot.PluginUid)
                     ?? throw new InvalidOperationException($"Plugin not found: {slot.PluginUid}");
        }

        // ── Lifecycle ──────────────────────────────────────────

        public void Load() {
            if (_handle != IntPtr.Zero) return;
            if (!_entry.IsEffect)
                throw new InvalidOperationException(
                    $"Cannot load instrument '{_entry.Name}' as an effect.");

            // controller 必须在专用线程创建——编辑器 createView/attached 期望
            // 同线程（Persistent Q 等线程敏感插件，桥接层实测 mismatched 线程
            // 挂起），attached 期间插件等待的消息也由该线程泵出（无死锁）。
            // UI 线程经 Invoke marshal，不阻塞。
            _handle = VstThread.Invoke(() => _bridge.Load(_entry.Path));
            if (_handle == IntPtr.Zero) {
                string? err = _bridge.LastError();
                throw new InvalidOperationException(
                    $"Failed to load '{_entry.Name}': {err ?? "unknown"} (bridge={_bridge.GetType().Name})");
            }
            Log.Information($"[VstEffect] Loaded '{_entry.Name}' (0x{_handle:X})");
        }

        public void Setup(double sampleRate, int maxBlockSize) {
            if (_handle == IntPtr.Zero || _isSetup) return;
            if (maxBlockSize <= 0 || maxBlockSize > int.MaxValue / 2)
                throw new ArgumentOutOfRangeException(nameof(maxBlockSize));
            if (!VstThread.Invoke(() => _bridge.Setup(_handle, sampleRate, maxBlockSize))) {
                Log.Warning($"[VstEffect] Setup failed: {_bridge.LastError()}");
                return;
            }
            _maxBlockSize = maxBlockSize;
            _processScratch = new float[maxBlockSize * 2];
            _isSetup = true;
            // Activate is deferred to first Process() call —
            // avoids keeping plugins hot while idle.
        }

        // ── IEffect ────────────────────────────────────────────

        public bool IsBypassed => _slot.Bypassed;
        public void Process(float[] buffer, int offset, int count) {
            if (_slot.Bypassed || _handle == IntPtr.Zero) return;
            EnsureActivated();
            int frames = count / 2;
            if (frames <= 0) return;

            // Native processing truncates to the configured maximum frame count.
            // Device reads can be larger; split them without leaving a dry suffix.
            // Keep the usual small, offset-zero audio callback allocation-free.
            if (offset == 0 && frames <= _maxBlockSize && buffer.Length >= count) {
                _bridge.Process(_handle, buffer, frames);
            } else {
                if (_processScratch.Length < _maxBlockSize * 2)
                    _processScratch = new float[_maxBlockSize * 2];
                for (int processed = 0; processed < frames;) {
                    int blockFrames = Math.Min(_maxBlockSize, frames - processed);
                    int blockSamples = blockFrames * 2;
                    int sourceOffset = offset + processed * 2;
                    Array.Copy(buffer, sourceOffset, _processScratch, 0, blockSamples);
                    _bridge.Process(_handle, _processScratch, blockFrames);
                    Array.Copy(_processScratch, 0, buffer, sourceOffset, blockSamples);
                    processed += blockFrames;
                }
            }
        }

        private void EnsureActivated() {
            if (_isActivated || !_isSetup) return;
            _bridge.Activate(_handle, true);
            _isActivated = true;
        }
        public void Reset() {
            if (_handle != IntPtr.Zero) _bridge.Reset(_handle);
        }

        // ── Native GUI ─────────────────────────────────────────

        /// <summary>
        /// Open native editor in a Win32 popup window.
        /// Each call creates a new window (multiple windows allowed).
        /// </summary>
        public bool OpenNativeEditor() {
            if (_handle == IntPtr.Zero) return false;
            // attached 在专用线程执行（= controller 线程）：线程敏感检查通过；
            // attached 秒级初始化只阻塞专用线程；插件在 attached 中
            // PostMessage 等待的消息由同一线程泵出。调用线程（UI）同步等待——
            // 大插件秒级会短暂卡调用线程，重插件请用异步版。
            bool ok = VstThread.Invoke(() => _bridge.OpenEditorWindow(_handle));
            if (ok) {
                _editorOpen = true;
                Log.Information($"[VstEffect] Native editor opened for '{_entry.Name}'");
            } else {
                Log.Warning($"[VstEffect] Editor failed for '{_entry.Name}': {_bridge.LastError()}");
            }
            return ok;
        }

        /// <summary>异步打开编辑器：attached（秒级）在专用线程执行，UI 线程不阻塞。</summary>
        public Task<bool> OpenNativeEditorAsync() {
            if (_handle == IntPtr.Zero) return Task.FromResult(false);
            return Task.Run(() => OpenNativeEditor());
        }

        // ── State Persistence ──────────────────────────────────

        /// <summary>Save processor state (for .ustxp). Returns null on failure.</summary>
        public byte[]? SaveState() {
            if (_handle == IntPtr.Zero) return null;
            return VstThread.Invoke(() => _bridge.SaveState(_handle));
        }

        /// <summary>Restore processor state (from .ustxp). Returns true on success.</summary>
        public bool RestoreState(byte[]? data) {
            if (_handle == IntPtr.Zero || data == null || data.Length == 0) return false;
            return VstThread.Invoke(() => _bridge.RestoreState(_handle, data));
        }

        // ── IDisposable ────────────────────────────────────────

        public void Dispose() {
            if (_handle != IntPtr.Zero) {
                // 全部在专用线程执行（controller 线程一致 + 同线程 SendMessageW
                // 直接调 WndProc 关窗，无跨线程排队死锁）。
                // 先关闭原生编辑器窗口——GUI 打开时直接 vst_unload 会让窗口
                // 悬空引用已卸载的 handle → 原生崩溃（LoadProject ClearAll 场景）
                if (_editorOpen) {
                    VstThread.Invoke(() => _bridge.CloseEditor(_handle));
                    _editorOpen = false;
                }
                if (_isActivated) {
                    VstThread.Invoke(() => _bridge.Activate(_handle, false));
                    _isActivated = false;
                }
                VstThread.Invoke(() => _bridge.Unload(_handle));
                Log.Information($"[VstEffect] Disposed '{_entry.Name}' (0x{_handle:X})");
                _handle = IntPtr.Zero;
                _vstThread?.Dispose();
                _vstThread = null;
            }
        }
    }
}
