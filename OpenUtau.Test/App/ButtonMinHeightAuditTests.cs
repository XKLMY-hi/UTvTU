using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using OpenUtau.App.Controls;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W32（task-42）审计：声明高度 &lt;32 的 Button **类**控件，是否被
    /// 主题 <c>Md3ButtonTheme.MinHeight=32</c>（`Styles/Md3ControlThemes.axaml`）或应用级
    /// <c>Button { Margin: 0,4 }</c> 顶掉。
    ///
    /// 判定原则（本类就是判据本身）：
    /// · Avalonia 类型选择器**精确匹配** ⇒ `Button` 的隐式主题/样式**不作用于** `ToggleButton`
    ///   /`RadioButton` 等派生类；这类控件声明多少就是多少（下面两个探针把它钉住）。
    /// · 真正受影响的是 **Button 本尊**：布局取 `Max(MinHeight, Height)`，
    ///   所以必须补本地 `MinHeight`（= 声明高度）+ `Margin=0` 中和，并且**只认 Bounds**——
    ///   "属性绿、像素红"已经骗过我们两轮（W6/W10）。
    ///
    /// 断言全部在**布局层**（真宿主窗口 + `Settle()`）：headless 下绘制是桩，但布局是真跑的。
    /// </summary>
    [Collection("Theme")]
    public class ButtonMinHeightAuditTests {
        /// <summary>陷阱本体：裸 Button 声明 20 高，实际渲染 32（主题 MinHeight 顶掉）。</summary>
        [AvaloniaFact]
        public void TrapIsReal_PlainButton_Declared20_Renders32() {
            var button = new Button { Content = "x", Height = 20, Width = 40 };
            MixerGeometryTests.InWindow(button, 200, 200, () => {
                Assert.Equal(32, button.MinHeight);          // 主题值（诊断用，不是判据）
                Assert.Equal(32, button.Bounds.Height);      // ← 判据：像素
            });
        }

        /// <summary>ToggleButton 不受 Button 主题影响（精确类型匹配）—— 声明 20 就是 20。</summary>
        [AvaloniaFact]
        public void ToggleButton_Declared20_IsNotTouchedByButtonTypeSelector() {
            var toggle = new ToggleButton { Content = "x", Height = 20, Width = 40 };
            MixerGeometryTests.InWindow(toggle, 200, 200, () => {
                Assert.Equal(0, toggle.MinHeight);           // 没有 Button 主题的 32
                Assert.Equal(20, toggle.Bounds.Height);      // ← 判据：像素
            });
        }

        /// <summary>
        /// RadioButton 不吃 Button 的 32；但它有**自己的**主题地板
        /// `Md3RadioButtonTheme.MinHeight=28`（Md3SelectionThemes.axaml:149）——
        /// 同一类陷阱的 RadioButton 版本，登记在这里免得下次又当新发现。
        /// </summary>
        [AvaloniaFact]
        public void RadioButton_HasItsOwnThemeFloor28_NotTheButton32() {
            var radio = new RadioButton { Content = "x", Height = 20, Width = 40 };
            MixerGeometryTests.InWindow(radio, 200, 200, () => {
                Assert.NotEqual(32, radio.MinHeight);        // 不是 Button 的 32
                Assert.Equal(28, radio.MinHeight);           // 是选择类主题的 28
                Assert.Equal(28, radio.Bounds.Height);       // ← 判据：像素
            });
        }

        /// <summary>
        /// <see cref="ViewScaler"/> 里那颗按钮：设计 24×24（`d:DesignWidth=24`）。
        /// 它是**真 Button**，主题 MinHeight=32 会顶掉声明高度 ⇒ 已在 XAML 里补
        /// `MinHeight="24"`+`Margin="0"`；这里按像素验收。
        /// </summary>
        [AvaloniaFact]
        public void ViewScaler_Button_RendersAtDeclared24() {
            var scaler = new ViewScaler { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            MixerGeometryTests.InWindow(scaler, 200, 200, () => {
                var button = scaler.GetVisualDescendants().OfType<Button>().First();
                Assert.Equal(24, button.MinHeight);          // 本地压掉了主题的 32
                Assert.Equal(24, button.Bounds.Width);       // ← 判据：像素
                Assert.Equal(24, button.Bounds.Height);      // ← 判据：像素
                Assert.Equal(0, button.Margin.Top);          // 不吃应用级 Button{Margin:0,4}
            });
        }
    }
}
