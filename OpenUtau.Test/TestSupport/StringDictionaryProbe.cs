using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

namespace OpenUtau.Test.TestSupport {
    /// <summary>
    /// 多语言资源字典探针（W12）：直接在测试里加载 <c>avares://OpenUtau/Strings/*.axaml</c>，
    /// **不调用 <c>App.SetLanguage</c>**。
    ///
    /// 为什么：语言是**进程级全局状态**，而 xUnit 只在同一个 Collection 内串行；
    /// 过去用 SetLanguage 做"双语键存在"断言，会在切换窗口期污染**并行**的其它用例
    /// （读到 zh 值）——这正是集成树里出现过一次的"Light 变体偶发失败"最可能的来源。
    /// 直接读字典既没有全局副作用，又能顺带断言两个文件的**键集完全一致**（比原来更强）。
    /// </summary>
    public static class StringDictionaryProbe {
        /// <summary>加载语言文件（如 "Strings.axaml" / "Strings.zh-CN.axaml"）。</summary>
        public static ResourceDictionary Load(string fileName) =>
            (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri($"avares://OpenUtau/Strings/{fileName}"));

        /// <summary>英文基准字典。</summary>
        public static ResourceDictionary English() => Load("Strings.axaml");

        /// <summary>简体中文字典。</summary>
        public static ResourceDictionary Chinese() => Load("Strings.zh-CN.axaml");

        /// <summary>取键值（缺失返回 null）。</summary>
        public static string? Value(ResourceDictionary dictionary, string key) =>
            dictionary.TryGetResource(key, ThemeVariant.Default, out object? value) && value is string s ? s : null;

        /// <summary>键集合。</summary>
        public static HashSet<string> Keys(ResourceDictionary dictionary) =>
            dictionary.Keys.OfType<string>().ToHashSet(StringComparer.Ordinal);
    }
}
