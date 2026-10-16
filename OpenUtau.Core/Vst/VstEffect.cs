using System;
using System.Threading.Tasks;
using System.Threading;
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
        private IVstExportBridge? _exportBridge;
        private Func<long, VstExportContext>? _exportClock;
        private long _exportPosition;
        private int _exportLatency;
        public int LatencySamples => _exportLatency;
        // 原生编辑器窗口是否打开——Dispose 时必须先关闭（否则窗口悬空引用已卸载 handle）
        private bool _editorOpen;
        // Shared for all instances: plugin modules also keep process-wide GUI thread state.
        private VstThread HostThread => VstThread.Shared;
        public string? EditorError { get; private set; }
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
            _handle = HostThread.Invoke(() => _bridge.Load(_entry.Path));
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
            var setup = HostThread.Invoke(() => {
                bool ok = _bridge.Setup(_handle, sampleRate, maxBlockSize) && _bridge.Activate(_handle, true);
                return (Ok: ok, Error: ok ? null : _bridge.LastError());
            });
            if (!setup.Ok) {
                Log.Warning($"[VstEffect] Setup/activation failed: {setup.Error}");
                return;
            }
            _maxBlockSize = maxBlockSize;
            _processScratch = new float[maxBlockSize * 2];
            _isSetup = true;
            // IComponent.setActive belongs to the host GUI thread, never the audio callback.
            // Native setProcessing remains deferred to the first processing block.
            _isActivated = true;
        }

        // ── IEffect ────────────────────────────────────────────

        internal (byte[] State, VstExportParameter[] Parameters) CaptureExportState() => HostThread.Invoke(() => {
            if (_bridge is not IVstExportBridge export)
                throw new NotSupportedException("The VST bridge does not support independent export.");
            var state = _bridge.SaveState(_handle) ?? throw new InvalidOperationException($"Could not capture '{DisplayName}' for export.");
            return (state, export.CaptureExportParameters(_handle));
        });

        internal void SetupForExport(double sampleRate, int blockSize, bool offline,
            VstExportParameter[] parameters, Func<long, VstExportContext> clock) {
            if (_bridge is not IVstExportBridge export)
                throw new NotSupportedException("The VST bridge does not support independent export.");
            _processScratch = new float[checked(blockSize * 2)];
            HostThread.Invoke(() => {
                if (!export.SetupExport(_handle, sampleRate, blockSize, offline))
                    throw new InvalidOperationException($"'{DisplayName}' rejected {(offline ? "offline" : "realtime")} processing.");
                if (!export.RestoreExportParameters(_handle, parameters))
                    throw new InvalidOperationException($"Could not restore '{DisplayName}' parameters.");
                if (!_bridge.Activate(_handle, true))
                    throw new InvalidOperationException($"Could not activate '{DisplayName}' for export.");
                _isActivated = true; // Cleanup must deactivate even if parameter flushing fails.
                if (!export.ProcessExport(_handle, _processScratch, 0, clock(0)))
                    throw new InvalidOperationException($"Could not initialize '{DisplayName}' export parameters.");
                _exportLatency = export.GetLatencySamples(_handle);
                if (_exportLatency < 0) throw new InvalidOperationException($"'{DisplayName}' reported invalid latency.");
            });
            _maxBlockSize = blockSize;
            _exportBridge = export;
            _exportClock = clock;
            _isSetup = _isActivated = true;
        }

        internal void SetExportPosition(long position) => _exportPosition = position;
        void ProcessBlock(float[] buffer, int frames) {
            if (_exportBridge == null) { _bridge.Process(_handle, buffer, frames); return; }
            if (!_exportBridge.ProcessExport(_handle, buffer, frames, _exportClock!(_exportPosition)))
                throw new InvalidOperationException($"'{DisplayName}' failed during export.");
            if (_exportBridge.GetLatencySamples(_handle) != _exportLatency)
                throw new InvalidOperationException($"'{DisplayName}' changed latency during export. The delivery was stopped to preserve alignment.");
            _exportPosition += frames;
        }

        public bool IsBypassed => _slot.Bypassed;
        public void Process(float[] buffer, int offset, int count) {
            if (_slot.Bypassed || _handle == IntPtr.Zero || !_isSetup || !_isActivated) return;
            int frames = count / 2;
            if (frames <= 0) return;

            // Native processing truncates to the configured maximum frame count.
            // Device reads can be larger; split them without leaving a dry suffix.
            // Keep the usual small, offset-zero audio callback allocation-free.
            if (offset == 0 && frames <= _maxBlockSize && buffer.Length >= count) {
                ProcessBlock(buffer, frames);
            } else {
                if (_processScratch.Length < _maxBlockSize * 2)
                    _processScratch = new float[_maxBlockSize * 2];
                for (int processed = 0; processed < frames;) {
                    int blockFrames = Math.Min(_maxBlockSize, frames - processed);
                    int blockSamples = blockFrames * 2;
                    int sourceOffset = offset + processed * 2;
                    Array.Copy(buffer, sourceOffset, _processScratch, 0, blockSamples);
                    ProcessBlock(_processScratch, blockFrames);
                    Array.Copy(_processScratch, 0, buffer, sourceOffset, blockSamples);
                    processed += blockFrames;
                }
            }
        }

        public void Reset() {
            if (_handle != IntPtr.Zero) HostThread.Invoke(() => _bridge.Reset(_handle));
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
            return HostThread.Invoke(OpenEditorOnHost);
        }

        bool OpenEditorOnHost() {
            if (_handle == IntPtr.Zero) return false;
            Log.Information("[VstEffect] Opening native editor for {Plugin}", _entry.Name);
            bool ok = _bridge.OpenEditorWindow(_handle);
            // LastError is native thread-local storage; read it on the producing thread.
            EditorError = ok ? null : _bridge.LastError();
            if (ok) {
                _editorOpen = true;
                Log.Information($"[VstEffect] Native editor opened for '{_entry.Name}'");
            } else {
                Log.Warning($"[VstEffect] Editor failed for '{_entry.Name}': {EditorError}");
            }
            return ok;
        }

        /// <summary>异步打开编辑器：attached（秒级）在专用线程执行，UI 线程不阻塞。</summary>
        public Task<bool> OpenNativeEditorAsync() {
            if (_handle == IntPtr.Zero) return Task.FromResult(false);
            return HostThread.InvokeAsync(OpenEditorOnHost);
        }

        // ── State Persistence ──────────────────────────────────

        /// <summary>Save processor state (for .ustxp). Returns null on failure.</summary>
        public byte[]? SaveState() {
            if (_handle == IntPtr.Zero) return null;
            return HostThread.Invoke(() => _bridge.SaveState(_handle));
        }

        public Task<byte[]?> SaveStateAsync() => HostThread.InvokeAsync(() =>
            _handle == IntPtr.Zero ? null : _bridge.SaveState(_handle));

        internal byte[]? TrySaveState(TimeSpan budget) {
            if (_handle == IntPtr.Zero || budget <= TimeSpan.Zero) return null;
            using var cancellation = new CancellationTokenSource(budget);
            try {
                return HostThread.InvokeAsync(() => _bridge.SaveState(_handle), cancellation.Token)
                    .WaitAsync(cancellation.Token).GetAwaiter().GetResult();
            } catch (OperationCanceledException) {
                Log.Warning("[VstEffect] State capture deferred for {Plugin}; preserving previous state", DisplayName);
                return null;
            }
        }

        /// <summary>Restore processor state (from .ustxp). Returns true on success.</summary>
        public bool RestoreState(byte[]? data) {
            if (_handle == IntPtr.Zero || data == null || data.Length == 0) return false;
            return HostThread.Invoke(() => _bridge.RestoreState(_handle, data));
        }

        // ── IDisposable ────────────────────────────────────────

        public void Dispose() {
            if (_handle != IntPtr.Zero) {
                // 全部在专用线程执行（controller 线程一致 + 同线程 SendMessageW
                // 直接调 WndProc 关窗，无跨线程排队死锁）。
                // 先关闭原生编辑器窗口——GUI 打开时直接 vst_unload 会让窗口
                // 悬空引用已卸载的 handle → 原生崩溃（LoadProject ClearAll 场景）
                if (_editorOpen) {
                    HostThread.Invoke(() => _bridge.CloseEditor(_handle));
                    _editorOpen = false;
                }
                if (_isActivated) {
                    HostThread.Invoke(() => _bridge.Activate(_handle, false));
                    _isActivated = false;
                }
                HostThread.Invoke(() => _bridge.Unload(_handle));
                Log.Information($"[VstEffect] Disposed '{_entry.Name}' (0x{_handle:X})");
                _handle = IntPtr.Zero;
            }
            // The shared GUI thread survives instance removal/reload until process exit.
        }
    }
}
