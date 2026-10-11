using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenUtau.Core.Vst {
    /// <summary>
    /// Controllable fake bridge for unit-testing VstEffect lifecycle and concurrency
    /// without requiring real VST plugins or native DLLs.
    /// </summary>
    internal class FakeVstBridge : IVstBridge {
        /// <summary>Optional artificial Load delay — used to test load serialization.</summary>
        public TimeSpan LoadDelay { get; set; }

        public IntPtr Load(string bundlePath) {
            if (LoadDelay > TimeSpan.Zero) Thread.Sleep(LoadDelay);
            return new(++_nextHandle);
        }
        public void Unload(IntPtr handle) { _unloaded.Add(handle); }
        public string? LastError() => null;
        public bool Setup(IntPtr handle, double sr, int block) => true;
        private int _activateCalls;
        public int ActivateCalls => _activateCalls;
        public bool Activate(IntPtr handle, bool enable) { _activateCalls++; return true; }
        public Action<float[], int>? ProcessCallback { get; set; }
        public void Process(IntPtr handle, float[] buf, int frames) { ProcessCallback?.Invoke(buf, frames); }
        public void Reset(IntPtr handle) { }
        public int GetNumParams(IntPtr handle) => 0;
        public float GetParam(IntPtr handle, int id) => 0f;
        public void SetParam(IntPtr handle, int id, float v) { }
        public string GetParamName(IntPtr handle, int id) => $"P{id}";
        public int GetNumInputs(IntPtr handle) => 2;
        public int GetNumOutputs(IntPtr handle) => 2;
        public IntPtr OpenEditor(IntPtr handle, IntPtr hwnd) => IntPtr.Zero;
        public bool OpenEditorWindow(IntPtr handle) => false;
        public void CloseEditor(IntPtr handle) { }
        public byte[]? SaveState(IntPtr handle) => new byte[] { 1, 2, 3 };
        public bool RestoreState(IntPtr handle, byte[] data) => true;
        public string? Probe(string path) => null;

        private int _nextHandle;
        private readonly List<IntPtr> _unloaded = new();
        public IReadOnlyList<IntPtr> Unloaded => _unloaded;
    }
}
