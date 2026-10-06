using Avalonia;
using OpenUtau.App.Controls;

namespace OpenUtau.App.Controls {
    /// <summary>
    /// **卷帘几何的单一来源**（W38 抽取第一步）。
    ///
    /// 背景：欢迎页要放"一小节真实演唱 + 音高线 + 音符块"的缩略，且要求**与卷帘同源**
    /// （同一套几何 / 同一批画笔），否则两处会各画一套、慢慢漂移。
    ///
    /// 抽取原则（**只挪公式 + 调用，不碰交互状态**）：`NotesCanvas` 是 834 行的交互控件
    /// （命中测试、hover 计时器、播放高亮/回弹、订阅），整块抽取风险与收益不匹配；
    /// 因此这里只搬**纯几何常量与纯函数**——它们没有状态、可单独断言。
    ///
    /// 画笔不在本类：直接复用 <c>ThemeManager</c> 的同一批 public static 字段
    /// （`AccentBrush1`/`AccentBrush2`/`AccentPen3`/`FinalPitchPen`/`BarNumberPen`），
    /// 由契约用例断言"两处引用的是同一对象"。
    /// </summary>
    public static class NotesGeometry {
        /// <summary>音符体的圆角（卷帘绘制音符时用的值；欢迎页缩略必须同值）。</summary>
        public const double NoteBodyCornerRadius = 2;

        /// <summary>
        /// 音符体矩形：由 <c>NotesViewModel.TickToneToPoint</c> / <c>TickToneToSize</c> 得到
        /// 左上角与尺寸后，按卷帘既有口径**内缩**（右 1 / 下 2、左上各 +1、Y 取整）。
        ///
        /// ⚠ 数值必须与抽取前逐字一致（历史行为），改动等于改卷帘观感。
        /// </summary>
        public static Rect NoteBodyRect(Point leftTop, Size size, double yOffset = 0) {
            Point lt = leftTop.WithX(leftTop.X + 1).WithY(System.Math.Round(leftTop.Y + 1));
            Size sz = size.WithWidth(size.Width - 1).WithHeight(System.Math.Floor(size.Height - 2));
            return new Rect(lt, new Point(lt.X + sz.Width, lt.Y + sz.Height));
        }

        /// <summary>
        /// 波形取样的纵坐标口径 —— 复用 <see cref="WaveformImage"/> 的同一个静态函数
        /// （0.5 为中线、峰值 ±0.5 满幅），保证欢迎页与卷帘的波形"同一把尺"。
        /// </summary>
        public static double WaveformY(float sample) => WaveformImage.NormalizePeak(sample);
    }
}
