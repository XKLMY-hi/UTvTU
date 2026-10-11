using System;
using OpenUtau.Core.Util;

namespace OpenUtau.Core.Vst {
    /// <summary>
    /// Production IVstBridge: delegates to the real static VstBridge P/Invoke layer.
    /// Guards non-Windows platforms so callers get graceful defaults instead of
    /// DllNotFoundException.
    /// </summary>
    public sealed class RealVstBridge : IVstBridge, IVstExportBridge {
        public static readonly RealVstBridge Instance = new();
        private RealVstBridge() { }

        private static bool IsWin => OS.IsWindows();

        public IntPtr Load(string bundlePath) => IsWin ? VstBridge.Load(bundlePath) : IntPtr.Zero;
        public void Unload(IntPtr handle) { if (IsWin) VstBridge.Unload(handle); }
        public string? LastError() => IsWin ? VstBridge.LastError() : null;
        public bool Setup(IntPtr handle, double sr, int block) => IsWin && VstBridge.Setup(handle, sr, block);
        public bool SetupExport(IntPtr handle, double sr, int block, bool offline) => IsWin && VstBridge.SetupExport(handle, sr, block, offline);
        public bool ProcessExport(IntPtr handle, float[] buffer, int frames, VstExportContext context) => IsWin && VstBridge.ProcessExport(handle, buffer, frames, context);
        public int GetLatencySamples(IntPtr handle) => IsWin ? VstBridge.GetLatencySamples(handle) : 0;
        public VstExportParameter[] CaptureExportParameters(IntPtr handle) => VstBridge.CaptureExportParameters(handle);
        public bool RestoreExportParameters(IntPtr handle, VstExportParameter[] parameters) => VstBridge.RestoreExportParameters(handle, parameters);
        public bool Activate(IntPtr handle, bool enable) => IsWin && VstBridge.Activate(handle, enable);
        public void Process(IntPtr handle, float[] buf, int frames) { if (IsWin) VstBridge.Process(handle, buf, frames); }
        public void Reset(IntPtr handle) { if (IsWin) VstBridge.Reset(handle); }
        public int GetNumParams(IntPtr handle) => IsWin ? VstBridge.GetNumParams(handle) : 0;
        public float GetParam(IntPtr handle, int id) => IsWin ? VstBridge.GetParam(handle, id) : 0f;
        public void SetParam(IntPtr handle, int id, float v) { if (IsWin) VstBridge.SetParam(handle, id, v); }
        public string GetParamName(IntPtr handle, int id) => IsWin ? VstBridge.GetParamName(handle, id) : $"P{id}";
        public int GetNumInputs(IntPtr handle) => IsWin ? VstBridge.GetNumInputs(handle) : 0;
        public int GetNumOutputs(IntPtr handle) => IsWin ? VstBridge.GetNumOutputs(handle) : 0;
        public IntPtr OpenEditor(IntPtr handle, IntPtr hwnd) => IsWin ? VstBridge.OpenEditor(handle, hwnd) : IntPtr.Zero;
        public bool OpenEditorWindow(IntPtr handle) => IsWin && VstBridge.OpenEditorWindow(handle);
        public void CloseEditor(IntPtr handle) { if (IsWin) VstBridge.CloseEditor(handle); }
        public byte[]? SaveState(IntPtr handle) => IsWin ? VstBridge.SaveState(handle) : null;
        public bool RestoreState(IntPtr handle, byte[] data) => IsWin && VstBridge.RestoreState(handle, data);
        public string? Probe(string path) => IsWin ? VstBridge.Probe(path) : null;
    }
}
