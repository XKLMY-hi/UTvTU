using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Input;
using OpenUtau.App.Commands;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 命令层契约（W28 / 产品设计 M09）。
    ///
    /// 命令注册表是「Id / 显示名 / 默认手势 / 处理体 / 分组」的**单一事实来源**；
    /// 这组用例把它守住 —— 加命令忘了文案、忘了处理体、或让两个命令抢同一手势，都在这里红。
    /// 另有一条**显示 ↔ 行为同源**的守卫：XAML 里显示的每个 InputGesture 都必须能在注册表里
    /// 找到同手势的命令（旧病是"菜单写着快捷键、按下去没反应"，例如修复前的 Ctrl+Shift+R）。
    ///
    /// 纯静态逻辑，不需要 headless 会话 ⇒ 用 [Fact]（不占 Avalonia 会话、跑得快）。
    /// </summary>
    public class CommandRegistryTests {
        /// <summary>测试固定用 Control 作主修饰键（macOS 的 Meta 由 <c>Resolve</c> 负责换算）。</summary>
        const KeyModifiers Cmd = KeyModifiers.Control;

        static HashSet<string> KeysOf(string fileName) {
            string path = Path.Combine(AppContext.BaseDirectory, "Strings", fileName);
            Assert.True(File.Exists(path), $"缺少随构建产出的语言文件：{path}（检查 OpenUtau.Test.csproj 的 None 链接）");
            return Regex.Matches(File.ReadAllText(path), "<system:String\\s+x:Key=\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .ToHashSet(StringComparer.Ordinal);
        }

        static string MainWindowXaml() =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Views", "MainWindow.axaml"));

        /// <summary>解析 XAML 里的 <c>InputGesture="Ctrl+Shift+S"</c> 写法 →（键, 修饰键）。</summary>
        static (Key Key, KeyModifiers Modifiers) ParseGesture(string text) {
            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries);
            KeyModifiers modifiers = KeyModifiers.None;
            for (int i = 0; i < parts.Length - 1; i++) {
                modifiers |= parts[i].Trim() switch {
                    "Ctrl" or "Control" => KeyModifiers.Control,
                    "Shift" => KeyModifiers.Shift,
                    "Alt" => KeyModifiers.Alt,
                    "Meta" or "Cmd" or "Win" => KeyModifiers.Meta,
                    var other => throw new InvalidOperationException($"未知修饰键：{other}"),
                };
            }
            return (Enum.Parse<Key>(parts[^1].Trim()), modifiers);
        }

        [Fact]
        public void EveryCommandHasIdNameKeyAndHandler() {
            Assert.NotEmpty(CommandRegistry.All);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in CommandRegistry.All) {
                Assert.False(string.IsNullOrWhiteSpace(c.Id), "命令 Id 不能为空");
                Assert.False(string.IsNullOrWhiteSpace(c.NameKey), $"{c.Id} 缺显示名键");
                Assert.NotNull(c.Execute);                                  // 处理体必须存在
                Assert.True(ids.Add(c.Id), $"命令 Id 重复：{c.Id}");
            }
        }

        [Fact]
        public void EveryNameKey_ExistsInBothLanguages() {
            var en = KeysOf("Strings.axaml");
            var zh = KeysOf("Strings.zh-CN.axaml");
            var missing = CommandRegistry.All
                .Where(c => !en.Contains(c.NameKey) || !zh.Contains(c.NameKey))
                .Select(c => $"{c.Id}→{c.NameKey}(EN={en.Contains(c.NameKey)},ZH={zh.Contains(c.NameKey)})")
                .ToList();
            Assert.True(missing.Count == 0, "显示名键缺失或 EN/zh 未成对：" + string.Join("; ", missing));
        }

        [Fact]
        public void NoTwoCommandsShareTheSameGesture() {
            var conflicts = CommandRegistry.Conflicts(Cmd);
            Assert.True(conflicts.Count == 0, "手势冲突：" + string.Join("; ",
                conflicts.Select(g => g.Key + " → " + string.Join("/", g.Select(c => c.Id)))));
        }

        /// <summary>同一命令的**别名手势**（如 Redo 的 Ctrl+Y 与 Ctrl+Shift+Z）不算冲突，但要能查到。</summary>
        [Fact]
        public void RedoHasTwoGestures_BothResolveToRedo() {
            var viaY = CommandRegistry.Match(Key.Y, Cmd, Cmd);
            var viaShiftZ = CommandRegistry.Match(Key.Z, Cmd | KeyModifiers.Shift, Cmd);
            Assert.NotNull(viaY);
            Assert.NotNull(viaShiftZ);
            Assert.Equal("edit.redo", viaY!.Id);
            // 别名是独立 Id（便于总览标注），但显示名与行为必须与主命令一致
            Assert.Equal(viaY.NameKey, viaShiftZ!.NameKey);
        }

        /// <summary>主修饰键按平台解析：表里写 Control，macOS 下换成 Meta。</summary>
        [Fact]
        public void Resolve_MapsPrimaryModifierToThePlatformCommandKey() {
            var mac = CommandRegistry.Resolve(new KeyGesture(Key.S, KeyModifiers.Control), KeyModifiers.Meta);
            Assert.NotNull(mac);
            Assert.Equal(Key.S, mac!.Key);
            Assert.Equal(KeyModifiers.Meta, mac.KeyModifiers);

            // 不含主修饰键的手势原样返回（F11 / Delete / Shift+S …）
            var shiftS = new KeyGesture(Key.S, KeyModifiers.Shift);
            Assert.Equal(shiftS.KeyModifiers, CommandRegistry.Resolve(shiftS, KeyModifiers.Meta)!.KeyModifiers);
            Assert.Null(CommandRegistry.Resolve(null, KeyModifiers.Meta));
        }

        /// <summary>
        /// **显示 ↔ 行为同源**（本卡的核心守卫）：XAML 里显示的每一个快捷键都必须在注册表里有对应命令。
        /// 反向不成立是**预期的**（注册表里还有尚未写进菜单的手势），差异由一致性清单登记。
        /// </summary>
        [Fact]
        public void EveryDisplayedInputGesture_ExistsInTheRegistry() {
            var displayed = Regex.Matches(MainWindowXaml(), "InputGesture=\"([^\"]+)\"")
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            Assert.True(displayed.Count > 0, "MainWindow.axaml 里没找到 InputGesture —— 解析或链接有问题");

            var orphans = new List<string>();
            foreach (string text in displayed) {
                var (key, modifiers) = ParseGesture(text);
                if (CommandRegistry.Match(key, modifiers, Cmd) == null) {
                    orphans.Add(text);
                }
            }
            Assert.True(orphans.Count == 0,
                "菜单显示了但注册表里没有的快捷键（= 按下去没反应）：" + string.Join(", ", orphans));
        }

        /// <summary>总览是按分组分区的 ⇒ 每个分组名都要有可用的本地化键（分组枚举全覆盖）。</summary>
        [Fact]
        public void EveryGroupIsCoveredByTheRegistry() {
            var groups = CommandRegistry.All.Select(c => c.Group).Distinct().ToList();
            foreach (CommandGroup g in Enum.GetValues<CommandGroup>()) {
                if (groups.Contains(g)) {
                    Assert.NotEmpty(CommandRegistry.InGroup(g));
                }
            }
            Assert.Contains(CommandGroup.File, groups);
        }

        /// <summary>手势文本与 XAML 的写法同源（总览显示的字符串要和菜单上看到的一致）。</summary>
        [Fact]
        public void GestureText_MatchesTheXamlWriting() {
            var save = CommandRegistry.Find("file.save")!;
            Assert.Equal("Ctrl+S", CommandRegistry.GestureText(CommandRegistry.Resolve(save.Gesture, Cmd)!));
            var saveAs = CommandRegistry.Find("file.saveas")!;
            Assert.Equal("Ctrl+Shift+S", CommandRegistry.GestureText(CommandRegistry.Resolve(saveAs.Gesture, Cmd)!));
            var fullscreen = CommandRegistry.Find("tools.fullscreen")!;
            Assert.Equal("F11", CommandRegistry.GestureText(CommandRegistry.Resolve(fullscreen.Gesture, Cmd)!));
        }
    }
}
