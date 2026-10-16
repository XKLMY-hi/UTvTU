using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.App;

[Collection("WorkspaceSession")]
public class SingerPickerTests {
    sealed class Singer : USinger {
        readonly string id;
        static int nextLocation;
        public Singer(string id, bool exists = true) {
            this.id = id; found = exists;
            Location = "voice/" + id + "/" + System.Threading.Interlocked.Increment(ref nextLocation);
        }
        public override string Id => id;
        public override string Name => "テト " + id;
        public override string Location { get; }
        public override Dictionary<string, string> LocalizedNames => new() { ["en"] = "Teto " + id };
        public override IList<string> SearchTerms => new[] { "Kasane", "重音" };
        public int Loads;
        public override void EnsureLoaded() => Loads++;
    }

    [Fact]
    public void CatalogRetainsTheExistingWinnerAndReportsEachDuplicateLocationWithoutLoadingBanks() {
        var a = new Singer("same"); var b = new Singer("same"); var c = new Singer("other");
        var catalog = SingerManager.BuildCatalog(new[] { a, a, b, c });
        Assert.Equal(2, catalog.singers.Count);
        Assert.Same(a, catalog.singers["same"]);
        var duplicate = Assert.Single(catalog.duplicates);
        Assert.Equal(a.Location, duplicate.ActivePath);
        Assert.Equal(b.Location, Assert.Single(duplicate.IgnoredPaths));
        Assert.All(new[] { a, b, c }, s => Assert.Equal(0, s.Loads));
    }

    [AvaloniaFact]
    public void MetadataSearchRankingAndFavoritesDoNotLoadVoicebanksOrChangeCurrentSinger() {
        var a = new Singer("a"); var b = new Singer("b"); var c = new Singer("c");
        var missing = new Singer("missing", false);
        var favorites = new List<string> { "c" }; var recent = new List<string> { "b", "c" };
        int saves = 0;
        using var vm = new SingerPickerViewModel(missing, () => new[] { a, b, c, b, missing }, favorites, recent, () => saves++);
        Assert.Equal(new[] { "c", "b", "a" }, vm.Rows.Select(r => r.Singer.Id));
        Assert.Contains("missing", vm.CurrentName);
        foreach (var query in new[] { "teto", "ﾃﾄ", "てと", "KASANE", "重音" }) {
            vm.Search = query; Assert.Equal(3, vm.Rows.Count);
        }
        vm.Search = "kasane b"; Assert.Same(b, Assert.Single(vm.Rows).Singer);
        vm.Search = "none"; Assert.True(vm.IsEmpty);
        vm.Search = ""; vm.ToggleFavorite(vm.Rows.Single(r => r.Singer == a));
        Assert.Equal(1, saves); Assert.Contains("a", favorites);
        vm.Move(1); Assert.NotNull(vm.Selected);
        Assert.All(new[] { a, b, c, missing }, s => Assert.Equal(0, s.Loads));
    }

    [AvaloniaFact]
    public void TrackHeaderOpensThePickerAndEveryCloseOrDetachRemovesItsSubscription() {
        var header = new TrackHeader();
        var vm = new TrackHeaderViewModel { IsSingerVisible = true };
        header.ViewModel = vm; header.DataContext = vm;
        var window = new WindowEx { Content = header, Width = 800, Height = 600 };
        var subscriptions = (List<ICmdSubscriber>)typeof(DocManager).GetField("subscribers", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(DocManager.Inst);
        try {
            window.Show(); Dispatcher.UIThread.RunJobs(); int baseline = subscriptions.Count;
            for (int i = 0; i < 3; i++) {
                header.FindControl<Button>("SingerButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var flyout = Assert.IsType<Flyout>(typeof(TrackHeader).GetField("singerFlyout", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(header));
                Assert.IsType<SingerPicker>(flyout.Content);
                Assert.Equal(baseline + 1, subscriptions.Count);
                if (i < 2) flyout.Hide(); else window.Content = null;
                Dispatcher.UIThread.RunJobs(); Assert.Equal(baseline, subscriptions.Count);
            }
        } finally { window.Content = null; header.Dispose(); window.Close(); }
    }

    [AvaloniaFact]
    public void PickerVirtualizesLargeCatalogAndCommitsOnlyOnEnterThenEscapeClearsBeforeClosing() {
        var singers = Enumerable.Range(0, 500).Select(i => new Singer(i.ToString("D4"))).ToArray();
        using var vm = new SingerPickerViewModel(singers[0], () => singers, new List<string>(), new List<string>(), () => { });
        var picker = new SingerPicker { DataContext = vm, Width = 400, Height = 480 };
        var window = new WindowEx { Content = picker, Width = 420, Height = 500 };
        USinger chosen = null; int closes = 0;
        picker.Chosen += s => chosen = s; picker.CloseRequested += () => closes++;
        try {
            window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); picker.FocusSearch();
            Assert.InRange(picker.GetVisualDescendants().OfType<SingerAvatar>().Count(), 1, 30);
            void Key(Key key) => picker.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
            Key(Avalonia.Input.Key.Down); Assert.Null(chosen);
            Key(Avalonia.Input.Key.Enter); Assert.Same(vm.Selected.Singer, chosen);
            vm.Search = "missing"; Key(Avalonia.Input.Key.Escape); Assert.Equal("", vm.Search); Assert.Equal(0, closes);
            Key(Avalonia.Input.Key.Escape); Assert.Equal(1, closes);
            Assert.All(singers, s => Assert.Equal(0, s.Loads));
            vm.Open(); vm.Dispose();
            var before = vm.Rows;
            vm.OnNext(new SingersChangedNotification(), false); Dispatcher.UIThread.RunJobs(); Assert.Same(before, vm.Rows);
        } finally { window.Close(); }
    }
}
