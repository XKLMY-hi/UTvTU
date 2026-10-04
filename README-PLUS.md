# OpenUTAU Plus

**OpenUTAU Plus** 是基于 [OpenUTAU](https://github.com/openutau/OpenUtau) 的增强分支版本。

> 完整功能说明、界面截图与开发文档见 **[README.md](./README.md)**；
> 设计决策与踩坑记录见 [`.opencode/plans/ui-rework-decisions.md`](.opencode/plans/ui-rework-decisions.md)。

## 与原版的关系

OpenUTAU Plus 在 MIT 许可证下从原版 OpenUTAU 分支而来，保持与上游同步的同时，添加以下改进：

- 🎨 **Material Design 3 界面体系**（2026-09 重铸）：设计稿驱动的颜色池、容器梯度、
  自有控件主题、单窗口三视图、全屏偏好设置；**第三方控件库已完全移除**
- 🎚️ **DAW 风格混音台** — 垂直推子、实时电平表、内嵌/独立双模式
- 🔌 **VST3 效果器插件** — 原生 GUI 弹窗、实时参数同步、按轨道串行处理
- 📦 **`.ustxp` 项目格式** — 插件参数持久化，兼容 `.ustx`
- 🇨🇳 **中文本地化增强**：完善中文界面翻译，优化中文用户体验
- 🔧 **综合性改进**：性能优化、bug 修复等

## 许可证

本项目基于原版 OpenUTAU 的 MIT 许可证。详见 [LICENSE.txt](./LICENSE.txt)。

## 开发

详见 [CLAUDE.md](./CLAUDE.md) 了解项目架构和开发指南。

## 当前状态

- 构建：**0 错误**；测试：**355 通过 / 0 失败**（`dotnet test OpenUtau.Test\OpenUtau.Test.csproj`）
- 需要 **.NET SDK 9** 构建（Avalonia 12 分析器依赖 Roslyn 4.14+）
- 进行中：主编辑器编排区/轨头/素材库的细节对齐（设计稿 S2–S5）、其余控件收归自有主题

## 致谢

- [OpenUTAU](https://github.com/openutau/OpenUtau) — 原始项目
- 所有 OpenUTAU 贡献者和社区成员

---

**By XKLMY ︱ 使用 vibe coding（DeepSeek v4.1 Flash）**
