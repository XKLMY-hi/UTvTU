# MD3 Expressive 局部样板

用户授权先试欢迎页、加载状态和播放控件。本轮只涉及这三个入口与共用几何适配器，未重构编辑器或音频逻辑。

- 欢迎页：36 DIP DisplaySmall 标题、144 DIP 几何单元、真实波形原路径作为低强调背景。最近/新建/打开/模板对应 Cookie7/Arch/Sunny/Square，切换触发有限变形，空闲不运行时钟。保留 232 DIP 导航与所有工程入口。
- 加载状态：3.2 秒几何循环，原品牌描边固定；实际等待任务与延迟弹出逻辑不变。减少动画保留静态七瓣形和等待文案。
- 播放控件：绑定现有 PlaybackViewModel.IsPlaying，播放三角与暂停双条连续变形。播放时采用 tertiary-container/on-tertiary-container，36 DIP 命中盒固定，轮廓在 18/12/10/8 DIP 之间随状态改变。继续使用原 OnPlayOrPause 处理器。
- 生命周期：有限变形使用 Avalonia RunAsync + CancellationToken；无限动画由样式 selector 驱动，隐藏时暂停，卸载/减少动画时撤掉 selector。没有新轮询定时器、音频订阅或文档修改。

## 几何与动效来源

运行 `python scripts/generate-expressive-shapes.py` 可重建 `geometry.json`、`shapes.svg` 和 `MaterialShapeProfiles.g.cs`。几何源文件、URL、SHA-256 记录于 geometry.json；原有 Arch/Square 路径保持原值。

Sunny 和 Cookie7 是 AndroidX MaterialShapes 的零 smoothing 特例，按 RoundedPolygon 的共享边裁切和 Cubic.circularArc 解析生成，不读取 PNG。动画用 192 条相同角度的正半径匹配，不会交叉；稳定端点绘制原生 cubic path。中间轮廓是高密度折线近似，未声称完整移植 AndroidX Morph 的 feature matcher。该适配器只面向本次中心可见的五种形状，不支持任意 SVG。

ExpressiveMotionTokens.kt.txt 是 Google 原始源码，Apache-2.0 许可保留；motion-source.json 记录 URL/哈希。欢迎页使用 default spatial 380/.8，播放图标使用 fast spatial 800/.6，通过现有有限 spring easing 适配到 Avalonia。其他控件仍采用项目原 Standard 方案。

## 验证范围

使用 SDK 9 对实际 Linux Debug 产品编译，并用 Avalonia.Headless 12.1.0 + Skia/HarfBuzz 渲染真实视图。检查欢迎页四色分页、浅深主题、800 DIP 窄窗口、快速切换、动画收束、加载循环与隐藏/卸载、实时减少动画、播放 VM 绑定、固定命中盒和语义色配对。播放状态为隔离测试 fixture，不输出音频。

现有 WelcomeViewTests 的几何/波形契约随样板调整；本轮不冒称运行全套 xUnit。实际运行检查及帧序列见交付预览的恢复包。Windows 原生窗口、真实声卡与音频播放未实测。

这是独立 `codex/md3-expressive-sample` 分支上的局部实验，没有推送主仓库。交付补丁只包含本次 UI 变更，不包含之前的 Linux 兼容改动。
