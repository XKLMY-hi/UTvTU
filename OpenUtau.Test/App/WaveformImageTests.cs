using OpenUtau.App.Controls;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 波形渲染的**方向契约**（与上游同源的缺陷回归）。
    ///
    /// 背景：`WaveformImage` 里把音频采样值映射成纵向位置时，我们与上游 merge-base
    /// 同源都写成了 <c>0.5f + s * 0.5f</c>。屏幕 y 轴向下、音频 + 向上 ⇒ 那样会把
    /// 波形**上下镜像**（正半周画到下半部）。上游 `1437d5e2` 修了这个符号，本树同修。
    ///
    /// 这里只测纯函数（归一化映射），不测像素：本仓 headless 走 UseHeadlessDrawing
    /// 桩绘制，`RenderTargetBitmap` 是空白位面。
    /// </summary>
    public class WaveformImageTests {
        [Fact]
        public void PeakNormalization_PutsPositiveSamplesOnTop() {
            // 端到端：+1（最大正峰）→ 图顶；-1（最大负峰）→ 图底；0 → 中线
            Assert.Equal(0f, WaveformImage.NormalizePeak(1f));
            Assert.Equal(1f, WaveformImage.NormalizePeak(-1f));
            Assert.Equal(0.5f, WaveformImage.NormalizePeak(0f));
            // 单调方向：采样值越大，纵向位置越靠上（数值越小）
            Assert.True(WaveformImage.NormalizePeak(1f) < WaveformImage.NormalizePeak(0.5f));
            Assert.True(WaveformImage.NormalizePeak(0.5f) < WaveformImage.NormalizePeak(0f));
            Assert.True(WaveformImage.NormalizePeak(0f) < WaveformImage.NormalizePeak(-0.5f));
            Assert.True(WaveformImage.NormalizePeak(-0.5f) < WaveformImage.NormalizePeak(-1f));
        }

        [Fact]
        public void PeakNormalization_IsNotTheInvertedFormula() {
            // 反向回归：旧实现 `0.5f + s * 0.5f` 会让 +1 落到图底（1.0）——这条会抓住它
            float inverted = 0.5f + 1f * 0.5f;
            Assert.Equal(1f, inverted);                                   // 旧公式的输出
            Assert.NotEqual(inverted, WaveformImage.NormalizePeak(1f));    // 新公式必须不同
            Assert.True(WaveformImage.NormalizePeak(1f) < 0.5f,
                "+1 的采样值必须画在中线以上（y 越小越靠上）");
            Assert.True(WaveformImage.NormalizePeak(-1f) > 0.5f,
                "-1 的采样值必须画在中线以下");
        }
    }
}
