using System;
using System.IO;
using Avalonia;
using OpenUtau.App.Controls;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W38 契约：**卷帘与欢迎页"两处同源"**必须可验证，而不是靠自觉。
    ///
    /// 分两层钉：
    /// 1. **数值层** —— `NotesGeometry` 的内缩/圆角/波形纵坐标口径与抽取前逐字一致（纯函数断言）；
    /// 2. **源码层** —— `NotesCanvas.cs` 真的**调用**了共享几何（把"挪了公式但没接线"这种半成品挡住）。
    ///
    /// 画笔（`ThemeManager.AccentBrush1/AccentPen3/FinalPitchPen`）与波形归一化
    /// （`WaveformImage.NormalizePeak`）不在本文件断言对象内，由 `NotesGeometry`
    /// 直接引用同一对象/同一静态实现 —— 见其类文档。
    /// </summary>
    public class NotesGeometryContractTests {
        static string NotesCanvasSource() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Controls", "NotesCanvas.cs"));

        /// <summary>内缩口径 = 抽取前逐字一致：X+1、Y 取整(+1)、宽−1、高 floor(−2)。</summary>
        [Fact]
        public void NoteBodyRect_InsetsMatchTheLegacyValues() {
            var rect = NotesGeometry.NoteBodyRect(new Point(10, 20.4), new Size(100, 30));
            Assert.Equal(11, rect.X, 6);      // 10 + 1
            Assert.Equal(21, rect.Y, 6);      // round(20.4 + 1) = 21
            Assert.Equal(99, rect.Width, 6);  // 100 − 1
            Assert.Equal(28, rect.Height, 6); // floor(30 − 2) = 28
        }

        [Fact]
        public void NoteBodyRect_KeepsRightBottomConsistentWithTopLeftPlusSize() {
            var rect = NotesGeometry.NoteBodyRect(new Point(3.5, 7.5), new Size(20, 11));
            Assert.Equal(rect.X + rect.Width, rect.Right, 6);
            Assert.Equal(rect.Y + rect.Height, rect.Bottom, 6);
        }

        /// <summary>圆角是卷帘既有值 2；改它等于改卷帘观感。</summary>
        [Fact]
        public void NoteBodyCornerRadius_IsThePianoRollValue() {
            Assert.Equal(2.0, NotesGeometry.NoteBodyCornerRadius);
        }

        /// <summary>波形纵坐标口径：0.5 为中线、±0.5 满幅（与 WaveformImage 同一把尺）。</summary>
        [Fact]
        public void WaveformY_UsesTheSharedNormalization() {
            Assert.Equal(0.5, NotesGeometry.WaveformY(0f), 6);
            Assert.Equal(0.35, NotesGeometry.WaveformY(0.3f), 6);   // 0.5 − 0.3×0.5
            Assert.Equal(0.0, NotesGeometry.WaveformY(1f), 6);
        }

        /// <summary>
        /// **源码层**：`NotesCanvas` 必须真的调用共享几何与共享圆角常量。
        /// 否则"抽出来但没接线"会让两处继续各画一套 —— 这正是本卡要消灭的漂移。
        /// </summary>
        [Fact]
        public void NotesCanvas_ActuallyCallsTheSharedGeometry() {
            string src = NotesCanvasSource();
            Assert.Contains("NotesGeometry.NoteBodyRect(", src);
            Assert.Contains("NotesGeometry.NoteBodyCornerRadius", src);
            // 抽取前的硬编码内缩不应再出现（否则说明还有第二份口径）
            Assert.DoesNotContain("WithWidth(size.Width - 1).WithHeight(Math.Floor(size.Height - 2))", src);
        }
    }
}
