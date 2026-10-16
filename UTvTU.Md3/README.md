# UTvTU.Md3

从 OpenUtau.Core/Theming 原样抽离的纯 .NET 8 MD3 颜色计算引擎。不依赖 Avalonia、主程序或音频模块。
为保持源码兼容，保留 OpenUtau.Core.Theming 命名空间与既有 API。

算法、生成表、参考实现来源与许可沿用原仓库。生成器仍在 tools/md3-codegen；原有 132 × 49 个角色值与 HCT 参考向量位于 OpenUtau.Test/Core/Theming/Data。
UI 库和 Core 分别引用本项目，避免 UI 为使用颜色而引入音频依赖。
