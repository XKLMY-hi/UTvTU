# W38 欢迎页「一小节真实演唱」波形资产

由 `OpenUtau.Test/Tools/WelcomeWaveformGenerator.cs` 生成（**默认 no-op**，不影响测试基线）。
本目录是**随包参考资产 + 依据留存**，不是运行时依赖。

## 怎么重新生成

```powershell
$env:OPENUTAU_GEN_WELCOME_WAVE='1'                                        # 开关（不设则生成器直接 return）
$env:OPENUTAU_GEN_VOICEBANK='C:\...\Documents\OpenUtau\Singers\足立レイver3.5.0'  # 可选：真声库路径
$env:OPENUTAU_GEN_WELCOME_OUT='G:\...\UTvTU-welcome\.opencode\design\welcome-waveform'
dotnet test OpenUtau.Test\OpenUtau.Test.csproj --filter FullyQualifiedName~WelcomeWaveformGenerator
```

> ⚠ 相对路径以**测试宿主的工作目录**为基准，请传绝对路径。

## 两个候选

| | 来源 | 时长 | 说明 |
|---|---|---|---|
| **b（随包定稿）** | **自研合成**：共振峰 /a/（F1 800 / F2 1200 / F3 2800）+ 谐波源 1/n^1.2 + 5.5 Hz 轻微颤音 + ADSR 45/90/200 ms | 0.75 s | 零第三方顾虑 ⇒ **随包 / 上线一律用 b** |
| a（本地对比参考） | **产品渲染链**（`ClassicRenderSetup` + `AudioFixtures.RenderMixdown`，720 ticks ≈ 0.75 s，tone 60） | 1.10 s | 见下"为什么 a 常以伪声库回退收场"；**不随包** |

- 包络：**112 点**（96–128 区间内取 112），每个候选给**绝对值**与**双极性**两版。
- 归一化：两候选**共用同一分母**（同一 `Mono` 域下的峰值）⇒ 图上"谁更响"是真实差异，不做各自自动增益。
- 产物：`waveform.json`（含 f0 曲线）、`waveform-a.png` / `waveform-b.png`、`waveform-compare.png`、
  `waveform-b.xaml`（可直接贴进视图的 1000×240 几何）、`waveform.log`（诊断 + 下面两段依据）。

## 为什么候选 a 常以「伪声库回退」收场

- **现象**：真声库渲染 20 s 超时，`TimeoutException`，诊断为 `PhonemesUpToDate=True, phonemes=1, phrases=0`
  —— 音素化出了 1 个音素，却没产出任何渲染短语。
- **原因**：真声库在 `character.yaml` 里**声明了自己的音素化器**，例如
  `足立レイver3.5.0` 的 `default_phonemizer: OpenUtau.Plugin.Builtin.JapanesePresampPhonemizer`；
  而本生成器复用的 `ClassicRenderSetup` 走**通用音素化路径**，没有按声库声明去走插件工厂
  （presamp 还需解析 `presamp.ini`）⇒ 拿不到「音素 → oto」映射 ⇒ 短语为空 ⇒ 超时。
  `桃音モモOU统合` 连 `character.yaml` 都没有，失败模式类似。
- ⚠ **这是「测试夹具缺 phonemizer 工厂」的限制，不是产品缺陷**：应用正常加载声库时走的是插件工厂
  （按 `character.yaml` 的 `default_phonemizer` 构造），产品里这类声库能正常渲染。
  **后人不要把它当 bug 去改产品代码。**
- **W38 裁决**：不为"看一眼观感"去打通插件工厂；a 保留为**本地对比参考**，随包用 b。

## 为什么不随包使用真声库渲染（许可原文与出处）

出处一：`<声库目录>\利用規約等\足立レイのガイドライン.txt`（Shift-JIS）

1. 「**音源の再配布は可能です。プログラムや機器に組み込んでの使用も可能です。**」
   ⇒ 再配布与"嵌入程序/设备"都被明确允许。
2. 「二次創作等については、同人活動の範囲内においては、基本的に**有償・無償を問わず自由**に…」
3. 「**法人による商用利用は、別途ご連絡ください。**」⇒ **唯一的保留条款**。

出处二：`<声库目录>\readme.txt`

4. 「このライブラリには**人の声は含まれていません**。sin波合成による母音を元に、手動により波形や
   周波数等を調整して作られた『人の声のように聞こえる音』の集合体です。」
   ⇒ 该声库**本身也是合成音源**（sin 波合成母音），与我们的候选 b 属同一类，只是走了产品链。

**结论**：许可属"随包可用"级别（第 3 条是法人商用的保留），但我们**已有零顾虑的自研资产** ⇒
直接用 b，不背第三方尾巴。以上原文即"为什么不随包"的留存依据。

## 顺带记录的两条工具坑（用这套测试工具的人会感谢）

1. 产品链按 `AudioFixtures.Channels` 读样本：**单声道数组会被当成立体声**
   ⇒ 时长减半、峰值翻倍。生成器已按声道交叠，并在代码里留了注释。
2. `AudioMeasure.Peak` **会把各声道相加**（双声道同相 ⇒ 2×）⇒ 归一化分母必须走 `Mono` 域，
   否则整幅画幅缩到一半。
