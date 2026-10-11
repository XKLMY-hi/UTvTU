using System;

namespace OpenUtau.Core.Vst {
    public readonly record struct VstExportParameter(int Id, float Value);
    public readonly record struct VstExportContext(long SamplePosition, double QuarterNotes, double Tempo, int Numerator, int Denominator);

    // Separate capability keeps legacy playback bridges and third-party test seams compatible.
    public interface IVstExportBridge {
        bool SetupExport(IntPtr handle, double sampleRate, int maxBlockSize, bool offline);
        bool ProcessExport(IntPtr handle, float[] buffer, int frames, VstExportContext context);
        int GetLatencySamples(IntPtr handle);
        VstExportParameter[] CaptureExportParameters(IntPtr handle);
        bool RestoreExportParameters(IntPtr handle, VstExportParameter[] parameters);
    }
}
