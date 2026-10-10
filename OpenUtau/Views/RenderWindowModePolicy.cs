using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenUtau.App.Views {
    /// <summary>The existing export paths' fixed capabilities, without audio or UI dependencies.</summary>
    internal sealed record RenderWindowModePolicy(bool IsMixdown, int SampleRate, bool IsBusy) {
        internal string FormatLabel => IsMixdown ? "WAV 32-bit float" : "WAV 16-bit";
        internal string SampleRateLabel => SampleRate.ToString(CultureInfo.InvariantCulture) + " Hz";
        internal string HintKey => IsMixdown ? "render.contract.mixdown" : "render.contract.pertrack";
        internal bool InputsEnabled => !IsBusy;
        internal bool TrackSelectionEnabled => !IsMixdown && !IsBusy;
        internal bool RangeSelectionEnabled => IsMixdown && !IsBusy;
        internal bool IncludesEffects => IsMixdown;
        internal bool IncludesTrackVolumeAndPan => IsMixdown;

        internal bool[] DisplayTrackSelection(IEnumerable<bool> selected) =>
            selected.Select(value => IsMixdown || value).ToArray();

        // Recording mixdown always consumes the entire project's live signal chain.
        // RenderTracks returns raw WaveMix stems, without track/master faders or FX.
        internal IReadOnlyCollection<int>? TrackFilter(IEnumerable<int> selected) =>
            IsMixdown ? null : selected.Distinct().ToArray();
    }
}
