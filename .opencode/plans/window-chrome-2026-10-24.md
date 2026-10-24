# 主窗口一体式外壳（2026-10-24）

用户批准主窗口采用设计稿同款自绘边框，保留 Windows 窗口体验。产品提交 `1ce10d87`（外壳）、`101f1142`（对齐）、`972c86a6`（描边层），`plus-develop`、Windows，未推送。

## 实现

- `WindowEx.UseWorkspaceChrome` 默认关闭，仅 `MainWindow.axaml` 显式启用。独立编辑器、其他原生窗口及 VST GUI 未启用新外壳；普通 WindowEx 的原生装饰契约仍通过。
- 新 `WorkspaceWindowDecorationsTheme` 使用 Avalonia 12 的 `WindowDrawnDecorations` 和原生 ElementRole。Framework PART 挂接最小化、最大化/还原与关闭；关闭仍经过 Window.Close/既有 WindowClosing 保存确认，未添加强制退出或自行处理 Win32 消息的产品代码。
- 标题栏高度占位为 0：页面自身的顶栏即拖动区域，不再额外叠一行原生标题栏。1 DIP 细边框放在装饰 Overlay，位于不透明 Window 背景之上且不参与命中，最大化和全屏隐藏；颜色使用动态 outline-variant；背景与按钮使用 MD3 动态角色。继续使用实色、`WindowTransparencyLevel.None`，状态切换不重置透明合成。
- 右侧为 32×32、8 圆角的三个窗口按钮，合计预留 128 DIP；最大化后更换还原图标，关闭悬停使用 error/on-error。欢迎页的按钮中心线为 Y=44，其他页面为 Y=28（均不含窗边框），切到设置/制作页会即时切换高度；最小化、最大化与关闭 SVG 的路径及 viewBox 原样来自 `html-css/U4-welcome-preferences.html`；未从 PNG 描摹或转换描边。
- 工作区、欢迎页、设置和制作工作区的顶栏均避让按钮区域。空白区标记 TitleBar，搜索与操作区域标记 User。窗口边缘由框架管理；没有手写鼠标拖动坐标或同步消息钩子。
- `WorkspaceHeader` 保留原分组，加入占位与换行：宽窗单行，中等双行，极窄且长工程名时运输组再单独占一行，控件不重新挂载。欢迎搜索宽度绑定实际可用区域，避免占位后仍保留固定 400 导致越界。
- 工程标题显示文件名或导入工程名、未命名工程与未保存星号；原有加载、编辑、保存通知同时刷新 ProjectTitle。系统 Window.Title 保留原版本/路径格式，供任务栏等使用。全屏释放按钮占位，返回时恢复。

实现依据：[Avalonia 官方窗口装饰文档](https://docs.avaloniaui.net/controls/primitives/windowdrawndecorations/)、[12.1.0 Window 源码](https://github.com/AvaloniaUI/Avalonia/blob/12.1.0/src/Avalonia.Controls/Window.cs)、[12.1.0 WindowDrawnDecorations 源码](https://github.com/AvaloniaUI/Avalonia/blob/12.1.0/src/Avalonia.Controls/Chrome/WindowDrawnDecorations.cs)。以本机 12.1.0 包和真实运行结果为准。

## 验证

[完整记录](window-chrome-2026-10-24/validation.json)包含 62 个不同 headless 用例、TRX、程序集哈希及 Win32 结果。

| 组 | 通过 | 耗时 | 峰值 |
| --- | ---: | ---: | ---: |
| 新窗口布局/状态/标题 | 11 | 19.7 秒 | 183.0 MiB |
| 实际工作区控件 | 10 | 15.3 秒 | 178.0 MiB |
| 欢迎页 | 21 | 14.5 秒 | 141.0 MiB |
| 偏好设置 | 6 | 9.2 秒 | 156.0 MiB |
| 搜索/主题/重排 | 6 | 13.5 秒 | 154.0 MiB |
| 页面与键盘导航 | 6 | 15.7 秒 | 156.0 MiB |
| 原生默认/视图切换契约 | 2 | 8.8 秒 | 106.0 MiB |

所有组仍使用 30 秒/384 MiB、串行护栏。原工作区夹具补齐了仓库已有的 PlayPauseButton 类型解析；没有修改或禁用原布局断言。新例还把实际顶栏的工程名填到最大宽度，在 800/1440/1800 下检查分组与窗口按钮区域无重叠。

Headless 后端不请求原生窗口装饰，不能用它证明装饰按钮的真实行为。独立 Win32 探针采用实际编译的 App 资源和 WindowEx、软件渲染、私有数据目录、禁止激活/任务栏；没有 MainWindow、工程或音频。最终 4.61 秒、134.0 MiB、exit 0，30 秒/384 MiB 护栏不变：

- 最大化/最小化/关闭区域的 WM_NCHITTEST 返回 9/8/20；拖动空白区为 2，文本框为 1，左侧缩放边缘为 10。
- 系统菜单句柄存在；按钮尺寸 32 DIP；欢迎/设置顶栏对齐正确；可见描边确在客户区上层、不拦截输入，最大化隐藏；最大化/还原与图标切换正常。
- 连续 3 次最小化/恢复、全屏返回保留内容与文本；关闭可被 Closing 取消。
- 动态深浅色两次检查均匹配当前 on-surface-variant。

这是窗口行为和命中检查，未模拟真实鼠标拖动全程或打开系统菜单，未做播放/预渲染/GPU压力；当前 RenderScaling=1，未做多显示器 DPI 迁移。没有自行截图，观感按约定交用户实机查看。

SDK 9.0.319、2 处理器、单节点、768 MiB 托管堆，增量主程序及测试，复用现有 Core。最终主程序 0 错/3 个既有警告、38.47 秒；测试 0 错/152 个既有警告、19.84 秒。主/测试目录 Main、Core 哈希一致。标准 Debug `OpenUtau/bin/Debug/net8.0-windows/OpenUtau.exe` 已更新。

原生探针的启动依赖最初未配好（native DLL 路径与编译 XAML 加载方式），修正后最终通过；未以这些失败当作产品验证。日志和隔离探针源码位于忽略目录 `.dsh/window-chrome-20261024/`，结构化证据记录其源码哈希。

本轮没有启动工作区主程序、关闭用户其他 exe、打包、推送或发布。结束时发现用户另一路径的 OpenUtau.exe 正在运行，未操作；该实例不代表当前工作区的本轮构建。下次启动仍需重新核实盘符、exe 路径与进程身份。
