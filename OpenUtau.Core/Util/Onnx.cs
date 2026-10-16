using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using OpenUtau.Core.Util;
using Serilog;

namespace OpenUtau.Core {
    public class GpuInfo {
        public int deviceId;
        public string description = "";

        override public string ToString() {
            return $"[{deviceId}] {description}";
        }
    }

    public enum OnnxRunnerChoice {
        Default,
        CPU,
        CPUForCoreML,
    }

    public class Onnx {
        // Upstream 799ba8de: cached DirectML sessions retain their provider even
        // after the preference changes. Lock order: DmlLock -> singer.SessionLock.
        internal static readonly object DmlLock = new();
        private static volatile bool dmlInUse;

        public readonly struct DmlScope : IDisposable {
            readonly bool entered;
            internal DmlScope(bool entered) { this.entered = entered; }
            public void Dispose() {
                if (entered) System.Threading.Monitor.Exit(DmlLock);
            }
        }

        public static DmlScope EnterDmlScope() {
            System.Threading.Monitor.Enter(DmlLock);
            if (!dmlInUse && Preferences.Default.OnnxRunner != "DirectML") {
                System.Threading.Monitor.Exit(DmlLock);
                return default;
            }
            dmlInUse = true;
            return new DmlScope(true);
        }

        public readonly struct SessionScope : IDisposable {
            readonly DmlScope dml;
            readonly object gate;
            internal SessionScope(object gate) {
                dml = EnterDmlScope();
                this.gate = gate;
                try { System.Threading.Monitor.Enter(gate); }
                catch { dml.Dispose(); throw; }
            }
            public void Dispose() {
                System.Threading.Monitor.Exit(gate);
                dml.Dispose();
            }
        }

        public static SessionScope EnterSessionScope(object gate) => new(gate);
        private static readonly Lazy<Dictionary<int, OrtEpDevice>> deviceCache = new(initializeDevices);
        private static Dictionary<int, OrtEpDevice> devices => deviceCache.Value;

        private static Dictionary<int, OrtEpDevice> initializeDevices() {
            var env = OrtEnv.Instance();
            var ortDevices = env.GetEpDevices();

            return ortDevices
                .Where(device => device.EpName.ToLower().Contains("dml"))
                .Select((device, index) => new { index, device })
                .ToDictionary(x => x.index, x => x.device);
        }

        public static List<string> getRunnerOptions() {
            if (OS.IsWindows()) {
                return new List<string> {
                "CPU",
                "DirectML"
                };
            } else if (OS.IsMacOS()) {
                return new List<string> {
                "CPU",
                "CoreML"
                };
            } else if (OS.IsAndroid()) {
                return new List<string> {
                "CPU",
                "NNAPI"
                };
            }
            return new List<string> {
                "CPU"
            };
        }

        public static List<GpuInfo> getGpuInfo() {
            if (OS.IsAndroid()) {
                return new List<GpuInfo>{new GpuInfo {
                    deviceId = 0, // eliminate exception of taking OnnxGpuOptions[0]
                }};
            }
            List<GpuInfo> gpuList = new List<GpuInfo>();
            var env = OrtEnv.Instance();
            var ortDevices = env.GetEpDevices();

            var i = 0;
            foreach (var device in ortDevices.Where(device => device.EpName.ToLower().Contains("dml"))) {
                var description = "";
                foreach (var item in device.HardwareDevice.Metadata.Entries) {
                    if (item.Key.ToLower() == "description") {
                        description = $"{item.Value} ({device.HardwareDevice.Type})";
                        break;
                    }
                }
                if (string.IsNullOrEmpty(description)) { // fallback
                    description = $"{device.EpName} {device.HardwareDevice.Vendor} ({device.HardwareDevice.Type})";
                }
                devices[i] = device;
                gpuList.Add(new GpuInfo {
                    deviceId = i++,
                    description = description
                });
            }
            if (gpuList.Count == 0) {
                gpuList.Add(new GpuInfo {
                    deviceId = 0,
                });
            }
            return gpuList;
        }

