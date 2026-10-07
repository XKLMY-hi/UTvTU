/// vst_bridge.h — C-ABI public exports for UTvTU VST host bridge
///
/// This DLL wraps Steinberg VST3 SDK host helpers behind a flat C API so
/// C# can call it via P/Invoke (DllImport).  Each call is thread-safe.

#ifndef VST_BRIDGE_H
#define VST_BRIDGE_H

#ifdef _WIN32
#  define VSTBRIDGE_API __declspec(dllexport)
#else
#  define VSTBRIDGE_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/// Opaque handle to a loaded plugin instance.
typedef struct VstBridgeInstance VstBridgeInstance;

/// ── Lifecycle ──────────────────────────────────────────────────────

/// Load a VST3 plugin from a bundle directory path (the .vst3 folder).
/// Returns NULL on failure; call vst_last_error() for details.
VSTBRIDGE_API VstBridgeInstance* vst_load(const wchar_t* bundlePath);

/// Destroy the plugin instance and free all resources.
VSTBRIDGE_API void vst_unload(VstBridgeInstance* inst);

/// ── Audio Setup ────────────────────────────────────────────────────

/// Configure the audio processor.  Must be called before vst_process().
/// Returns 0 on success, non-zero on failure.
VSTBRIDGE_API int vst_setup(VstBridgeInstance* inst,
                            double sampleRate,
                            int maxBlockSize);

/// Start/stop processing.  Call vst_activate(true) before the first
/// process call of a playback session; call vst_activate(false) after.
VSTBRIDGE_API int vst_activate(VstBridgeInstance* inst, int enable);

/// ── Audio Processing ───────────────────────────────────────────────

/// Process stereo-interleaved float samples in-place.
///   buffer  — stereo interleaved float[frameCount*2]
///   frames  — number of sample frames (per channel)
VSTBRIDGE_API void vst_process(VstBridgeInstance* inst,
                               float* buffer,
                               int    frames);

/// Reset the processor state (e.g. delay lines, envelopes).
VSTBRIDGE_API void vst_reset(VstBridgeInstance* inst);

/// ── Parameters ─────────────────────────────────────────────────────

/// Number of automatable parameters.
VSTBRIDGE_API int vst_get_num_params(VstBridgeInstance* inst);

/// Read / write normalised parameter value [0, 1].
VSTBRIDGE_API float vst_get_param(VstBridgeInstance* inst, int paramId);
VSTBRIDGE_API void  vst_set_param(VstBridgeInstance* inst,
                                  int paramId, float normalisedValue);

/// Human-readable parameter name (UTF-8).  Caller does NOT own the pointer.
VSTBRIDGE_API const char* vst_get_param_name(VstBridgeInstance* inst,
                                             int paramId);

/// Number of audio input / output buses.
VSTBRIDGE_API int vst_get_num_inputs(VstBridgeInstance* inst);
VSTBRIDGE_API int vst_get_num_outputs(VstBridgeInstance* inst);

/// ── Editor (Windows HWND) ──────────────────────────────────────────

/// Open the native editor and return its parent HWND (Windows only).
/// Returns NULL if the plugin has no editor or on non-Windows platforms.
/// The caller must size and position the returned HWND.
VSTBRIDGE_API void* vst_open_editor(VstBridgeInstance* inst, void* parentHwnd);

/// Close the native editor.
VSTBRIDGE_API void vst_close_editor(VstBridgeInstance* inst);

/// Open a popup Win32 window with the VST editor embedded.
/// Creates a window, embeds the plugin GUI, and runs a message loop
/// on a background thread.  Returns immediately.
/// Returns 1 if an editor window was opened, 0 otherwise.
VSTBRIDGE_API int vst_open_editor_window(VstBridgeInstance* inst);

/// ── State Persistence ────────────────────────────────────────────────

/// Save plugin state to buffer.  ioSize: in = buffer capacity, out = bytes written.
/// Returns 0 on success, -1 on failure, -2 if buffer too small (ioSize = needed).
VSTBRIDGE_API int vst_save_state(VstBridgeInstance* inst, char* outBuf, int* ioSize);

/// Restore plugin state from buffer.  Returns 0 on success, -1 on failure.
VSTBRIDGE_API int vst_restore_state(VstBridgeInstance* inst, const char* data, int size);

/// ── Lightweight Probe (scanning) ────────────────────────────────────

/// Probe a VST3 bundle or single-file DLL WITHOUT creating a component.
/// Reads the factory class info and writes a JSON summary to `jsonBuf`.
/// Returns 0 on success; call vst_last_error() on failure.
///
/// This is safe to call on instrument VSTs — no component is created.
VSTBRIDGE_API int vst_probe(const wchar_t* bundleOrDllPath,
                            char* jsonBuf, int jsonBufSize);

/// ── Error Reporting ────────────────────────────────────────────────

/// Last error message (UTF-8).  Thread-local; valid until the next
/// bridge call on the same thread.
VSTBRIDGE_API const char* vst_last_error(void);

#ifdef __cplusplus
}
#endif

#endif // VST_BRIDGE_H
