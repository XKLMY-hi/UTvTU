# 资源中心与工作区细化

`index.html` 是本轮布局、CSS 令牌和原生 SVG 几何的权威。9 张 PNG 由这个 HTML 生成并逐张查看；不是产品截图。样例音源、包、OTO 数值和波形明确为设计示例，无安装、插件加载、音频播放或工程写入。渲染页保留上一轮布局供导航参考，本轮仅细化资源、底部面板和短浮层。

## 优先落地布局

音源管理采用可搜索对象列表与当前对象详情。对象身份在详情顶部固定，OTO、子音源和说明是同一音源的兄弟页签。目录、试听是主操作；原 SingerMenu 作为设置溢出入口，不删网站、头像/肖像、编码、类型、默认音素化器、文件名别名、发布、合并、错误报告、刷新等真实功能。搜索只筛列表，不隐式改选当前对象。

| 部位 | HTML 几何 | 生产对接 |
|---|---|---|
| 工作区宿主 | 左导航232 / 顶部56；内容内距20,24,16；标题22 | ToolWorkspaceView 的 Render/Singers/Packages 兄弟导航 |
| 音源列表 | 宽232；搜索36；音源行56，头像36；列表内距8，底部安装36 | Singers / Singer；本地名称和类型搜索 |
| 对象身份 | 头像64 / 圆角12；标题16，类型12、路径11；动作32 | Singer.Avatar / Name / Info；OpenLocation、角色试听、SingerMenu |
| 详情导航 | 高40；主色3高下划线；页签间隔2 | OTO、Subbanks、Info，Classic 能力决定可用状态 |
| OTO局部工具 | 字段36；动作32；别名搜索最大260宽 | SearchAlias / RefreshSinger / SaveOtos / vLabeler；OtoDirty 控制保存状态 |
| OTO表 | 行32，数值输入28；横向最低568；表体独立滚动 | displayedOtos / SelectedOto，保留原编辑与右键菜单 |
| 波形 | 可调分隔条6；身份栏28；波形156；动作栏40 | OtoPlot；选中 Alias + DisplayFile + 当前 Singer，播放所选文件、ZoomInMel |
| 子音源 | 只读四列，行36；页内编辑入口32 | Prefix / Suffix / ToneRangesString / Color；沿用现有子音源编辑器 |
| 包管理 | 搜索36；操作36；行56；表格最低650宽；页内状态区 | Available 的真实 Name/Developer/Version/InstalledDisplay/PrimaryActionLabel |
| 底部面板 | 默认43vh；标题40；字段36/动作32；保留至少170高编辑区 | 收起保留工具/高度；关闭结束工具；独立恢复入口 |
| 短浮层 | 宽360、内距16、圆角12；背景blur3 + 黑色28%遮罩 | 单一短输入/确认；浮层正文保持清晰，输入聚焦、Tab循环、Esc取消/关闭恢复焦点 |

**OTO完整列必须保留**：Alias、Set、DisplayFile、Phonetic、Prefix、Suffix、Color、Offset、Consonant、Cutoff、Preutter、Overlap。HTML 主图为可读性展示7个常用列，生产不能据此删除其余列。数值调整沿用真实控件校验/保存语义；表右键保持定位源文件、vLabeler、setParam和原FRQ工具。

包搜索仅使用当前实际已知元数据；本地筛选为全部、已安装、可安装或更新，依据现有 InstalledDisplay / CanInstallOrUpdate / CanUninstall。每行主操作用 PrimaryActionLabel，卸载仅在允许时显示。无匹配和无音源为空状态，安装/目录入口保持可达。保留当前 Status，不添加在线商店、云同步或虚构联网状态。

## 窄窗与短高度

1180以下宿主导航200、音源列表208，内容左右20；1040以下导航180、音源列表收为详情上方音源选择入口，展开232宽抽屉，选择后收回。800以下导航152、内容左右16，头像48、波形140；齿轮溢出入口保留图标。详情正文可纵向滚动，表格保留列宽横向滚动，波形不压成数像素。身份、页签和状态仍属于当前音源，搜索无匹配不把详情改成另一音源。640宽验证表格横向滚动，800×700验证文档无横向溢出与波形底部操作可滚动到达。

底部收起只有40高标题，页签仍可恢复正文，并保留此前高度。关闭按钮独立于收起；关闭隐藏整个工具面板，编辑工具栏入口可再打开。此项取代上一轮README把关闭与收起归为同一行为的说明。浮层只模糊其背景工作视图，开/关180ms淡入淡出加6px位移；减弱动效时无过渡，关闭立即完成。迟到动画回调须按打开代号失效，不能把重新打开的浮层隐藏。

## 颜色、状态与 native paths

产品读取现有动态角色，HTML深浅色仅演示。禁止用示例固定色覆盖主题。

| CSS角色 | Avalonia角色资源 | 用途 |
|---|---|---|
| surface / surface-lowest | md3.surface / md3.surface-container-lowest | 内容 / 波形画布 |
| surface-low / surface-container | md3.surface-container-low / md3.surface-container | 壳与列表 / 表头 |
| surface-high / surface-highest | md3.surface-container-high / md3.surface-container-highest | 字段与浮层 / 状态背景 |
| on-surface / on-surface-variant | md3.on-surface / md3.on-surface-variant | 正文 / 次级说明 |
| primary / on-primary | md3.primary / md3.on-primary | 主动作、焦点、OTO时间标记 |
| secondary-container / on-secondary-container | md3.secondary-container / md3.on-secondary-container | 选中列表行、胶囊 |
| outline / outline-variant | md3.outline / md3.outline-variant | 边框 / 分隔 |
| error / tertiary | md3.error / md3.tertiary | 截止 / 预发与重叠标记（示例） |

悬停8%、按下12%、禁用38%；焦点2px外环；常规字段圆角8、列表行8、头像12、按钮胶囊999。原生图标16px，保留填充渲染和 true bounds 居中。`#native-icons` 的16个 symbol 逐字引用 `OpenUtau/Assets/Icons.axaml`：package、search、folder-open、settings、info、pencil、mic、play、stop、skip-back、chevron-up、chevron-down、x、refresh-cw、save、headphones；viewBox 全部 `0 0 256 256`，验证每个 d 与源 StreamGeometry 完全一致。品牌仍直接引用现有 utvtu-icon-32.png，不描摹、不重画、不做描边转填充。

## 查看与验证

直接打开 `index.html`，默认音源OTO页；可用 `?view=resources&resource=packages`、`?view=resources&empty=1`、`?view=context`、`?theme=light`。原型包含本地搜索、选择、类型能力、脏状态、包筛选、抽屉、面板收起/关闭/恢复/调高、浮层焦点与动效，动作示意不产生文件变更。

`render.cjs` 仅启动隔离的无头Edge渲染新HTML，不连接任何产品或已有浏览器。运行 `node render.cjs`；依赖本机bundled Playwright。`validation.json` 记录9图、Edge版本、搜索保留目标、Classic限制、OTO脏状态、包状态筛选、800边界/640横向滚动、387→40→387面板恢复、blur3/180ms/焦点回归、减弱动效即时关闭、16个native paths及0页面错误。PNG均已实际查看，检查后的布局修正包含小高度波形最小尺寸和窄窗齿轮可见性。没有运行产品构建、测试、真实音源/VST或产品截图；生产迁移与验证由root负责。