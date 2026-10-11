using OpenUtau.App.Views;
using OpenUtau.Core.Ustx;
using Xunit;

namespace OpenUtau.Test.App {
    public sealed class RenderInputValidationTests {
        [Theory]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("-1")]
        [InlineData("")]
        [InlineData("hello")]
        public void InvalidSecondsCannotReachTheRenderPlan(string text) => Assert.False(RenderInputParser.TrySeconds(text, out _));
        [Theory]
        [InlineData("0", "1", true)]
        [InlineData("1", "1", false)]
        [InlineData("2", "1", false)]
        [InlineData("0", "NaN", false)]
        [InlineData("-1", "2", false)]
        public void CustomRangeMustBeFiniteNonnegativeAndNonempty(string start, string end, bool valid) =>
            Assert.Equal(valid, RenderInputParser.TryRange(new UProject(), 2, start, end, 0, 0, out _, out _));
        [Fact]
        public void SelectionIsValidatedInsteadOfFallingBackToFullSong() {
            var project = new UProject();
            project.parts.Add(new UVoicePart { Duration = 960 });
            Assert.False(RenderInputParser.TryRange(project, 1, null, null, 0, 0, out _, out _));
            Assert.True(RenderInputParser.TryRange(project, 1, null, null, 100, 200, out int start, out int end));
            Assert.Equal(100, start);
            Assert.Equal(200, end);
        }
    }
}
