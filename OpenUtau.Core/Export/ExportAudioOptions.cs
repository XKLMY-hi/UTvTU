using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Export {
    public enum ExportWaveFormat { Pcm16, Pcm24, Float32 }
    public enum ExportProcessingMode { Offline, Realtime }

    // A job owns these values; later UI changes cannot redirect a running export.
    public sealed record ExportAudioOptions {
        public ExportWaveFormat Format { get; init; } = ExportWaveFormat.Pcm24;
        public ExportProcessingMode ProcessingMode { get; init; } = ExportProcessingMode.Offline;
        public int SampleRate { get; init; } = 44100;
        public int StartTick { get; init; }
        public int EndTick { get; init; } = -1;
        public double TailSeconds { get; init; }
        public bool BuiltInEffects { get; init; } = true;
        public bool VstEffects { get; init; } = true;
        public bool TrackVolumeAndPan { get; init; } = true;
        public double MasterGain { get; init; } = 1;
        public IReadOnlyCollection<int>? TrackIndices { get; init; }
        internal IReadOnlyDictionary<int, ExportTrackParameters>? TrackParameters { get; init; }
        internal IReadOnlyDictionary<int, Vst.VstEffect[]>? OwnedVstEffects { get; init; }
        internal System.Threading.CancellationToken Cancellation { get; init; }

        public void Validate() {
            if (!Enum.IsDefined(Format) || !Enum.IsDefined(ProcessingMode) || SampleRate is not (44100 or 48000 or 96000))
                throw new ArgumentException("Unsupported WAV format or sample rate.");
            if (StartTick < 0 || (EndTick != -1 && EndTick <= StartTick))
                throw new ArgumentException("The export range must end after its start.");
            if (!double.IsFinite(TailSeconds) || TailSeconds < 0 || TailSeconds > 30 ||
                !double.IsFinite(MasterGain) || MasterGain < 0)
                throw new ArgumentException("Invalid tail duration or master gain.");
        }
    }

    public sealed record ExportTarget(UTrack Track, int Index, string Name, string Path);
    internal sealed record ExportTrackParameters(double Volume, float Pan, UMixFx? Effects);

    public sealed class ExportDeliveryPlan {
        public UProject Project { get; }
        public ExportAudioOptions Audio { get; }
        public bool PerTrack { get; }
        public IReadOnlyList<ExportTarget> Targets { get; }
        public double StartSeconds { get; }
        public double DurationSeconds { get; }
        readonly Dictionary<UTrack, string> vstIdentities;

        public ExportDeliveryPlan(UProject project, string outputPath, bool perTrack,
            ExportAudioOptions options, IEnumerable<UTrack> selected) {
            options.Validate();
            Project = project;
            PerTrack = perTrack;
            string destination = System.IO.Path.GetFullPath(outputPath);
            if (!string.Equals(System.IO.Path.GetExtension(destination), ".wav", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose a WAV destination.");
            var selection = selected.Distinct().ToArray();
            if (selection.Any(t => !project.tracks.Contains(t))) throw new ArgumentException("A selected track no longer belongs to this project.");
            selection = selection.Where(t => !t.Muted && project.parts.Any(p => p.trackNo == project.tracks.IndexOf(t))).ToArray();
            if (selection.Length == 0) throw new ArgumentException("Select at least one unmuted track containing a part.");
            int end = options.EndTick == -1 ? project.EndTick : options.EndTick;
            StartSeconds = project.timeAxis.TickPosToMsPos(options.StartTick) / 1000;
            DurationSeconds = (project.timeAxis.TickPosToMsPos(end) / 1000) - StartSeconds;
            if (!double.IsFinite(DurationSeconds) || DurationSeconds <= 0) throw new ArgumentException("The project or export range is empty.");
            var indices = selection.Select(t => project.tracks.IndexOf(t)).ToArray();
            Audio = options with { EndTick = end, TrackIndices = Array.AsReadOnly(indices), MasterGain = perTrack ? 1 : options.MasterGain,
                TrackParameters = new ReadOnlyDictionary<int, ExportTrackParameters>(selection.ToDictionary(t => project.tracks.IndexOf(t),
                    t => new ExportTrackParameters(t.Volume, (float)t.Pan, t.MixFx?.Clone()))) };
            vstIdentities = selection.ToDictionary(t => t, VstIdentity);
            string directory = System.IO.Path.GetDirectoryName(destination)!;
            string prefix = System.IO.Path.GetFileNameWithoutExtension(destination);
            Targets = Array.AsReadOnly(selection.Select(t => {
                int index = project.tracks.IndexOf(t);
                string name = string.Concat(t.TrackName.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim(' ', '.');
                if (name.Length == 0) name = "Track";
                string path = perTrack ? System.IO.Path.Combine(directory, $"{prefix}_{index + 1:D2}_{name}.wav") : destination;
                return new ExportTarget(t, index, t.TrackName, path);
            }).ToArray());
        }

        public void ValidateTargets() {
            foreach (var target in Targets) {
                if (target.Index >= Project.tracks.Count || !ReferenceEquals(Project.tracks[target.Index], target.Track))
                    throw new OperationCanceledException("The project tracks changed during export.");
                if (Audio.VstEffects && vstIdentities[target.Track] != VstIdentity(target.Track))
                    throw new OperationCanceledException("The selected VST slots changed during export.");
            }
        }

        public bool NeedsDevice(ExportAudioOptions options) => options.VstEffects && Targets
            .Where(t => options.TrackIndices == null || options.TrackIndices.Contains(t.Index))
            .Any(t => t.Track.VstSlots?.Any(slot => slot.IsLoaded && !slot.Bypassed) == true);
        static string VstIdentity(UTrack track) => string.Join("\n", track.VstSlots?.Select(s => $"{s.SlotIndex}:{s.PluginUid}:{s.Bypassed}") ?? Array.Empty<string>());
    }
}