        private static SessionOptions getOnnxSessionOptions(bool coremlEnableOnSubgraphs = false) {
            SessionOptions options = new SessionOptions();
            List<string> runnerOptions = getRunnerOptions();
            string runner = Preferences.Default.OnnxRunner;
            if (String.IsNullOrEmpty(runner)) {
                runner = runnerOptions[0];
            }
            if (!runnerOptions.Contains(runner)) {
                runner = "CPU";
            }
            switch (runner) {
                case "DirectML":
                    options.EnableMemoryPattern = false;
                    options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                    var d = devices[Preferences.Default.OnnxGpu];
                    options.AppendExecutionProvider(
                        OrtEnv.Instance(),
                        new List<OrtEpDevice> { d },
                        new Dictionary<string, string> { }
                     );
                    break;
                case "CoreML":
                    // Note: MLProgram format has stricter validation and may fail with complex DiffSinger models
                    // that have topological sorting issues (e.g., variance_predictor with diffusion embeddings)
                    // so we always use NeuralNetwork format (default) as MLProgram fails with complex models.
                    options.AppendExecutionProvider("CoreML", new Dictionary<string, string> {
                        { "MLComputeUnits", "ALL" },
                        { "RequireStaticInputShapes", "1"},
                        { "ModelFormat", "NeuralNetwork"},
                        { "EnableOnSubgraphs", coremlEnableOnSubgraphs ? "1" : "0" }  // Disable subgraph processing to avoid complex control flow issues
                    });
                    break;
                case "NNAPI":
                    options.AppendExecutionProvider_Nnapi();
                    break;
            }
            return options;
        }

        public static InferenceSession getInferenceSession(byte[] model, OnnxRunnerChoice runnerChoice = OnnxRunnerChoice.Default) {
            if (runnerChoice == OnnxRunnerChoice.CPU ||
                (runnerChoice == OnnxRunnerChoice.CPUForCoreML && Preferences.Default.OnnxRunner == "CoreML")) {
                return new InferenceSession(model);
            } else {
                using var dmlScope = EnterDmlScope();
                // Try with CoreML subgraphs enabled first, fallback to default if it fails
                if (OS.IsMacOS() && Preferences.Default.OnnxRunner == "CoreML") {
                    try {
                        using var options = getOnnxSessionOptions(coremlEnableOnSubgraphs: true);
                        return new InferenceSession(model, options);
                    } catch (Exception e) {
                        Log.Warning(e, "Failed to create session with CoreML subgraphs enabled, falling back to default settings");
                    }
                }
                return CreateSession(options => new InferenceSession(model, options), () => new InferenceSession(model));
            }
        }

        public static InferenceSession getInferenceSession(string modelPath, OnnxRunnerChoice runnerChoice = OnnxRunnerChoice.Default) {
            if (runnerChoice == OnnxRunnerChoice.CPU ||
                (runnerChoice == OnnxRunnerChoice.CPUForCoreML && Preferences.Default.OnnxRunner == "CoreML")) {
                return new InferenceSession(modelPath);
            } else {
                using var dmlScope = EnterDmlScope();
                // Try with CoreML subgraphs enabled first, fallback to default if it fails
                if (OS.IsMacOS() && Preferences.Default.OnnxRunner == "CoreML") {
                    try {
                        using var options = getOnnxSessionOptions(coremlEnableOnSubgraphs: true);
                        return new InferenceSession(modelPath, options);
                    } catch (Exception e) {
                        Log.Warning(e, "Failed to create session with CoreML subgraphs enabled, falling back to default settings");
                    }
                }
                return CreateSession(options => new InferenceSession(modelPath, options), () => new InferenceSession(modelPath));
            }
        }

        private static InferenceSession CreateSession(Func<SessionOptions, InferenceSession> create, Func<InferenceSession> cpu) {
            try {
                // Upstream 4798078a: keep the SafeHandle alive for the complete
                // native constructor, then release the temporary options deterministically.
                using var options = getOnnxSessionOptions();
                return create(options);
            } catch (Exception e) when (Preferences.Default.OnnxRunner == "DirectML") {
                Log.Warning(e, "Failed to create a DirectML inference session, falling back to CPU.");
                return cpu();
            }
        }

        public static void VerifyInputNames(InferenceSession session, IEnumerable<NamedOnnxValue> inputs) {
            var sessionInputNames = session.InputNames.ToHashSet();
            var givenInputNames = inputs.Select(v => v.Name).ToHashSet();
            var missing = sessionInputNames
                .Except(givenInputNames)
                .OrderBy(s => s, StringComparer.InvariantCulture)
                .ToArray();
            if (missing.Length > 0) {
                throw new ArgumentException("Missing input(s) for the inference session: " + string.Join(", ", missing));
            }
            var unexpected = givenInputNames
                .Except(sessionInputNames)
                .OrderBy(s => s, StringComparer.InvariantCulture)
                .ToArray();
            if (unexpected.Length > 0) {
                throw new ArgumentException("Unexpected input(s) for the inference session: " + string.Join(", ", unexpected));
            }
        }
    }
}
