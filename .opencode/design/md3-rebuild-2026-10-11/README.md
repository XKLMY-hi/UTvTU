# MD3 重构 · 欢迎场景与三种种子图库

`index.html` + 9张PNG是设计两件套，HTML的角色值、SVG路径和CSS是权威。PNG来自新HTML的隔离无头浏览器，不是UTvTU产品截图。欢迎场景已落地到 `WelcomeView.axaml` 与新 `MaterialShape.cs`；全局主题、控件状态、动态来源及其余界面已同步完成，见[完整实施记录](../../plans/md3-rebuild-2026-10-11.md)。

## 真实配色与语义

`palettes.json` 是root用现有Core DLL直接调用 `Md3SchemeColors.Create(seed, TonalSpot, dark, 0)` 导出的数据副本，源为 `.dsh/md3-rebuild-palettes.json`；三种种子 #6750A4 / #006C4C / #8F4E00，各有明暗49角色。图库显示14个相关角色的实际HEX，欢迎场景使用同一份数据；没有凭视觉仿色。种子色不等于primary角色值：例如#6750A4的浅色primary为#65558F。

| 用途 | 角色 / 尺寸 |
|---|---|
| 页面 / 左导航 | surface / surface-container-low；导航232，页内24 |
| 欢迎页头 | primary-container + on-primary-container；ExtraLarge28；内距24；标题22、说明13 |
| 主操作 | filled：primary + on-primary；高40、Full999 |
| 次操作 | tonal：secondary-container + on-secondary-container；高40、Full999 |
| 搜索与次级文字 | surface-container-high / on-surface-variant；搜索40，Small8 |
| 最近工程 | 行56，Medium12；文件图标容器32、图标20；正文13、路径12、格式11 |
| 模板 | 原240×64布局与真实TemplateFiles；Medium12 |
| 空状态 | Square72、图标26；secondary-container + on-secondary-container；标题16、说明13 |
| 恢复提示 | error-container + on-error-container，沿用真实HasRecovery |
| 页脚提示 | 原拖入入口提示32高；on-surface-variant |

采用Compact desktop尺度：页面动作40、编辑动作32/24、正文13、说明12、标签13/12/11。它是在Windows工作站密度下对MD3排版/尺寸角色的明确适配，不假称直接照搬移动端的全部默认字号或触控命中范围。产品全部通过md3.*动态角色，共享Md3Shape*的CornerRadius、Md3Type*的double、Md3Spacing*与Md3ControlHeight。CSS中的数值是设计表达，不作为固定产品主题。HTML默认 outlined 控件主色文字；悬停8%、按下10%、键盘焦点2px外环，filled/tonal状态层使用各自文字色。编辑区控件密度未改变。

## Google原生几何

选择Google官方35图形中的 **Arch + Square**。参数来源为上一轮保存的[MaterialShapes源码](../md3-style-audit-2026-10-11/assets/MaterialShapes.kt.txt)，官方[MaterialShapes API](https://developer.android.com/reference/kotlin/androidx/compose/material3/MaterialShapes)。本目录保存AndroidX [RoundedPolygon](https://raw.githubusercontent.com/androidx/androidx/androidx-main/graphics/graphics-shapes/src/commonMain/kotlin/androidx/graphics/shapes/RoundedPolygon.kt) 和 [Cubic](https://raw.githubusercontent.com/androidx/androidx/androidx-main/graphics/graphics-shapes/src/commonMain/kotlin/androidx/graphics/shapes/Cubic.kt) 原生源码快照，保留Apache2许可头；文件SHA-256和URL在 `geometry.json`。

- Square：`RoundedPolygon.rectangle(width=1,height=1,rounding=.3)`，零smoothing；归一化100×100后四角半径30。
- Arch：规则4顶点、外接半径1、每顶点rounding `[1,1,.2,.2]`、旋转-135°，零smoothing；边长sqrt(2)，顶部两个切角的总需求2超过边长，按原算法缩至sqrt(2)/2。每个角取两边allowed-cut的最小值，归一化后顶部圆角50，底部14.14213562373095。
- 两者均采用原 `Cubic.circularArc` 的90°系数 `4/3 * (sqrt(2)-1)`，生成三次贝塞尔；不使用SVG arc替换、不做像素描摹。`geometry.cjs` 是零smoothing情形的解析化计算，不声称移植整个RoundedPolygon/Morph。几何输出保留9位小数；与AndroidX Float32中间舍入可能有末位差异，并非二进制同构。
- `geometry.json`、`assets/selected-shapes.svg`、HTML和 `MaterialShape.cs` 的路径一致。MaterialShape按较短边等比缩放居中，缓存每个实例的尺寸变换，不订阅主题/文档、不拦截输入。

Arch64 + mic24用于页头视觉识别；Square72承托无工程、无搜索匹配和无模板空状态。形状不用于音符、波形采样、推子或品牌。品牌Canvas及其原生描边/填充路径在生产完全保留；设计HTML仅引用已有 `Assets/brand/utvtu-icon-32.png`。普通14图标为Assets/Icons.axaml原生256路径，没有描边转填充。

## 保留功能与响应

最近、新建、打开、模板四页保留原名称、Click事件和业务绑定，搜索、恢复、最近项目、音频导入、模板与真实已安装音源仍走原命令。静态对照保留17个x:Name、19个Click、18个业务绑定和整个品牌Canvas。HTML中的文件、版本、波形为明确示例；按钮只演示入口，不创建工程/安装包/合成音频。无工程与无匹配分开显示。

产品保持原232左导航和弹性内容；新增页头为3:2两列、描述可换行、装饰不拦输入。HTML在800×650及640×650均无横向溢出；700以下设计导航收180，装饰波形隐藏、徽标/日期收敛，左导航与正文可分别滚动。这些窄窗CSS是实现建议，不能把浏览器检查当作Avalonia实际布局验收。

## 验证与交付

运行 `node geometry.cjs`、`node design.cjs`、`node render.cjs`，只产生本目录设计文件；不会修改产品。render使用bundled Playwright隔离启动Edge，不连接现有浏览器/应用。`validation.json` 记录三种种子明暗角色核对、9图、空状态/四页切换、800/640宽边界、14原生图标、HTML/产品两条shape路径一致、品牌与命令绑定保留、pageErrors=0。图像已逐张查看并修复品牌资源路径与图库末行裁切。

产品SDK9单节点构建通过；214项互不重复轻量回归通过（10组串行、≤30秒/384MB），包含欢迎/偏好布局、真正的选中色配对、焦点优先级、减少动效、窄轨道密度、资源操作、效果器事务和离线导出。`product-validation.json` 保存分组、TRX路径、用例名和产品/测试DLL哈希。隐藏启动检查加载两轨工程、正常响应与退出，峰值256MB、ERR/FTL=0。未抓真实窗口截图；浏览器图不是Avalonia字体、产品像素或原生设备的验收证据。
共同状态层采用AndroidX `StateTokens.kt` v0_210：hover .08、focus .10、pressed .10、dragged .16；原生源码快照与许可保存在 `assets/StateTokens.kt.txt`，出处/日期/SHA-256见 `state-provenance.json`。`interface-coverage.json` 逐文件列出71份XAML的共同令牌/角色与主题挂载，44份直接消费共同令牌，其余继承应用主题；这是源码覆盖，不是71页的逐像素验收。
