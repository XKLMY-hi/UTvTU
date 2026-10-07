## UTvTU 0.1.568-plus.0.0.2-beta

### 新功能 🚀
- **渲染窗口**（Ctrl+Shift+R）：实时录制导出含完整 VST 效果
- **RecordingAdapter**：静音播放录制模式，绝对精确还原全信号链
- **混音台双向同步**：主界面与混音台音量/声像实时互通
- **VST 自动激活**：打开 .ustxp 项目即自动加载恢复插件状态
- **.ustxp 最近文件**：打开/保存 .ustxp 自动加入最近列表

### 改进 💅
- 声像范围统一 -100~100（混音台 + 轨道头一致）
- 混音台轨道条 UI 升级（圆角卡片 + 红色强调推子）
- 渲染窗口精简两栏布局（范围 + 格式）

### 修复 🐛
- 修复导出时 VST 效果丢失（现用实时录制方案）
- 修复 TrackColorDialog 残留的 BouncyCastle using
- MasterAdapter/RenderEngine 可访问性修正

### 技术
- 新增 `OpenUtau.Core/SignalChain/RecordingAdapter`
- 版本号 `0.1.568.2`
- 基于 OpenUTAU 0.1.568 (上游 tag)
