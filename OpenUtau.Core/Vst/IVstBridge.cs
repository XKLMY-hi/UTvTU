using System;

namespace OpenUtau.Core.Vst {
    /// <summary>
    /// Testable seam over the native VST3 bridge (vst_bridge.dll).
    /// Extracted from the static VstBridge class so that VstEffect lifecycle and
    /// concurrency logic can be tested with a fake bridge — no real plugin DLL
    /// required in unit tests.
    /// </summary>
    public interface IVstBridge {
        IntPtr Load(string bundlePath);
        void Unload(IntPtr handle);
        string? LastError();

        bool Setup(IntPtr handle, double sampleRate, int maxBlockSize);
        bool Activate(IntPtr handle, bool enable);
        void Process(IntPtr handle, float[] buffer, int sampleFrames);
        void Reset(IntPtr handle);

        int GetNumParams(IntPtr handle);
        float GetParam(IntPtr handle, int paramId);
        void SetParam(IntPtr handle, int paramId, float value);
        string GetParamName(IntPtr handle, int paramId);
        int GetNumInputs(IntPtr handle);
        int GetNumOutputs(IntPtr handle);

        IntPtr OpenEditor(IntPtr handle, IntPtr parentHwnd);
        bool OpenEditorWindow(IntPtr handle);
        bool OpenEditorWindow(IntPtr handle, IntPtr ownerHwnd) => OpenEditorWindow(handle);
        void CloseEditor(IntPtr handle);

        byte[]? SaveState(IntPtr handle);
        bool RestoreState(IntPtr handle, byte[] data);

        string? Probe(string path);
    }
}
