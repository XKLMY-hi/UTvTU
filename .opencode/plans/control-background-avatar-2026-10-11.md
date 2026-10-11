# 控件背景与声库真实头像 · 2026-10-11

针对用户提供的 BPM 浮层、资源中心与渲染页三张截图，已完成产品提交 `a04807e9` / `1e6b6cc5`，plus-develop，未推送。

## 修复

- BPM 等嵌入浮层此前由 WorkspaceToolView 默认绘制 md3.surface，圆角宿主卡片内部因此出现整块方形页面底色。工具基类取消默认底色；嵌入工具使用宿主表面，渲染等完整页面仍可显式指定页面背景。实际挂载检查保证内容处于卡片内边距之内，按钮只由同尺寸/同圆角的根 Border 绘底，富内容 Presenter 不另刷背景。
- 声库侧栏由单一 surface-container-low、12 圆角、ClipToBounds 的表面包围，原 SingerBrowser 命名、筛选/选择行为和窄窗切换保留；去掉声库 ListBox 的重复边框。
- 列表每行使用 SingerAvatar：优先 AvatarData，无数据时使用声库 Avatar 文件路径；仅已实现的行解码72像素宽缩略图，36格 UniformToFill、8圆角裁切。不调用 EnsureLoaded、不启动合成引擎，不订阅全局文档或新计时器。缺失/读取失败使用默认麦克风；真实头像不叠默认图标。回收/卸载清除 Image.Source 并释放位图，重新挂载重新解码。
- SingersViewModel 的 AvatarRevision 在声库加载/刷新时通知列表；设置头像已有 ModifyConfig→RefreshSinger 流程同步生效。列表 ItemsSource 仍为 USinger，没有更改筛选后保持当前对象的语义。
- 资源页重新挂载检查发现 OtoPlot 的旧缺陷：卸载释放位图却未清除引用，后续 Render 访问已释放对象。现在清空波形/频谱位图及波形缓存，加载时按已有 WaveFile 重建必要频谱；没有改变采样/时序算法。
- 渲染和偏好分段选中项原 `icon-check` 包含外方框；新 `icon-checkmark` 逐字复用原生向量中的勾选子路径，仅去掉外框。没有从截图描摹、转换描边或改品牌。

## 轻量验证

为了保留用户当前主程序会话，构建输出到 `.dsh/controls-polish-build/`。SDK9.0.317、单节点完整项目引用 build4 通过，31.36秒、0错148警告；仅测试断言收尾的 build5 使用 BuildProjectReferences=false，9.86秒、0错145警告。

**54 项互不重复检查通过**，沿用 Test-Light 的30秒/384MB/串行保护；临时脚本仅改变 runner/OutDir，记录 `.dsh/controls-polish-validation.json`。

| 组 | 项数 | 秒 | 测试进程峰值 MB | TRX |
|---|---:|---:|---:|---|
| 资源、头像、浮层与工具宿主 | 23 | 16.2 | 225 | bd39e8e5c4484eeaaff5c7eaf105b320.trx |
| 设计系统、页面分段布局与渲染流程 | 31 | 15.9 | 192 | 3f74d2b4a2d54d5c8cdd6bbb56b65c38.trx |

头像测试使用有效PNG字节；Avalonia headless 的图像解码是桩，因此只验证绑定/来源/尺寸请求、未选声库无加载、刷新、读取失败回退、卸载释放和重新挂载，不能当成真实图像像素或损坏图片解码验收。未修改全局 headless 渲染器、未跑全套/真实推理/音频/频谱压力，未自行截图。UI lint ERROR0/WARN0、diff检查通过。

## 安装与当前会话

已将验证过的 OpenUtau.dll 用原子替换更新至原主程序目录，旧二进制备份位于 `.dsh/controls-polish-previous-binaries/`；保持原 exe、配置/声库位置和运行中的会话。调试信息是 embedded，没有外部 OpenUtau.pdb。标准测试目录的产品DLL和测试DLL也已更新；主程序/测试产品DLL SHA-256 相同：`E08F01A86E183DD846C6B2C7B344DB36F967C9D5801918F8E6A17B8B9043A2B8`。

当前主程序 PID6932、15:51:33+08 启动，仍为重启前的内存版本，响应正常；没有关闭窗口或注入预览工程/测试参数。`.dsh/workspace-preview.json` 标记 NeedsRestartForUpdate=true。下次启动应正常退出旧实例再直接运行原目录 OpenUtau.exe，Arguments=[]；若出现未保存提示应交由用户处理，不强制杀进程。新二进制本轮未重新开真机窗口，当前截图对应旧版本。

用户实机复看 BPM 等小浮层是否还有方形底色、声库列表的真实头像/更换头像/窄窗切换，以及渲染分段的选中标记。
