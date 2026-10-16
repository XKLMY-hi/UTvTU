using System;
using OpenUtau.App.ViewModels;
using Xunit;

namespace OpenUtau.Test.App;

public class UpdaterReleaseTests {
    static UpdaterViewModel.GithubRelease Release(string tag, bool preview = false, long id = 1) => new() {
        tag_name = tag, html_url = "https://github.com/XKLMY-hi/UTvTU/releases/tag/" + tag, prerelease = preview, id = id,
    };
    [Theory]
    [InlineData("UTvTU-26.1.2-030405", true)]
    [InlineData("UTvTU-26.10.17-013015+abc123", true)]
    [InlineData("UTvTU-50.12.31-235959", true)]
    [InlineData("UTvTU-26.2.30-013015", false)]
    [InlineData("UTvTU-26.10.17-253015", false)]
    [InlineData("UTvTU-26.10.17-013015-beta", false)]
    public void BuildStampIsCalendarValidatedAndSupportsUnpaddedDates(string tag, bool valid) {
        Assert.Equal(valid, UpdaterViewModel.TryParseBuildTag(tag, out var stamp));
        if (tag.Contains("50.")) Assert.Equal(2050, stamp.Year);
    }
    [Fact]
    public void SelectsNewestOfficialBuildRatherThanUploadOrderOrPreview() {
        var stable = Release("UTvTU-26.10.18-000000", id: 1);
        var old = Release("UTvTU-26.10.16-000000", id: 999);
        var preview = Release("UTvTU-26.10.19-000000", true);
        var draft = Release("UTvTU-26.10.20-000000"); draft.draft = true;
        var wrong = Release("UTvTU-26.10.21-000000"); wrong.html_url = "https://example.com/update";
        Assert.Same(stable, UpdaterViewModel.FindNewerRelease(new[] { old, preview, draft, wrong, stable }, "UTvTU-26.10.17-013015"));
        Assert.Null(UpdaterViewModel.FindNewerRelease(new[] { stable }, stable.tag_name));
        Assert.Null(UpdaterViewModel.FindNewerRelease(new[] { stable }, "developer build"));
        Assert.Null(UpdaterViewModel.FindNewerRelease(new[] { Release("0.1.568-plus.0.0.9") }, stable.tag_name));
    }
}
