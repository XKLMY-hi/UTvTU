using System;
using OpenUtau.Api;
using OpenUtau.Core.Format;
using OpenUtau.Core.Ustx;
using OpenUtau.Plugin.Builtin;
using Xunit;

namespace OpenUtau.Test.Core.Api {
    /// <summary>
    /// W14/P2-2：`Phonemizer` 的三个**父级表达式取值**（SHFT/ALT/CLR）的行为钉桩
    /// （上游 2c283d2b 的 Core/Api 部分）。
    ///
    /// 关键语义：CLR 必须走 <c>track.VoiceColorExp.options</c> —— 那是 `UTrack.Validate`
    /// 按声库 subbank 颜色列表建的运行期描述符，`UNote`/`UPhoneme` 也都用它；
    /// `VoiceColorExp` 为空或下标越界时返回空串（与 UPhoneme 一致），不再回落到工程里
    /// 那个 options 可能为空的 CLR 描述符（否则同一个轨道上，"父级颜色"与"音符颜色"
    /// 会取到不同的东西）。
    /// </summary>
    public class PhonemizerParentExpressionTest {
        static UExpressionDescriptor Descriptor(string abbr, double defaultValue, params string[] options) {
            var d = new UExpressionDescriptor($"{abbr} test", abbr, -100, 100, 0) {
                type = UExpressionType.Curve,
                CustomDefaultValue = (float)defaultValue,
            };
            d.options = options;
            return d;
        }

        static (EnglishVCCVPhonemizer phonemizer, UProject project, UTrack track) Make() {
            var project = new UProject();
            var track = new UTrack();
            var phonemizer = new EnglishVCCVPhonemizer {
                project = project,
                track = track,
            };
            return (phonemizer, project, track);
        }

        [Fact]
        public void VoiceColor_UsesVoiceColorExpOptions_NotProjectDescriptor() {
            var (phonemizer, project, track) = Make();
            // 工程里的 CLR 描述符有值，但轨道尚未 Validate（VoiceColorExp == null）
            project.expressions[Ustx.CLR] = Descriptor(Ustx.CLR, 1, "proj-a", "proj-b");
            Assert.Equal(string.Empty, phonemizer.GetParentVoiceColor());

            // Validate 之后（真实流程）：VoiceColorExp 按声库颜色建好，取到的是它
            track.VoiceColorExp = Descriptor(Ustx.CLR, 1, "singer-x", "singer-y");
            Assert.Equal("singer-y", phonemizer.GetParentVoiceColor());
        }

        [Fact]
        public void VoiceColor_OutOfRangeOrNullOptions_ReturnsEmpty_WithoutThrowing() {
            var (phonemizer, project, track) = Make();
            project.expressions[Ustx.CLR] = Descriptor(Ustx.CLR, 5, "only-one");
            track.VoiceColorExp = Descriptor(Ustx.CLR, 5, "only-one");
            Assert.Equal(string.Empty, phonemizer.GetParentVoiceColor());   // index 5 越界

            track.VoiceColorExp.options = null!;
            Assert.Equal(string.Empty, phonemizer.GetParentVoiceColor());   // options 为 null

            phonemizer.project = null;
            Assert.Equal(string.Empty, phonemizer.GetParentVoiceColor());   // project 为空
        }

        [Fact]
        public void Alternate_ZeroMeansUnset_OtherwiseValue() {
            var (phonemizer, project, _) = Make();
            project.expressions[Ustx.ALT] = Descriptor(Ustx.ALT, 0);
            Assert.Null(phonemizer.GetParentAlternate());

            project.expressions[Ustx.ALT] = Descriptor(Ustx.ALT, 3);
            Assert.Equal(3, phonemizer.GetParentAlternate());

            phonemizer.track = null;
            Assert.Null(phonemizer.GetParentAlternate());
        }

        [Fact]
        public void ToneShift_ReturnsCustomDefaultValue_AndZeroWhenUnset() {
            var (phonemizer, project, _) = Make();
            project.expressions[Ustx.SHFT] = Descriptor(Ustx.SHFT, -2);
            Assert.Equal(-2, phonemizer.GetParentToneShift());

            phonemizer.project = null;
            Assert.Equal(0, phonemizer.GetParentToneShift());
        }
    }
}
