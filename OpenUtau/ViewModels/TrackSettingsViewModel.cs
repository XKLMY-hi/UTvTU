using System;
using System.Linq;
using DynamicData.Binding;
using OpenUtau.App.Editing;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    class TrackSettingsViewModel : ViewModelBase {
        public UTrack Track { get; private set; }
        public ObservableCollectionExtended<IResampler> Resamplers => resamplers;
        [Reactive] public IResampler? Resampler { get; set; }
        [Reactive] public bool NeedsResampler { get; set; }
        public ObservableCollectionExtended<IWavtool> Wavtools => wavtools;
        [Reactive] public IWavtool? Wavtool { get; set; }
        [Reactive] public bool NeedsWavtool { get; set; }
        [Reactive] public bool IsNotClassic { get; set; }
        [Reactive] public GraphChoice? Graph { get; set; }
        public ObservableCollectionExtended<GraphChoice> Graphs { get; } = new();
        public sealed class GraphChoice {
            /// <summary>The graph's id; null for the renderer's default.</summary>
            public readonly string? Id;
            readonly string label;
            public GraphChoice(string? id, string label) {
                Id = id;
                this.label = label;
            }
            public override string ToString() => label;
        }


        ObservableCollectionExtended<IResampler> resamplers =
            new ObservableCollectionExtended<IResampler>();
        ObservableCollectionExtended<IWavtool> wavtools =
            new ObservableCollectionExtended<IWavtool>();
        readonly UProject project;

        public TrackSettingsViewModel(UTrack track) {
            project = DocManager.Inst.Project;
            ToolsManager.Inst.Initialize();
            Track = track;
            if (!string.IsNullOrEmpty(Track.RendererSettings.renderer)) {
                var renderer = Track.RendererSettings.renderer;
                resamplers.AddRange(ToolsManager.Inst.Resamplers);
                string? resamplerName = Track.RendererSettings.resampler;
                if (string.IsNullOrEmpty(resamplerName)) {
                    if (!Preferences.Default.DefaultResamplers.TryGetValue(renderer, out resamplerName)) {
                        resamplerName = string.Empty;
                    }
                }
                Resampler = ToolsManager.Inst.GetResampler(resamplerName);
                wavtools.AddRange(Renderers.GetSupportedWavtools(Resampler));
                string? wavtoolName = Track.RendererSettings.wavtool;
                if (string.IsNullOrEmpty(wavtoolName)) {
                    if (!Preferences.Default.DefaultWavtools.TryGetValue(renderer, out wavtoolName)) {
                        wavtoolName = string.Empty;
                    }
                }
                Wavtool = ToolsManager.Inst.GetWavtool(wavtoolName);
                NeedsResampler = Renderers.CLASSIC == renderer;
                NeedsWavtool = Renderers.CLASSIC == renderer;
                IsNotClassic = Renderers.CLASSIC != renderer;
                var project = DocManager.Inst.Project;
                var library = project.expressionGraphs ?? new System.Collections.Generic.List<Core.ExpressionGraph.UExpressionGraph>();
                // The Worldline-R variants share Worldline-R's Graphs.
                string slot = Renderers.GetExpressionGraphSlot(renderer);
                bool InSlot(Core.ExpressionGraph.UExpressionGraph g) =>
                    g.renderer != null && Renderers.GetExpressionGraphSlot(g.renderer) == slot;
                string? defaultId = null;
                project.defaultExpressionGraphs?.TryGetValue(slot, out defaultId);
                var defaultGraph = library.FirstOrDefault(g => g.id == defaultId && InSlot(g));
                string defaultName = defaultGraph != null
                    ? defaultGraph.name ?? defaultGraph.id
                    : ThemeManager.GetString("tracks.expressiongraph.none");
                Graphs.Add(new GraphChoice(null, $"{ThemeManager.GetString("tracks.expressiongraph.default")} ({defaultName})"));
                Graphs.AddRange(library.Where(InSlot).Select(g => new GraphChoice(g.id, g.name ?? g.id)));
                Graph = Graphs.FirstOrDefault(c => c.Id != null && c.Id == Track.ExpressionGraph) ?? Graphs[0];
            }
            this.WhenAnyValue(x => x.Resampler)
                .Subscribe(resampler => {
                    resampler?.CheckPermissions();
                    var wavtool = Wavtool;
                    wavtools.Clear();
                    wavtools.AddRange(Renderers.GetSupportedWavtools(resampler));
                    if (wavtool != null && wavtools.Contains(wavtool)) {
                        Wavtool = wavtool;
                    } else {
                        Wavtool = wavtools.FirstOrDefault();
                    }
                });
            this.WhenAnyValue(x => x.Wavtool)
                .Subscribe(wavtool => {
                    wavtool?.CheckPermissions();
                });
        }

        public void OpenResamplerLocation() {
            OS.OpenFolder(PathManager.Inst.ResamplersPath);
        }

        public void SetDefaultResampler() {
            if (Resampler != null) {
                Preferences.Default.DefaultResamplers[Track.RendererSettings.renderer] = Resampler.ToString() ?? string.Empty;
                Preferences.Save();
            }
        }

        public void OpenWavtoolLocation() {
            OS.OpenFolder(PathManager.Inst.WavtoolsPath);
        }

        public void SetDefaultWavtool() {
            if (Wavtool != null) {
                Preferences.Default.DefaultWavtools[Track.RendererSettings.renderer] = Wavtool.ToString() ?? string.Empty;
                Preferences.Save();
            }
        }

        public void Finish() {
            if (!ReferenceEquals(project, DocManager.Inst.Project) || !project.tracks.Contains(Track)) return;
            if (Graph != null && Graph.Id != Track.ExpressionGraph) {
                int index = project.tracks.IndexOf(Track);
                Core.ExpressionGraph.ExpressionGraphEdits.Apply(project, draft => draft.TrackOverrides[index] = Graph.Id);
            }
            if (Renderers.CLASSIC != Track.RendererSettings.renderer) return;
            using var edits = new DocumentEditGesture(project, "command.track.setting");
            if (!edits.CanExecute) return;
            var settings = Track.RendererSettings.Clone();
            settings.resampler = Resampler?.ToString() ?? string.Empty;
            settings.wavtool = Wavtool?.ToString() ?? string.Empty;
            edits.Execute(new TrackChangeRenderSettingCommand(project, Track, settings));
        }
    }
}
