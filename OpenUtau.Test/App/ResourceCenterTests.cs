using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Controls;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Theming;
using OpenUtau.Theming;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.App;

[Collection("WorkspaceSession")]
public sealed class ResourceCenterTests {
    sealed class Singer : USinger {
        readonly string id;
        public Singer(string id) { this.id = id; found = loaded = true; }
        public override string Id => id;
        public override string Name => id;
        public override USingerType SingerType => USingerType.Classic;
        public override IList<string> Errors => Array.Empty<string>();
        public override IList<USubbank> Subbanks => Array.Empty<USubbank>();
        public IList<UOto> Samples { get; set; }
        public override IList<UOto> Otos => Samples ??= new[] { UOto.OfDummy(id + "-a") };
        public byte[] ImageData { get; set; }
        public string ImagePath { get; set; }
        public int LoadCalls { get; private set; }
        public bool FailImageRead { get; set; }
        public override byte[] AvatarData => FailImageRead ? throw new IOException("thumbnail unavailable") : ImageData;
        public override string Avatar => ImagePath;
        public override void EnsureLoaded() => LoadCalls++;
    }

    static void WaitImage(Image image) {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (image.Source == null && DateTime.UtcNow < deadline) {
            Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(5);
        }
        Dispatcher.UIThread.RunJobs(); Assert.NotNull(image.Source);
    }

    [AvaloniaFact]
    public void VoicebankRowsShowRealSmallAvatarsWithoutLoadingUnselectedBanksAndRefreshAfterReload() {
        // A real PNG fixture: headless drawing's Save is a no-op and its decoder is a stub.
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgYGBgAAAABQABpfZFQAAAAABJRU5ErkJggg==");
        var file = Path.Combine(Path.GetTempPath(), "utvtu-avatar-" + Guid.NewGuid() + ".png");
        File.WriteAllBytes(file, bytes);
        var alpha = new Singer("Alpha") { ImageData = bytes };
        var beta = new Singer("Beta") { ImagePath = file };
        var missing = new Singer("Missing");
        using var vm = new SingersViewModel(new[] { alpha, beta, missing });
        var page = new SingersDialog { DataContext = vm };
        var window = new WindowEx { Width = 1200, Height = 800, Content = page };
        try {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var avatars = page.GetVisualDescendants().OfType<SingerAvatar>().ToArray();
            Assert.Equal(3, avatars.Length);
            var a = avatars.Single(c => c.Singer == alpha);
            var b = avatars.Single(c => c.Singer == beta);
            var m = avatars.Single(c => c.Singer == missing);
            var ai = a.GetVisualDescendants().OfType<Image>().Single();
            var bi = b.GetVisualDescendants().OfType<Image>().Single();
            WaitImage(ai); WaitImage(bi);
            Assert.InRange(Assert.IsType<Bitmap>(ai.Source).PixelSize.Width, 1, 72);
            Assert.InRange(Assert.IsType<Bitmap>(bi.Source).PixelSize.Width, 1, 72);
            Assert.Equal(0, beta.LoadCalls);
            Assert.Null(m.GetVisualDescendants().OfType<Image>().Single().Source);
            Assert.True(m.GetVisualDescendants().OfType<DawIcon>().Single().IsVisible);
            Assert.False(a.GetVisualDescendants().OfType<DawIcon>().Single().IsVisible);
            alpha.ImageData = null; vm.RefreshSinger(); Dispatcher.UIThread.RunJobs();
            Assert.Null(ai.Source);
            Assert.True(a.GetVisualDescendants().OfType<DawIcon>().Single().IsVisible);
            alpha.ImageData = bytes; vm.RefreshSinger(); Dispatcher.UIThread.RunJobs();
            WaitImage(ai); Assert.IsType<Bitmap>(ai.Source);
            beta.FailImageRead = true; vm.AvatarRevision++; Dispatcher.UIThread.RunJobs();
            Assert.Null(bi.Source);
            Assert.True(b.GetVisualDescendants().OfType<DawIcon>().Single().IsVisible);
            beta.FailImageRead = false; vm.AvatarRevision++; Dispatcher.UIThread.RunJobs();
            WaitImage(bi); Assert.IsType<Bitmap>(bi.Source);
            var previous = bi.Source;
            window.Content = null; Dispatcher.UIThread.RunJobs();
            Assert.Null(ai.Source); Assert.Null(bi.Source);
            window.Content = page; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var remounted = page.GetVisualDescendants().OfType<SingerAvatar>().Single(c => c.Singer == beta)
                .GetVisualDescendants().OfType<Image>().Single();
            WaitImage(remounted); Assert.IsType<Bitmap>(remounted.Source); Assert.Same(previous, remounted.Source);
            Assert.Equal(0, beta.LoadCalls);
        } finally { window.Close(); File.Delete(file); }
    }

