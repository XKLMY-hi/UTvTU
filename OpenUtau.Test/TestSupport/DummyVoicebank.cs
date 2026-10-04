using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NAudio.Wave;
using OpenUtau.Classic;

namespace OpenUtau.Test.TestSupport {
    /// <summary>
    /// W7-1 伪声库（dummy voicebank）：**测试运行时生成**到临时目录，不提交二进制。
    ///
    /// 结构 = 最小可用 CV 经典声源：
    /// <list type="bullet">
    /// <item><c>character.txt</c>：<c>name=…</c>（<see cref="VoicebankLoader.ParseCharacterTxt"/> 认这个键）</item>
    /// <item><c>&lt;alias&gt;.wav</c>：每个音素一条**已知频率**的 16-bit 单声道正弦
    /// （44100 Hz、1 s、-4.4 dBFS、首尾 20 ms 淡入淡出避免分析边界），完全确定性</item>
    /// <item><c>oto.ini</c>：<c>&lt;wav&gt;=&lt;alias&gt;,offset,consonant,cutoff,preutter,overlap</c>
    /// （单位 ms，shift_jis 编码——ASCII 内容，兼容经典声库编码约定）</item>
    /// </list>
    ///
    /// 别名频率故意各不相同：渲染后可用频谱测量**验证输出确实是这个音素**。
    /// </summary>
    internal static class DummyVoicebank {
        public const string FolderName = "W7DUMMYCV";
        public const int WavSampleRate = 44100;
        public const double WavSeconds = 1.0;
        public const double WavAmp = 0.6;   // ≈ -4.4 dBFS 峰值

        /// <summary>别名 → 源样本基频（Hz）。</summary>
        public static readonly Dictionary<string, double> Aliases = new() {
            ["a"] = 220.00,   // A3
            ["i"] = 277.18,   // C#4
            ["u"] = 329.63,   // E4
            ["e"] = 440.00,   // A4
            ["o"] = 554.37,   // C#5
        };

        /// <summary>
        /// 经典声库加载路径会用 <c>Encoding.GetEncoding("shift_jis")</c>
        /// （<c>VoicebankLoader.LoadInfo</c>），必须先在进程内注册代码页提供程序。
        /// </summary>
        static void EnsureEncodingProvider() {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        /// <summary>生成伪声库，返回声库目录（父目录为 <paramref name="rootDir"/>）。</summary>
        public static string Create(string rootDir) {
            EnsureEncodingProvider();
            string dir = Path.Combine(rootDir, FolderName);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "character.txt"),
                "name=W7 Dummy CV\nauthor=OpenUtau Plus W7 fixture\nversion=1.0\n", new UTF8Encoding(false));

            var oto = new StringBuilder();
            foreach (var pair in Aliases) {
                string wav = $"{pair.Key}.wav";
                WriteToneWav(Path.Combine(dir, wav), pair.Value);
                // offset=0, consonant=0, cutoff=0（保留到样本尾）, preutter=50ms, overlap=20ms
                oto.AppendLine($"{wav}={pair.Key},0,0,0,50,20");
            }
            File.WriteAllText(Path.Combine(dir, "oto.ini"), oto.ToString(), Encoding.GetEncoding("shift_jis"));
            return dir;
        }

        static void WriteToneWav(string path, double freq) {
            int n = (int)(WavSeconds * WavSampleRate);
            var samples = new float[n];
            for (int i = 0; i < n; i++) {
                samples[i] = (float)(WavAmp * Math.Sin(2 * Math.PI * freq * i / WavSampleRate));
            }
            int fade = WavSampleRate / 50; // 20 ms
            for (int i = 0; i < fade; i++) {
                float g = (float)i / fade;
                samples[i] *= g;
                samples[n - 1 - i] *= g;
            }
            using var writer = new WaveFileWriter(path, new WaveFormat(WavSampleRate, 16, 1));
            writer.WriteSamples(samples, 0, n);
        }

        /// <summary>
        /// 加载伪声库 → <see cref="ClassicSinger"/>。
        /// 注意：<c>ToolsManager</c> 必须先 <c>Initialize()</c>，否则
        /// <c>GetResampler</c> 在空 map 上抛 KeyNotFoundException；
        /// <c>BasePath</c> 取声库目录的**父目录**，让 <c>Voicebank.Id</c> = 目录名。
        /// </summary>
        public static ClassicSinger Load(string voicebankDir) {
            EnsureEncodingProvider();
            ToolsManager.Inst.Initialize();
            var voicebank = new Voicebank {
                File = Path.Combine(voicebankDir, "character.txt"),
                BasePath = Path.GetDirectoryName(voicebankDir),
            };
            VoicebankLoader.IsTest = true;
            VoicebankLoader.LoadVoicebank(voicebank);
            var singer = new ClassicSinger(voicebank);
            singer.EnsureLoaded();
            return singer;
        }
    }
}
