using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;
using Xunit;

namespace OpenUtau.Test.App;

// The old vertical chain row is no longer used by the mixer or workspace.
[Collection("Theme")]
public class FxChainRowPresentationTests {
    static Window Mount(Control content, double width = 650) {
        var window = new Window { Width = width, Height = 330, Content = content };
        window.Show(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); return window;
    }
    static T Part<T>(Visual root, string name) where T : Control => root.GetVisualDescendants().OfType<T>().Single(v => v.Name == name);
    static UTrack EqTrack() {
        var track = new UTrack(); TrackRackCommands.Add(track, TrackEffectKind.Eq).Execute(); return track;
    }
    [AvaloniaFact]
    public void NativeSwitchUsesSharedThemeAndItsPowerActionReachesTheModel() {
        var track = EqTrack(); using var model = new FxChainViewModel(); model.Attach(track);
        var card = new EffectRackCard { DataContext = model.Rows[0] }; var window = Mount(card);
        try {
            var power = Part<ToggleButton>(card, "PowerToggle");
            var shape = Part<Border>(power, "PART_Track");
            Assert.Equal(new Size(34, 20), shape.Bounds.Size);
            Assert.True(power.Focusable); Assert.True(power.IsChecked);
            power.IsChecked = false; power.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(TrackEffectRack.IsPowered(track, TrackEffectRack.Read(track)[0]));
        } finally { window.Close(); }
    }
    [AvaloniaFact]
    public void LongVstTitlesStayInsideTheCardAndRemainAvailableInTheTooltip() {
        var track = new UTrack { VstSlots = new() { new VstPluginSlot(0) { PluginUid = new string('Q', 160) } } };
        using var model = new FxChainViewModel(); model.Attach(track);
        var card = new EffectRackCard { DataContext = model.Rows[0] }; var window = Mount(card);
        try {
            var label = Part<TextBlock>(card, "NameText");
            Assert.True(label.Bounds.Width < card.Bounds.Width);
            Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, label.TextTrimming);
            Assert.Equal(track.VstSlots[0].PluginUid, ToolTip.GetTip(label));
            Assert.Empty(card.GetVisualDescendants().OfType<Knob>());
        } finally { window.Close(); }
    }
    [AvaloniaFact]
    public void KeyboardReorderKeepsTheSelectedInstanceIdentity() {
        var track = EqTrack(); TrackRackCommands.Add(track, TrackEffectKind.Compressor).Execute();
        using var panel = new FxChainPanel { Track = track }; var window = Mount(panel);
        try {
            var card = panel.GetVisualDescendants().OfType<EffectRackCard>().First(); string id = panel.ViewModel.Rows[0].EffectId;
            card.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right, KeyModifiers = KeyModifiers.Alt });
            Assert.Equal(id, track.EffectRack![1].Id);
        } finally { window.Close(); }
    }
    [AvaloniaFact]
    public void RemountDisposesTheOldEditorAndCreatesAViewOfTheCurrentState() {
        var track = EqTrack(); using var model = new FxChainViewModel(); model.Attach(track);
        var card = new EffectRackCard { DataContext = model.Rows[0] }; var parent = new StackPanel(); parent.Children.Add(card);
        var window = Mount(parent);
        try {
            var old = card.GetVisualDescendants().OfType<BuiltInEffectView>().Single().ViewModel;
            parent.Children.Remove(card);
            var next = track.EffectRack![0].Settings!.Clone(); next.EqHighDb = 8;
            TrackRackCommands.Settings(track, track.EffectRack[0].Id, next).Execute();
            old.EqHighDb = 11; Assert.Equal(8, track.EffectRack[0].Settings!.EqHighDb);
            parent.Children.Add(card); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var current = card.GetVisualDescendants().OfType<BuiltInEffectView>().Single().ViewModel;
            Assert.NotSame(old, current); Assert.Equal(8, current.EqHighDb);
        } finally { window.Close(); }
    }
}
