# UTvTU.DawUI（内部控件库，第一批）

供 UTvTU 内部使用的 Avalonia 12.1 控件与 MD3 主题库，`IsPackable=false`。
不引用主程序、工程模型、音频引擎或 ViewModel。仅引用 Avalonia 和纯颜色引擎 `UTvTU.Md3`。

## 所有权

- `UTvTU.Md3`：原有 HCT/CAM16、TonalSpot 等方案、对比度曲线与 49 个角色；源码原样迁移。
- `UTvTU.DawUI`：同一个 `ColorPool`、资源发布、派生音乐数据配色缓存、动效、形状/排版/密度令牌、基础控件及模板。
- 主程序：Windows 强调色、用户偏好、语言资源、工程编辑与撤销、音频参数写入。
- `OpenUtau.Core.Theming`、`OpenUtau.Theming` 是保留的源代码兼容命名空间；它们的实际实现分别位于上述两个库中，没有第二份颜色池。

## 首批 API

| 控件 | 作用 |
|---|---|
| `DawButton` | Filled / Tonal / Outlined / Text 按钮；沿用原生 Command、Click、键盘与无障碍语义 |
| `DawIconButton` | 图标按钮，`IconData` 使用填充 Geometry；调用方提供 AutomationProperties.Name |
| `DawToggleButton` | 原生 IsChecked / Toggle 状态与公共状态层 |
| `DawSwitch` | DAW 紧凑开关，复用原效果器链紧凑开关模板 |
| `DawKnob` | 原有效果器旋钮输入与绘制，拖动 / 滚轮 / 键盘 / Shift 微调 / 双击复位 |
| `DawFader` | 原混音台推子的输入、布局与手势边界，允许宿主提供外观 |
| `DawVerticalFader` | 同一推子输入上的可直接使用的 MD3 外观 |
| `DawParameterField` | Value / Minimum / Maximum / Unit / Format，明确开始、提交、取消的输入边界 |

`DawStyle.Density` 可设在父面板并继承，也可由单个控件的 Density 覆盖：Standard 40、Compact 32、Dense 24 DIP。密度影响布局和输入空间，开关轨道仍保留紧凑几何。

```xml
<StackPanel xmlns="https://github.com/avaloniaui"
            xmlns:daw="using:UTvTU.DawUI.Controls"
            daw:DawStyle.Density="Compact" Spacing="8">
  <daw:DawButton Variant="Tonal" Content="预听" Command="{Binding PreviewCommand}"/>
  <daw:DawSwitch Content="启用" IsChecked="{Binding Enabled, Mode=TwoWay}"/>
  <daw:DawParameterField Value="{Binding Gain, Mode=TwoWay}"
                         Minimum="-24" Maximum="12" Unit="dB" Format="0.0"/>
</StackPanel>
```

## 参数交互

- 双击或 Enter / F2 开始输入；Enter 提交；Esc 取消。
- 失焦提交合法值，非法值取消；Enter 输入非法值时保留输入与错误状态。
- 拒绝 NaN/Infinity，提交时限制范围；不会向宿主回写非法值。
- 外部值更新、禁用和卸载会结束待定输入，后续失焦不会复活旧值或重复提交。
- `EditingStarted` / `ValueCommitted` 是宿主需要的编辑边界。普通设置可使用显式 TwoWay；有撤销栈的宿主应保留单向显示绑定，在提交回调中发出已有命令。控件不接触 DocManager。
- 旋钮和推子的原有输入实现已迁入库，旧类是兼容入口；连续拖动读数、电平表等尚保留原实现。

## 独立宿主

宿主挂载 Avalonia 基础主题（例如 Fluent，以提供 ScrollViewer 等通用部件），再挂载一次：

```xml
<StyleInclude Source="avares://UTvTU.DawUI/Themes/DawTheme.axaml"/>
```

随后在 UI 线程配置主题：

```csharp
ColorPool.Initialize(seed, Md3SchemeVariant.TonalSpot, isDark, contrastLevel);
Md3Motion.PreferenceSource = () => reducedMotion;
Md3Motion.ApplyPreference();
Md3Motion.Register(window);
```

`ColorPool.Derived(trackSeed)` 返回跟随同一深浅主题与对比度、保持轨道种子身份的缓存配色。
颜色使用 `md3.*` 画刷和 `md3.color.*` 颜色资源键。更换主题替换旧资源字典，不重复叠加；现有 DynamicResource 控件自动更新。
主程序保留旧 StyleInclude 路径作转接，按原挂载次序加载同一实现，不再额外挂整个 DawTheme，避免重复全局主题。

## 接入与验证

主程序“工具 → 控件陈列室”顶部展示第一批控件；演示值不写工程。
效果器的 `Knob`、混音台的 `DawFader` 已通过兼容子类接入库，原始输入算法与宿主事件保持不变。
混音台的 `DawValueField` 是继承 `DawParameterField` 的兼容适配器，保留旧 Readout/Editor 名称、字号与布局；原宿主的提交回调和撤销路径继续使用。
全局按钮/输入/选择模板已由库拥有，旧资源键继续解析到同一来源。已定稿的钢琴窗和页面布局不在本批次重排。

颜色算法对照沿用仓库的参考数据：132 × 49 个角色值及 675 个 HCT 向量；具体运行报告和完整截图随累计备份提供。
本次 Linux 编译与实际 Avalonia/Skia 验证不替代 Windows、真实音频或 VST 验证。
