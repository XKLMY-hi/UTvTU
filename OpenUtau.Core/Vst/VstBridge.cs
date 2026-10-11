using System;
using System.Runtime.InteropServices;
using Serilog;

namespace OpenUtau.Core.Vst {
    /// <summary>
    /// P/Invoke wrapper for the native VST3 bridge DLL (vst_bridge.dll).
    ///
    /// Follows the pattern established by Worldline.cs:
    ///   - [DllImport("vst_bridge")] with Cdecl calling convention
    ///   - Safe managed wrappers around raw P/Invoke calls
    ///   - Proper error handling with Serilog
    ///
    /// The bridge DLL is placed in runtimes/win-x64/native/ alongside worldline.dll.
    /// </summary>
    public static class VstBridge {
        private const string DllName = "vst_bridge";

        // ── Lifecycle ─────────────────────────────────────────────

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern IntPtr vst_load([MarshalAs(UnmanagedType.LPWStr)] string bundlePath);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void vst_unload(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr vst_last_error();

        /// <summary>Load a VST3 plugin from a bundle directory path.</summary>
        public static IntPtr Load(string bundlePath) {
            try {
                IntPtr handle = vst_load(bundlePath);
                if (handle == IntPtr.Zero) {
                    string? err = LastError();
                    Log.Warning($"[VST] Failed to load '{bundlePath}': {err ?? "unknown error"}");
                }
                return handle;
            } catch (Exception ex) {
                Log.Error(ex, $"[VST] Exception loading '{bundlePath}'");
                return IntPtr.Zero;
            }
        }

        /// <summary>Destroy the plugin instance and free all resources.</summary>
        public static void Unload(IntPtr handle) {
            if (handle == IntPtr.Zero) return;
            try { vst_unload(handle); }
            catch (Exception ex) { Log.Error(ex, "[VST] Exception unloading plugin"); }
        }

        // ── Audio Setup ───────────────────────────────────────────

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_setup(IntPtr handle, double sampleRate, int maxBlockSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_activate(IntPtr handle, int enable);

        /// <summary>Configure audio processing. Must call before Process.</summary>
        public static bool Setup(IntPtr handle, double sampleRate, int maxBlockSize) {
            if (handle == IntPtr.Zero) return false;
            return vst_setup(handle, sampleRate, maxBlockSize) == 0;
        }

        /// <summary>Activate (true) or deactivate (false) the processor.</summary>
        public static bool Activate(IntPtr handle, bool enable) {
            if (handle == IntPtr.Zero) return false;
            return vst_activate(handle, enable ? 1 : 0) == 0;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_setup_export(IntPtr handle, double sampleRate, int maxBlockSize, int offline);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_process_export(IntPtr handle, float[] buffer, int frames, long samplePosition,
            double quarterNotes, double tempo, int numerator, int denominator);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_get_latency_samples(IntPtr handle);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_get_export_parameter(IntPtr handle, int index, out int id, out float value);
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_set_export_parameter(IntPtr handle, int id, float value);

        public static bool SetupExport(IntPtr handle, double rate, int block, bool offline) => vst_setup_export(handle, rate, block, offline ? 1 : 0) == 0;
        public static bool ProcessExport(IntPtr handle, float[] buffer, int frames, VstExportContext c) =>
            vst_process_export(handle, buffer, frames, c.SamplePosition, c.QuarterNotes, c.Tempo, c.Numerator, c.Denominator) == 0;
        public static int GetLatencySamples(IntPtr handle) => vst_get_latency_samples(handle);
        public static VstExportParameter[] CaptureExportParameters(IntPtr handle) {
            var values = new System.Collections.Generic.List<VstExportParameter>();
            int count = GetNumParams(handle);
            for (int i = 0; i < count; i++) {
                int result = vst_get_export_parameter(handle, i, out int id, out float value);
                if (result == 1) continue; // Read-only output meters are not a settings snapshot.
                if (result != 0)
                    throw new InvalidOperationException("Could not capture a VST parameter.");
                values.Add(new VstExportParameter(id, value));
            }
            return values.ToArray();
        }
        public static bool RestoreExportParameters(IntPtr handle, VstExportParameter[] values) {
            foreach (var value in values)
                if (vst_set_export_parameter(handle, value.Id, value.Value) != 0) return false;
            return true;
        }

        // ── Audio Processing ──────────────────────────────────────

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void vst_process(IntPtr handle, float[] buffer, int frames);

        /// <summary>
        /// Process stereo-interleaved float samples in-place.
        /// buffer[0] = L0, buffer[1] = R0, buffer[2] = L1, buffer[3] = R1, ...
        /// </summary>
        public static void Process(IntPtr handle, float[] buffer, int sampleFrames) {
            if (handle == IntPtr.Zero) return;
            try { vst_process(handle, buffer, sampleFrames); }
            catch (Exception ex) { Log.Error(ex, "[VST] Exception processing audio"); }
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void vst_reset(IntPtr handle);

        /// <summary>Reset the processor state (delay lines, envelopes, etc.).</summary>
        public static void Reset(IntPtr handle) {
            if (handle == IntPtr.Zero) return;
            try { vst_reset(handle); }
            catch (Exception ex) { Log.Error(ex, "[VST] Exception resetting plugin"); }
        }

        // ── Parameters ────────────────────────────────────────────

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_get_num_params(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern float vst_get_param(IntPtr handle, int paramId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void vst_set_param(IntPtr handle, int paramId, float normalisedValue);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr vst_get_param_name(IntPtr handle, int paramId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_get_num_inputs(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_get_num_outputs(IntPtr handle);

        public static int GetNumParams(IntPtr handle) =>
            handle != IntPtr.Zero ? vst_get_num_params(handle) : 0;

        public static float GetParam(IntPtr handle, int paramId) =>
            handle != IntPtr.Zero ? vst_get_param(handle, paramId) : 0f;

        public static void SetParam(IntPtr handle, int paramId, float value) {
            if (handle == IntPtr.Zero) return;
            vst_set_param(handle, paramId, value);
        }

        public static string GetParamName(IntPtr handle, int paramId) {
            if (handle == IntPtr.Zero) return $"Param {paramId}";
            IntPtr ptr = vst_get_param_name(handle, paramId);
            return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) ?? $"Param {paramId}" : $"Param {paramId}";
        }

        public static int GetNumInputs(IntPtr handle) =>
            handle != IntPtr.Zero ? vst_get_num_inputs(handle) : 0;

        public static int GetNumOutputs(IntPtr handle) =>
            handle != IntPtr.Zero ? vst_get_num_outputs(handle) : 0;

        // ── Editor ────────────────────────────────────────────────

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr vst_open_editor(IntPtr handle, IntPtr parentHwnd);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void vst_close_editor(IntPtr handle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_open_editor_window(IntPtr handle);

        /// <summary>Open the native editor. parentHwnd is a Windows HWND.</summary>
        public static IntPtr OpenEditor(IntPtr handle, IntPtr parentHwnd) {
            if (handle == IntPtr.Zero) return IntPtr.Zero;
            try { return vst_open_editor(handle, parentHwnd); }
            catch (Exception ex) { Log.Error(ex, "[VST] Exception opening editor"); return IntPtr.Zero; }
        }

        /// <summary>
        /// Open a popup Win32 window with the VST editor embedded.
        /// The bridge creates the window and runs its own message loop.
        /// Returns true if an editor was opened.
        /// </summary>
        public static bool OpenEditorWindow(IntPtr handle) {
            if (handle == IntPtr.Zero) return false;
            try { return vst_open_editor_window(handle) == 1; }
            catch (Exception ex) { Log.Error(ex, "[VST] Exception opening editor window"); return false; }
        }

        /// <summary>Close the native editor.</summary>
        public static void CloseEditor(IntPtr handle) {
            if (handle == IntPtr.Zero) return;
            try { vst_close_editor(handle); }
            catch (Exception ex) { Log.Error(ex, "[VST] Exception closing editor"); }
        }

        // ── State Persistence ────────────────────────────────────

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_save_state(IntPtr handle, byte[] outBuf, ref int ioSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int vst_restore_state(IntPtr handle, byte[] data, int size);

        /// <summary>Save processor state. Returns null on failure.</summary>
        public static byte[]? SaveState(IntPtr handle) {
            if (handle == IntPtr.Zero) return null;
            try {
                byte[] buf = new byte[65536];
                int size = buf.Length;
                int r = vst_save_state(handle, buf, ref size);
                if (r == -2) { // buffer too small, retry
                    buf = new byte[size];
                    r = vst_save_state(handle, buf, ref size);
                }
                if (r != 0) return null;
                byte[] result = new byte[size];
                Array.Copy(buf, result, size);
                return result;
            } catch (Exception ex) {
                Log.Error(ex, "[VST] SaveState failed");
                return null;
            }
        }

        /// <summary>Restore processor state. Returns true on success.</summary>
        public static bool RestoreState(IntPtr handle, byte[] data) {
            if (handle == IntPtr.Zero || data == null || data.Length == 0) return false;
            try {
                return vst_restore_state(handle, data, data.Length) == 0;
            } catch (Exception ex) {
                Log.Error(ex, "[VST] RestoreState failed");
                return false;
            }
        }

        // ── Error ─────────────────────────────────────────────────

        /// <summary>Get the last error message from the bridge (thread-local).</summary>
        public static string? LastError() {
            try {
                IntPtr ptr = vst_last_error();
                return ptr != IntPtr.Zero ? Marshal.PtrToStringAnsi(ptr) : null;
            } catch { return null; }
        }

        // ── Probe (scanning) ───────────────────────────────────────

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        private static extern int vst_probe([MarshalAs(UnmanagedType.LPWStr)] string path, byte[] jsonBuf, int jsonBufSize);

        /// <summary>
        /// Probe a VST3 plugin WITHOUT creating a component.
        /// Returns factory metadata as JSON. Safe for instruments.
        /// </summary>
        public static string? Probe(string path) {
            try {
                byte[] buf = new byte[8192];
                int r = vst_probe(path, buf, buf.Length);
                if (r != 0) return null;
                // Trim trailing nulls
                int len = 0;
                while (len < buf.Length && buf[len] != 0) len++;
                return System.Text.Encoding.UTF8.GetString(buf, 0, len);
            } catch (Exception ex) {
                Log.Warning($"[VST] Probe failed for '{path}': {ex.Message}");
                return null;
            }
        }
    }
}
