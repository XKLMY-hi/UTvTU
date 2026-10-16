using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reactive.Linq;
using Avalonia.Headless.XUnit;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using Xunit;

namespace OpenUtau.Test.App;

[Collection("WorkspaceSession")]
public class RequiredPackageTests {
    [Fact]
    public void FindsNestedDeduplicatedIdsThroughRenderErrorWrappingWithoutTreatingPathsAsPackages() {
        var missing = new MissingPackageException("pc-nsf-hifigan");
        var wrapped = new MessageCustomizableException("render failed", "other", missing);
        var error = new AggregateException(wrapped, new Exception("render", missing),
            new MissingPackageException("../model"), new MissingPackageException("C:\\model"));
        Assert.Equal(new[] { "pc-nsf-hifigan" }, MissingPackageException.Find(error));
        Assert.Empty(MissingPackageException.Find(new Exception("please install game")));
    }
    [AvaloniaFact]
    public async Task RequiredPackageWaitsForTheExistingRegistryRequestAndInstallsOnlyTheExactId() {
        var registry = new TaskCompletionSource<List<RegistrySoftware>>();
        var installed = new List<OudepMetadata>();
        var game = new RegistrySoftware { id = "game", names = new() { ["en"] = "GAME" }, versions = new[] { new RegistryVersion { version = "1.0" } } };
        int reads = 0, installs = 0;
        using var vm = new PackageManagerViewModel(false, () => { reads++; return registry.Task; },
            () => Task.FromResult(installed), (software, _) => {
                Assert.Same(game, software); installs++; installed.Clear(); installed.Add(new() { id = "game", version = "1.0" }); return Task.CompletedTask;
            });
        var refresh = vm.RefreshAsync(); var request = vm.InstallRequiredAsync("game");
        registry.SetResult(new() { game }); await refresh;
        Assert.True(await request); Assert.Equal(1, installs);
        Assert.False(await vm.InstallRequiredAsync("gam")); Assert.Equal(1, installs);
        Assert.False(await vm.InstallRequiredAsync("../game")); Assert.Equal(1, installs);
        Assert.True(await vm.InstallRequiredAsync("GAME")); Assert.Equal(2, installs); // Repair missing content despite an installed receipt.
        Assert.False(vm.IsInstalling);
    }
    [AvaloniaFact]
    public async Task InstallationDisablesConflictingCommandsAndRejectsRepeatedRequests() {
        var pending = new TaskCompletionSource(); int installs = 0;
        using var vm = new PackageManagerViewModel(false,
            () => Task.FromResult(new List<RegistrySoftware> { new() { id = "game", versions = new[] { new RegistryVersion { version = "1" } } } }),
            () => Task.FromResult(new List<OudepMetadata>()),
            (_, _) => { installs++; return pending.Task; });
        var install = vm.InstallRequiredAsync("game");
        Assert.True(vm.IsInstalling);
        Assert.False(await vm.InstallCommand.CanExecute.FirstAsync());
        Assert.False(await vm.UninstallCommand.CanExecute.FirstAsync());
        Assert.False(await vm.RefreshCommand.CanExecute.FirstAsync());
        Assert.False(await vm.InstallRequiredAsync("game")); Assert.Equal(1, installs);
        pending.SetResult(); Assert.True(await install);
        Assert.False(vm.IsInstalling); Assert.True(await vm.RefreshCommand.CanExecute.FirstAsync());
    }

    [AvaloniaFact]
    public async Task ClosingBeforeTheRegistryReturnsNeverStartsInstallation() {
        var response = new TaskCompletionSource<List<RegistrySoftware>>(); int installs = 0;
        var vm = new PackageManagerViewModel(false, () => response.Task, () => Task.FromResult(new List<OudepMetadata>()),
            (_, _) => { installs++; return Task.CompletedTask; });
        var request = vm.InstallRequiredAsync("game"); vm.Dispose();
        response.SetResult(new() { new() { id = "game" } });
        Assert.False(await request); Assert.Equal(0, installs);
    }
}
