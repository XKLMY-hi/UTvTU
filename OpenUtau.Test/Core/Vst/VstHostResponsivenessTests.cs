using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Core.Vst {
    [Collection("VstShared")]
    public class VstHostResponsivenessTests {
        const string Uid = "test:shared-gui-host";

        [Fact]
        public async Task ManyInstancesAndParallelGuiStateRequestsUseOneOwnerWithoutPoolWaiters() {
            var owners = new HashSet<int>();
            var bridge = new FakeVstBridge {
                LoadCallback = () => owners.Add(Environment.CurrentManagedThreadId),
                ActivateCallback = () => owners.Add(Environment.CurrentManagedThreadId),
                EditorCallback = () => { owners.Add(Environment.CurrentManagedThreadId); return true; },
                StateCallback = () => { owners.Add(Environment.CurrentManagedThreadId); return new byte[] { 9 }; },
            };
            var effects = Enumerable.Range(0, 24).Select(i => {
                string uid = Uid + (i % 3);
                VstTestSetup.Register(VstTestSetup.MakeEntry(uid));
                return new VstEffect(VstTestSetup.CreateSlot(uid, i % 3), bridge);
            }).ToArray();
            try {
                foreach (var fx in effects) { fx.Load(); fx.Setup(44100, 256); }
                // Audio processing runs on another thread without initialization callbacks.
                await Task.Run(() => {
                    var buffer = new float[512];
                    foreach (var fx in effects) fx.Process(buffer, 0, buffer.Length);
                });
                Assert.All(await Task.WhenAll(effects.Select(fx => fx.OpenNativeEditorAsync())).WaitAsync(TimeSpan.FromSeconds(3)), result => Assert.True(result));
                Assert.All(await Task.WhenAll(effects.Select(fx => fx.SaveStateAsync())).WaitAsync(TimeSpan.FromSeconds(3)),
                    state => Assert.Equal(new byte[] { 9 }, state));
                Assert.Single(owners);
                // Releasing some slots must not terminate the owner of the remaining slots.
                foreach (var fx in effects.Take(12)) fx.Dispose();
                Assert.All(await Task.WhenAll(effects.Skip(12).Select(fx => fx.OpenNativeEditorAsync())), result => Assert.True(result));
            } finally {
                foreach (var fx in effects) fx.Dispose();
            }
        }

        [Fact]
        public void InstancesAndReloadsShareTheirModuleGuiThread() {
            VstTestSetup.Register(VstTestSetup.MakeEntry(Uid));
            var owners = new HashSet<int>();
            var bridge = new FakeVstBridge {
                LoadCallback = () => owners.Add(Environment.CurrentManagedThreadId),
                EditorCallback = () => { owners.Add(Environment.CurrentManagedThreadId); return true; },
            };
            using (var a = new VstEffect(VstTestSetup.CreateSlot(Uid), bridge)) {
                using var b = new VstEffect(VstTestSetup.CreateSlot(Uid), bridge);
                a.Load(); b.Load();
                Assert.True(b.OpenNativeEditor());
            }
            using (var reloaded = new VstEffect(VstTestSetup.CreateSlot(Uid), bridge)) {
                reloaded.Load();
                Assert.True(reloaded.OpenNativeEditor());
            }
            Assert.Single(owners);
            Assert.DoesNotContain(Environment.CurrentManagedThreadId, owners);
        }

        [Fact]
        public void EditorErrorIsReadOnItsProducingThread() {
            VstTestSetup.Register(VstTestSetup.MakeEntry(Uid));
            using var error = new ThreadLocal<string?>();
            var bridge = new FakeVstBridge {
                EditorCallback = () => { error.Value = "native attach failure"; return false; },
                ErrorCallback = () => error.Value,
            };
            using var fx = new VstEffect(VstTestSetup.CreateSlot(Uid), bridge);
            fx.Load();
            Assert.False(fx.OpenNativeEditor());
            Assert.Equal("native attach failure", fx.EditorError);
            Assert.Null(error.Value);
        }

        [Fact]
        public async Task BusyCaptureReturnsWithinBudgetWithoutUnloadingRunningNativeCode() {
            VstTestSetup.Register(VstTestSetup.MakeEntry(Uid));
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var bridge = new FakeVstBridge {
                StateCallback = () => { entered.Set(); release.Wait(); return new byte[] { 7 }; },
            };
            using var fx = new VstEffect(VstTestSetup.CreateSlot(Uid), bridge);
            fx.Load();
            try {
                var capture = Task.Run(() => fx.TrySaveState(TimeSpan.FromMilliseconds(80)));
                Assert.True(entered.Wait(2000));
                Assert.Null(await capture.WaitAsync(TimeSpan.FromSeconds(2)));
                Assert.True(fx.IsLoaded);
                Assert.Empty(bridge.Unloaded);
            } finally { release.Set(); }
            Assert.Equal(new byte[] { 7 }, fx.SaveState());
        }

        [Fact]
        public void AutosavePreparationKeepsPreviousStateWhenGuiThreadIsBusy() {
            VstTestSetup.RunExclusive(() => {
                VstTestSetup.Register(VstTestSetup.MakeEntry(Uid));
                var manager = VstPluginManager.Inst;
                var previous = manager.Bridge;
                int trackNo = VstTestSetup.NextTrackNo();
                var slot = VstTestSetup.CreateSlot(Uid);
                slot.StateData = new byte[] { 42 };
                using var entered = new ManualResetEventSlim();
                using var release = new ManualResetEventSlim();
                try {
                    manager.Bridge = new FakeVstBridge();
                    Assert.NotNull(manager.LoadEffect(trackNo, slot));
                    var busy = VstThread.Shared.InvokeAsync(() => { entered.Set(); release.Wait(); return true; });
                    Assert.True(entered.Wait(2000));
                    var project = new UProject();
                    project.tracks[0].TrackNo = trackNo;
                    var elapsed = Stopwatch.StartNew();
                    project.BeforeSave(TimeSpan.FromMilliseconds(80));
                    Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(2));
                    Assert.Equal(new byte[] { 42 }, slot.StateData);
                    Assert.NotNull(project.voiceParts);
                    release.Set();
                    Assert.True(busy.Wait(2000));
                } finally {
                    release.Set();
                    manager.RemoveTrack(trackNo);
                    manager.Bridge = previous;
                }
            });
        }
    }
}
