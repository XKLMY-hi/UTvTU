using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenUtau.Core;
using OpenUtau.Core.DiffSinger;
using OpenUtau.Core.Util;
using Xunit;

namespace OpenUtau.Test.Core.DiffSinger;

[Collection("WorkspaceSession")]
public sealed class OnnxLifecycleTests : IDisposable {
    readonly string previousRunner = Preferences.Default.OnnxRunner;
    public OnnxLifecycleTests() { Preferences.Default.OnnxRunner = "CPU"; }
    public void Dispose() { Preferences.Default.OnnxRunner = previousRunner; }
    static string Model(bool grouped) => Path.Combine(AppContext.BaseDirectory, "Files", "Onnx", grouped ? "duration-rel.onnx" : "duration-abs.onnx");

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FactoryRunsRealMinimalCpuModelsAndRetainsInputContracts(bool grouped, bool bytes) {
        using var session = bytes
            ? Onnx.getInferenceSession(File.ReadAllBytes(Model(grouped)))
            : Onnx.getInferenceSession(Model(grouped));
        var input = new System.Collections.Generic.List<NamedOnnxValue> {
            NamedOnnxValue.CreateFromTensor("encoder_out", new DenseTensor<float>(new float[] { 3, 7 }, new[] { 1, 2 })),
        };
        if (DiffSingerBasePhonemizer.DurationUsesWordInputs(null, session.InputNames)) {
            input.Add(NamedOnnxValue.CreateFromTensor("word_div", new DenseTensor<long>(new long[] { 1, 1 }, new[] { 1, 2 })));
            input.Add(NamedOnnxValue.CreateFromTensor("word_dur", new DenseTensor<long>(new long[] { 3, 7 }, new[] { 1, 2 })));
        }
        Onnx.VerifyInputNames(session, input);
        if (grouped) Assert.Throws<ArgumentException>(() => Onnx.VerifyInputNames(session, input.Take(1)));
        using var results = session.Run(input);
        Assert.Equal(new float[] { 3, 7 }, results.First().AsTensor<float>().ToArray());
    }

    [Theory]
    [InlineData("rel", false, true)]
    [InlineData("abs", true, false)]
    [InlineData(null, true, true)]
    [InlineData(null, false, false)]
    public void ExplicitDurationTypeWinsWhileOlderExportsProbeTheGraph(string kind, bool grouped, bool expected) {
        Assert.Equal(expected, DiffSingerBasePhonemizer.DurationUsesWordInputs(kind,
            grouped ? new[] { "encoder_out", "word_div", "word_dur" } : new[] { "encoder_out" }));
        var config = Yaml.DefaultDeserializer.Deserialize<DsConfig>("dur: duration.onnx\n" + (kind == null ? "" : "dur_type: " + kind));
        Assert.Equal(kind, config.dur_type);
    }

    [Fact]
    public void DirectMlScopeRemainsActiveForCachedSessionsAfterSwitchingBackToCpu() {
        Preferences.Default.OnnxRunner = "DirectML";
        using var started = new ManualResetEventSlim();
        Task other;
        using (Onnx.EnterDmlScope()) {
            Preferences.Default.OnnxRunner = "CPU";
            other = Task.Run(() => {
                started.Set();
                using var scope = Onnx.EnterDmlScope();
            });
            Assert.True(started.Wait(2000));
            Assert.False(other.Wait(50));
        }
        Assert.True(other.Wait(2000));
    }

    [Fact]
    public void SessionReleaseWaitsForTheActiveOperationAndNestedScopesAreReentrant() {
        var gate = new object();
        using var started = new ManualResetEventSlim();
        using var session = Onnx.getInferenceSession(Model(false), OnnxRunnerChoice.CPU);
        Task release;
        using (Onnx.EnterSessionScope(gate)) {
            using (Onnx.EnterSessionScope(gate)) Assert.True(Monitor.IsEntered(gate));
            release = Task.Run(() => {
                started.Set();
                using var scope = Onnx.EnterSessionScope(gate);
                session.Dispose();
            });
            Assert.True(started.Wait(2000));
            Assert.False(release.Wait(50));
            using var output = session.Run(new[] { NamedOnnxValue.CreateFromTensor("encoder_out", new DenseTensor<float>(new float[] { 2, 4 }, new[] { 1, 2 })) });
            Assert.Equal(4, output.First().AsTensor<float>()[0, 1]);
        }
        Assert.True(release.Wait(2000));
    }
}
