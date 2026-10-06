# 品牌线独立验收 · fx-verify · 树 `plus-develop @ 8075a916`

1. **几何来源真实性**：`brand-mark` / `brand-brace` / `brand-v-chevron` 与 `extracted/{mark.svg, brace-open.svg, v-chevron.svg}` **逐字一致**（去空白后字符数 102=102 / 49=49 / 12=12，自写脚本逐字符比对，无差异）✔。`brand-wordmark` **不逐字相等**（XAML 1813 vs SVG 2104 字符；首差 @6 = `M153.5098,0.0004` vs `M153.5100560`）—— 这是**设计内的展平+取整**（`Icons.axaml:188-202` 注释 + 契约用例文档写明"U 用自身 matrix、T 用顶层 g"），不是描摹偏差。
2. **防漂移用例（覆盖字标）**：`dotnet test --filter FullyQualifiedName~BrandGeometryContractTests` → **4/4 通过 exit 0**（该用例前 3 条直接比 `d`、第 4 条按展平规则+指纹比字标，并断言 `Icons.axaml` 无额外派生键）✔ ⇒ 第 1 项判 **PASS（含口径）**。
3. **❌ FAIL：产品内仍有派生 monoline 引用（且是悬空键）**。`OpenUtau/Views/UpdaterDialog.axaml:22` `Data="{StaticResource brand-wordmark-interim-monoline}"`；全仓（含 *.xaml/*.json/*.md）**只有这一处引用、没有任何源码定义**（仅命中生成物 `OpenUtau/obj/Debug/net8.0-windows/Avalonia/resources:318164`）⇒ ① 违反"不得引用派生 monoline"；② 悬空 `StaticResource`，一旦实例化 `UpdaterDialog`（可达：`MainWindow.axaml.cs:1352` `new UpdaterDialog()`、`:178` `UpdaterDialog.CheckForUpdate`）即 XAML 载入抛异常（本 fork 更新检查被禁用 ⇒ 目前潜伏）。**最小复现**：`Select-String brand-wordmark-interim-monoline` 全仓只出 1 处引用 0 处定义；或直接 `new UpdaterDialog()` 加载。
4. **Assets/brand 目录**：仅 `README.md` + `utvtu.ico` + `utvtu-icon-16/24/32.png` ✔；无 `out/` 目录、无派生件被 XAML/代码引用（除第 3 条那条）✔。
5. **真机①**：应用正常启动（⇒ 无非法 XAML）；启动窗 `{ 脸 UTvTU }` 锁形全部走矢量：`SplashWindow.axaml:32-61` 五个 `Path Data={StaticResource brand-*}`，文件内 **无 Image/Bitmap** ✔；welcome 卡品牌区实测 ink 盒 `x[60,257] y[70,119]`（198×50）已渲染，脸标底取到用户主题 `md3.primary`（本机 `#95CDF7`，其 seed 偏蓝）+ `on-primary` 墨色 ✔。⚠️ 启动窗本身未单独抓到切片（1s/12s 两次列窗只剩主窗）⇒ "启动窗"细分项**未验证**，"矢量+描边口径"为**代码审查 PASS**。
6. **真机②窗口/任务栏图标**：`Styles.axaml:30` 与 30+ 窗口统一 `Icon="/Assets/brand/utvtu.ico"` ✔；标题栏图标区像素主色 **`#5A44E0`（品牌紫）+ `#FFFFFF`**（40×32 区域）⇒ **新紫底脸标** ✔ PASS。
7. **真机③非法写法**：全仓 `Fill="none"` 仅 **1 命中且是注释**（`SplashWindow.axaml:51` 在解释"Avalonia 里无填充=不写 Fill"）⇒ **真实命中 0** ✔；应用启动成功 = 运行期反证 ✔。
8. **EvenOdd 契约**：`wordmark-split-report.json` 自证数字 = **原样 EvenOdd 挖空 656 px → 重划后 0 px**（还有 `resplit_evenodd_vs_original_nonzero_diff_px: 0`），note 明写"T(g38) 两个重叠矩形 → 重划为 3 个不重叠子矩形" ✔；`wordmark-ut-evenodd-vs-nonzero.png` 在库 ✔；字标覆盖见第 2 项用例 ✔。
9. **可追溯性**：`Assets/brand/README.md` 五条踩坑关键词全中（`6.9`×1、`flipx`×1、`180`×2、`不能含`×1、`EvenOdd`×5、`Fill="none"`×1）+ 展平节（`展平`×3）✔；`EXTRACT-MANIFEST.md` 有 **A″（:117）** 与 **A″-1（:153）** 条目 ✔。
10. **结论分级**：**FAIL 1 条**（第 3 项：派生 monoline 悬空引用）；PASS 5 条（1/2/4/6/7/8/9 中的相应项）；**未验证**：启动窗独立切片（第 5 项细分）。其余无 FAIL。
11. 未改任何产品代码；实例已按 PID + 精确前缀 `…\UTvTU\OpenUtau` 关闭（0 残留）；主题仍 Dark；主树 `git status` = 0 项；证据：`bv-01-main.png`（本轮唯一新截图）。
12. **给 Lead 的最小处置建议**：把 `UpdaterDialog.axaml:22` 改为 `{StaticResource brand-wordmark}`（或删除该字标行），或恢复该键定义——现状是"派生件残留 + 悬空引用"双料问题；修完请顺带让契约用例扫全仓键引用（现在只扫 `Icons.axaml`）。