    [AvaloniaFact]
    public void FilteringTheBrowserKeepsTheLoadedVoicebankUntilTheUserSelectsAnotherOne() {
        var alpha = new Singer("Alpha"); var beta = new Singer("Beta");
        using var vm = new SingersViewModel(new[] { alpha, beta });
        var page = new SingersDialog { DataContext = vm };
        var window = new WindowEx { Width = 1200, Height = 800, Content = page }; window.Show();
        try {
            Dispatcher.UIThread.RunJobs();
            Assert.Same(alpha, vm.Singer);
            window.UpdateLayout();
            var mel = page.GetVisualDescendants().OfType<ToggleButton>().Single(b => b.Classes.Contains("toolAction"));
            Assert.Equal(40, mel.Bounds.Height);
            mel.IsChecked = true; Dispatcher.UIThread.RunJobs();
            Assert.True(vm.ZoomInMel);
            Assert.Equal(ColorPool.Current.Color(Md3Role.OnSecondaryContainer), Assert.IsAssignableFrom<ISolidColorBrush>(mel.Foreground).Color);
            Assert.Equal(ColorPool.Current.Color(Md3Role.SecondaryContainer), Assert.IsAssignableFrom<ISolidColorBrush>(mel.Background).Color);
            foreach (var action in page.GetVisualDescendants().OfType<Button>().Where(b => b.Content is StackPanel && b.Classes.Contains("resourceAction"))) {
                Assert.Equal(action.Foreground, action.GetVisualDescendants().OfType<DawIcon>().Single().Fill);
                Assert.Equal(action.Foreground, action.GetVisualDescendants().OfType<TextBlock>().Single().Foreground);
            }
            vm.SearchSinger = "  BETA  "; Dispatcher.UIThread.RunJobs();
            Assert.Same(beta, Assert.Single(vm.FilteredSingers));
            Assert.Same(alpha, vm.Singer);
            Assert.Equal("Alpha-a", Assert.Single(vm.DisplayedOtos).Alias);
            vm.SearchSinger = "no-match"; Dispatcher.UIThread.RunJobs();
            Assert.True(vm.NoSingerMatches); Assert.Same(alpha, vm.Singer);
            vm.SearchSinger = "beta"; Dispatcher.UIThread.RunJobs();
            page.FindControl<ListBox>("SingerList")!.SelectedItem = beta;
            Dispatcher.UIThread.RunJobs();
            Assert.Same(beta, vm.Singer);
            Assert.Equal("Beta-a", Assert.Single(vm.DisplayedOtos).Alias);
            Assert.All(vm.SetEncodingMenuItems, item => Assert.NotNull(item.Command));
        } finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DetailsAndSearchSurviveResponsiveNavigationWithoutLosingOtoColumns() {
        var page = new SingersDialog();
        var window = new WindowEx { Width = 1200, Height = 800, Content = page }; window.Show();
        try {
            Dispatcher.UIThread.RunJobs();
            Assert.True(page.FindControl<Grid>("SingerBrowser")!.IsVisible);
            var surface = page.FindControl<Border>("SingerBrowserSurface")!;
            Assert.True(surface.IsVisible); Assert.True(surface.ClipToBounds);
            Assert.Equal(new CornerRadius(28), surface.CornerRadius);
            var options = page.FindControl<Button>("SingerOptions")!;
            Assert.Same(options.Content, Assert.Single(options.GetVisualDescendants().OfType<DawIcon>()));
            var search = page.FindControl<TextBox>("AliasSearch")!; search.Text = "retained";
            page.FindControl<Button>("SubbanksTab")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(page.FindControl<DataGrid>("SubbanksGrid")!.IsVisible);
            window.Width = 800; Dispatcher.UIThread.RunJobs();
            Assert.False(page.FindControl<Grid>("SingerBrowser")!.IsVisible);
            Assert.False(surface.IsVisible);
            Assert.True(page.FindControl<Grid>("SingerPicker")!.IsVisible);
            Assert.True(page.FindControl<DataGrid>("SubbanksGrid")!.IsVisible);
            page.FindControl<Button>("OtoTab")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("retained", search.Text);
            Assert.Equal(12, page.FindControl<DataGrid>("OtoGrid")!.Columns.Count);
            Assert.InRange(page.FindControl<DataGrid>("OtoGrid")!.Bounds.Height, 1, 800);
        } finally { window.Close(); }
    }

    static PackageRowViewModel RegistryRow(string id, string developer) => new(new RegistrySoftware {
        id = id, names = new() { ["en"] = id }, developers = new[] { developer },
        versions = new[] { new RegistryVersion { version = "2.0" } },
    });

    [AvaloniaFact]
    public void ReloadAndVoicebankChangesReapplyTheAliasFilterToTheCurrentSamples() {
        var alpha = new Singer("Alpha"); var beta = new Singer("Beta");
        using var vm = new SingersViewModel(new[] { alpha, beta });
        vm.SearchAlias = "  ALPHA  ";
        Assert.Single(vm.DisplayedOtos);
        vm.SelectedOto = vm.DisplayedOtos[0];
        alpha.Samples = new[] { UOto.OfDummy("Alpha-new"), UOto.OfDummy("unrelated") };
        vm.RefreshSinger();
        Assert.Equal("Alpha-new", Assert.Single(vm.DisplayedOtos).Alias);
        Assert.Same(vm.DisplayedOtos[0], vm.SelectedOto);
        vm.Singer = beta;
        Assert.Empty(vm.DisplayedOtos); Assert.Null(vm.SelectedOto);
        vm.GotoOto(beta, beta.Otos[0]);
        Assert.Equal(string.Empty, vm.SearchAlias);
        Assert.Same(beta.Otos[0], vm.SelectedOto);
    }

    [AvaloniaFact]
    public void PackageFiltersCombineSearchWithActualInstallationAndUpdateState() {
        using var vm = new PackageManagerViewModel(false);
        var fresh = RegistryRow("Fresh", "TeamA"); var update = RegistryRow("Update", "TeamB");
        var current = RegistryRow("Current", "TeamB");
        update.SetInstalled(new OudepMetadata { id = "Update", version = "1.0" });
        current.SetInstalled(new OudepMetadata { id = "Current", version = "2.0" });
        vm.Available.Add(fresh); vm.Available.Add(update); vm.Available.Add(current);
        vm.SearchText = " teamb "; Assert.Equal(2, vm.MatchCount);
        vm.FilterIndex = 2; Assert.Same(update, Assert.Single(vm.FilteredAvailable));
        update.InstalledVersion = "2.0"; Assert.True(vm.NoMatches);
        vm.FilterIndex = 1; Assert.Equal(2, vm.MatchCount);
        vm.SearchText = "2.0"; Assert.Equal(2, vm.MatchCount);
        current.IsInstalled = false; Assert.Equal(1, vm.MatchCount);
        vm.SearchText = "no-match"; Assert.True(vm.NoMatches);
    }

    [AvaloniaFact]
    public async Task AnUnavailableRegistryStillShowsLocalPackagesAndDoesNotInventUpdateAvailability() {
        using var vm = new PackageManagerViewModel(false,
            () => Task.FromException<List<RegistrySoftware>>(new InvalidOperationException("offline")),
            () => Task.FromResult(new List<OudepMetadata> { new() { id = "local", version = "1.0" } }));
        await vm.RefreshAsync();
        var local = Assert.Single(vm.Available);
        Assert.True(local.IsInstalled); Assert.False(local.CanInstallOrUpdate);
        vm.FilterIndex = 1; Assert.Same(local, Assert.Single(vm.FilteredAvailable));
        vm.FilterIndex = 2; Assert.True(vm.NoMatches);
    }

    [AvaloniaFact]
    public async Task ClosingAResourceSessionDiscardsItsLateRegistryResponse() {
        var response = new TaskCompletionSource<List<RegistrySoftware>>();
        int localReads = 0;
        var vm = new PackageManagerViewModel(false, () => response.Task, () => {
            localReads++; return Task.FromResult(new List<OudepMetadata>());
        });
        var refresh = vm.RefreshAsync(); vm.Dispose();
        response.SetResult(new() { new() { id = "late" } }); await refresh;
        Assert.Empty(vm.Available); Assert.Equal(0, localReads);
    }
}
