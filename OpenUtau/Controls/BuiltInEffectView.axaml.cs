using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using OpenUtau.App.ViewModels;
using OpenUtau.Core.Ustx;

namespace OpenUtau.App.Controls;

public partial class BuiltInEffectView : UserControl, IDisposable {
    public bool IsEq { get; }
    public bool IsComp { get; }
    public bool IsReverb { get; }
    public MixFxViewModel ViewModel { get; }

    public BuiltInEffectView() : this(new UTrack(), string.Empty, TrackEffectKind.Eq) { }

    public BuiltInEffectView(UTrack track, string id, TrackEffectKind kind) {
        IsEq = kind == TrackEffectKind.Eq;
        IsComp = kind == TrackEffectKind.Compressor;
        IsReverb = kind == TrackEffectKind.Reverb;
        InitializeComponent();
        DataContext = ViewModel = new MixFxViewModel(track, id);
        ViewModel.AskForName = PromptForName;
        ModuleHost.ContentTemplate = (IDataTemplate)Resources["RackEditor" + kind]!;
        ModuleHost.Content = ViewModel;
        AddHandler(PointerPressedEvent, (_, e) => {
            if (e.Source is Control c && (c is Knob || c.GetVisualAncestors().Any(a => a is Knob))) ViewModel.Edits?.BeginGesture();
        }, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, (_, _) => ViewModel.FinishEdits(), RoutingStrategies.Bubble, true);
        Loaded += (_, _) => {
            foreach (var knob in this.GetLogicalDescendants().OfType<Knob>())
                knob.AddHandler(PointerCaptureLostEvent, (_, _) => ViewModel.FinishEdits(), RoutingStrategies.Direct, true);
        };
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) ViewModel.FinishEdits(); });
    }

    Task<string?> PromptForName() {
        var completion = new TaskCompletionSource<string?>();
        var dialog = new Views.TypeInDialog { Title = ThemeManager.GetString("mixfx.library.save") };
        dialog.SetText(string.Empty);
        string? name = null;
        dialog.onFinish = value => name = value;
        dialog.Closed += (_, _) => completion.TrySetResult(name);
        if (TopLevel.GetTopLevel(this) is Window owner) dialog.ShowDialog(owner); else dialog.Show();
        return completion.Task;
    }

    public void Dispose() => ViewModel.Dispose();
}
