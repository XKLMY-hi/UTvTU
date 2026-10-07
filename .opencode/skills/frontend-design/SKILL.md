---
name: frontend-design
description: UTvTU 的 Avalonia 前端设计指南。编写 XAML/控件/样式/窗口/主题相关代码时参考：窗口架构（WindowEx 自绘/对话框原生）、样式体系（Colors/Styles/资源 Key）、数据绑定（ViewModelBase + [Reactive]）、典型控件模式与参考文件。注意部分信息可能滞后，与 AGENTS.md 冲突时以 AGENTS.md 为准。
---

# frontend-design — UTvTU

Avalonia 前端设计指南。编写 XAML / 控件 / 样式 / 窗口 / 主题相关代码时参考。

> 注意：本文件部分信息可能滞后于最新架构（如 Avalonia 版本、SukiUI 接管进度），与 AGENTS.md 冲突时以 AGENTS.md 为准。

## 技术栈

| 层 | 技术 |
|----|------|
| UI 框架 | Avalonia 12.1.0（曾为 11.2.4） |
| MVVM | ReactiveUI + Fody `[Reactive]` 属性编织 |
| 主题 | SukiUI 渐进接管中，见 AGENTS.md「SukiUI 现状与约定」 |

## 项目 XAML 约定

### 窗口架构
- **自绘窗口** → 继承 `WindowEx` (`OpenUtau.App.Controls.WindowEx`)
  - 自带 `WindowTitleBar` 标题栏 (min/max/close)
  - 自绘边框 + 亚克力/Mica 模糊
  - `ExtendClientAreaToDecorationsHint=True` 由 WindowEx 自行设置
- **普通对话框** → 继承 `Window`（原生标题栏）
  - 全局 Window 样式仅设 FontFamily / Icon / Background
  - 不再强制扩展客户区

### 样式体系
- 全局样式：`OpenUtau/Styles/Styles.axaml`
- 颜色定义：`OpenUtau/Colors/LightTheme.axaml` / `DarkTheme.axaml`
- 禁止硬编码色值（用 `{DynamicResource Key}`）
- 画笔资源：`Brushes.axaml`
- 控件级样式优先用 `Classes` 而非内联 Style

### 常用资源 Key
- `SystemControlBackgroundAltHighBrush` — 窗口背景
- `AcrylicTintBrush` — 亚克力底色
- `ui.fontfamily` — 全局字体

### 数据绑定
- ViewModel 继承 `ViewModelBase` (ReactiveObject)
- 可变属性用 `[Reactive]` 特性（Fody 自动生成 INotifyPropertyChanged）
- 命令用 `ReactiveCommand.Create(...)`
- View 中 `DataContext = vm`，控件绑定 `{Binding PropName}`

### 文件组织
- Views/ — 窗口 (WindowEx) 和对话框
- ViewModels/ — 每个 View 对应一个 ViewModel
- Controls/ — 可复用控件（UserControl）
- Colors/ — 主题色
- Styles/ — 全局样式
- Strings/ — 多语言 axaml 资源

## 典型控件模式

### 窗口 (WindowEx)
```xml
<controls:WindowEx xmlns:controls="clr-namespace:OpenUtau.App.Controls"
                   x:Class="OpenUtau.App.Views.MyWindow">
  <controls:WindowTitleBar Title="My Window" />
  <Grid>...</Grid>
</controls:WindowEx>
```
代码后置中设置 `DataContext = new MyViewModel()`。

### 带 VM 的控件
```csharp
// ViewModel 用 [Reactive] 声明属性
public class MyViewModel : ViewModelBase {
    [Reactive] public string Label { get; set; }
    public ICommand DoIt { get; }
    public MyViewModel() {
        DoIt = ReactiveCommand.Create(() => { ... });
    }
}
```

### 主题色获取
```csharp
ThemeManager.GetTrackColor(trackColor).AccentColor  // → IBrush
ThemeManager.GetString("key")                        // → 多语言字符串
```

### 自绘按钮三态
参考 `WindowTitleBar.axaml` 中 min/max/close 按钮：
- `Classes="titleBarBtn"` 基础
- `Classes="closeBtn"` 关闭按钮（hover 变红）
- `PointerOver` / `Pressed` 伪类

## UI 模式参考

| 模式 | 参考文件 |
|------|---------|
| 自绘窗口 | `Controls/WindowEx.cs` + `WindowTitleBar.axaml` |
| 混音台控件 | `Controls/MixerTrackStrip.axaml(.cs)` |
| 动态列表窗口 | `Views/VstRackWindow.axaml.cs` |
| 弹出选择器 | `Views/VstRackWindow.axaml.cs` `AddPluginToSlot()` |
| 欢迎页导航 | `Views/WelcomeScreen.axaml` |
| 设置对话框 | `Views/PreferencesDialog.axaml` |
| 渲染进度 | `Views/RenderWindow.axaml.cs` |
| 可拖拽分割条 | `Controls/ResizerBar.axaml.cs` |
| 自定义 MessageBox | `Views/MessageBox.axaml` |

## 注意事项
- 不要在 View 中直接操作 Core 模型 — 走 ViewModel 或 DocManager 命令
- 禁用硬编码颜色，全部走 `{DynamicResource}`
- WindowEx 窗口不要自己绑定 `WindowDecorationMargin`（基类已处理）
- CRLF 换行符警告可忽略
