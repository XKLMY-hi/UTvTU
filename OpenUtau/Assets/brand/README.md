# Assets/brand —— 产品内品牌资产（W40）

> 几何**唯一权威来源**：`.opencode/design/brand/extracted/**`（用户设计稿的**纯提取件**，逐字原生 path，
> 已纳入版本控制）。`Icons.axaml` 里的 `brand-*` 几何由
> `OpenUtau.Test/App/BrandGeometryContractTests.cs` 常驻逐字校验；
> **几何若变，消费方必须重取**（改 `Icons.axaml` 后必须重建再跑测试）。
> `.opencode/design/brand/out/**` 是**派生目录**（光学补偿 / 字距重排 / 描摹），**不作产品来源**；
> 产品内**不留任何设计目录的副本**（同一份东西放两处迟早漂移）。

## 本目录只保留两类东西

| 文件 | 状态 | 说明 |
|---|---|---|
| `utvtu.ico` | ✅ | 16/24/32/48/64/128/256 逐尺寸；**只含标志几何（逐字提取件）+ CSS 翻译的瓦片底色，不含字标** ⇒ 无需重出 |
| `utvtu-icon-16.png` / `-24` / `-32.png` | ✅ | 同上（逐尺寸瓦片） |

**已按裁决删除**（无代码/安装包引用，属"出厂用不到"）：
`utvtu-splash@1x.png` / `@2x.png`（含派生字标、且零消费方）、`out/` 派生时代的留档副本
（`utvtu-brand-*.axaml.txt`、`utvtu-mark-source.svg`、`utvtu-brace-source*.svg`、两份 metrics JSON）、
以及提取件的产品内镜像 `extracted/**`。
若将来做物料（商店页 / README 头图）需要 splash 栅格，**从启动窗 XAML 本体渲染**
（那份 XAML 就是事实、永不漂移），不要再从设计目录拷一份栅格。

## 五条踩坑结论（"派生件会引入错误"与"只有真机能暴露"的实例）

1. **派生字标坐标比例错 6.9×**：早前用源像素 `bbox_px` 给切角 v 定尺度，与 U/T 的归一化空间相差 ~6.9×，
   合成 bbox 变成 1226.68×694 ⇒ 字标被缩到极小并多出一个大三角；同源 splash PNG 的裁切也一起错位。
   （fx-rack 已加生成器断言 `assert_wordmark_sane()`。）
2. **派生"预翻转"括号是 180° 旋转不是镜像**：派生键 `brand-brace-flipx` 把 `x′=24−x` **和** `y′=24−y`
   同时施加（源点 (20,4) → (4,20)），是绕 (12,12) 的 180° 旋转；包围盒从 y 4..40 变成 y −16..20，
   在 `Stretch=Uniform` 组合里会错位。**用户设计稿本身没有独立右括号几何** ——
   `extracted/brace-close.svg` = 同一条 brace 路径 + `transform: scaleX(-1)`（origin top_left）
   ⇒ 产品侧正解就是 `brand-brace` + `ScaleX(-1)`，不要预烘焙镜像。
3. **字标键不能含切角 v**：`StreamGeometry` 只支持 EvenOdd，而切角 v 是**自交单环**，
   混进同一个键会在 EvenOdd 下填出贯穿细缝（NonZero 才对）。
   ⇒ `brand-wordmark` 取 **U/T-only** 提取件；v 由消费方用 `brand-v-chevron` **单独描边**渲染（描边不经填充规则）。
4. **U/T-only ≠ EvenOdd 安全**：T（`g38`）的 CharProc 用**两个互相重叠的矩形**画（nonzero 并集才是 T），
   EvenOdd 会把重叠区**挖空 656 px**（T 的字身与横杠脱开）⇒ 提取件已把 T **精确重划为 3 个互不重叠子矩形**
   （纯分区、几何零改动，自证三联图：原样 EvenOdd 656 / 原样 NonZero / 重划后 0）。
   **不要用未重划的原始提取**（`wordmark-outline.svg` 那份 5 段合并版只在 NonZero 下正确，仅作取证/对照）。
5. **真机 vs Build/headless**：`Fill="none"` 在 Avalonia 是**非法 brush 字符串**，会在 XAML 载入时抛
   `Invalid brush string: 'none'`（本仓启动窗因此整机启动即退）—— Build 0 错误、headless 全绿都拦不住，
   只有真机跑才暴露。**"无填充"= 不写 `Fill`**。

## 字标展平（`brand-wordmark` 从 `extracted/wordmark-ut.svg` 到 XAML）

该源有两个对消费方不友好的特征，展平脚本按固定规则处理（规则写在 `Icons.axaml` 该键注释里）：
- 4 条 path 的 **transform 语义不一致**：U 两条要用**自身 matrix**、T 两条要用**顶层 `g`**；
  判据 = 哪条变换能让该 path 落进文件声明的 viewBox（两种读法都落不进时**直接报错，不猜**）。
- 它的 `d` 是 **PDF 内容流语法**（`x y m` / `6 数 c` / `h`，操作数在前），**不是 SVG 语法**（`M x y`）
  ⇒ 用标准 SVG 渲染器直接解析会错位。
展平结果自检：墨迹盒与 viewBox **精确一致**、8 个子路径、独立光栅化肉眼确认为 `UT TU` 形；
源指纹（viewBox + 4 条 `d` 的 SHA256）记在键注释与防漂移用例里。

