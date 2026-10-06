# Assets/brand —— 产品内品牌资产（W40）

> 几何**唯一权威来源**：`.opencode/design/brand/extracted/**`（用户设计稿的**纯提取件**，逐字原生 path）。
> `Icons.axaml` 里的 `brand-*` 几何由 `OpenUtau.Test/App/BrandGeometryContractTests.cs` 常驻逐字校验；
> **几何若变，消费方必须重取**（改 `Icons.axaml` 后必须重建再跑测试）。
> `.opencode/design/brand/out/**` 是**派生目录**（光学补偿 / 字距重排 / 描摹），**不作产品来源**。

## 文件与状态

| 文件 | 状态 | 说明 |
|---|---|---|
| `utvtu.ico` | ✅ 可用 | 16/24/32/48/64/128/256 逐尺寸；**只含标志几何**（逐字提取件）+ CSS 翻译的瓦片底色，**不含字标** ⇒ 本轮不重出 |
| `utvtu-icon-16.png` / `-24` / `-32.png` | ✅ 可用 | 同上（逐尺寸瓦片） |
| `utvtu-splash@1x.png` / `@2x.png` | ⏳ **待重做** | 内含**派生字标** ⇒ 等 PDF `Type3 CharProcs` 轮廓到位后重出；**不阻塞**（应用内启动窗是矢量绘制，PNG 只服务安装包/物料） |
| `OpenUtau.icns`（上一级目录） | ⏳ 待办 | 缺 512/1024 栅格，维持现状 |
| `*.txt`（几何/清单留档） | 参考资料 | `utvtu-brand-lockup.axaml.txt` 等派生目录文件的只读留档，**不是来源** |

## 两条踩坑结论（"派生件会引入错误"的实例）

1. **派生字标坐标比例错 6.9×**：早前用源像素 `bbox_px` 给切角 v 定尺度，与 U/T 的归一化空间相差 ~6.9×，
   合成 bbox 变成 1226.68×694 ⇒ 字标被缩到极小并多出一个大三角；同源 splash PNG 的裁切也一起错位
   （1x 图只剩右半截）。fx-rack 已加生成器断言 `assert_wordmark_sane()` 防这类"数字看着对、整体崩"。
2. **派生"预翻转"括号是 180° 旋转不是镜像**：派生键 `brand-brace-flipx` 把 `x′=24−x` **和** `y′=24−y`
   同时施加（源点 (20,4) → (4,20)），是绕 (12,12) 的 180° 旋转；包围盒从 y 4..40 变成 y −16..20，
   在 `Stretch=Uniform` 组合里会错位。**用户设计稿本身没有独立右括号几何** ——
   `extracted/brace-close.svg` = 同一条 brace 路径 + `transform: scaleX(-1)`（origin top_left）
   ⇒ 产品侧正解就是 `brand-brace` + `ScaleX(-1)`，不要预烘焙镜像。
