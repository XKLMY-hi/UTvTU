using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Input;
using OpenUtau.App.Commands;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// W48 视图切换快捷键 · 第一步契约（**零行为变化**那一半）：
    /// 三条命令进注册表 + 显示名键 EN/zh 成对 + 手势无冲突。
    ///
    /// ⚠ 接线现状：`MainWindow.MapGlobalShortcut` 是**硬编码白名单**投影（`_ => GlobalShortcut.None`），
    /// 所以这三条目前只会被 `Match` 命中、不会真的切换视图 —— 那是第二步（等 fx-ctl 的 W47 落地后
    /// 再补枚举 + 映射 + 分发），本文件**不**断言"按下去就切换"。
    /// </summary>
    public class ViewShortcutRegistryTests {
        static readonly (string Id, Key Key, string NameKey)[] Expected = {
            ("view.workspace", Key.D1, "command.view.gotoworkspace"),
            ("view.pianoroll", Key.D2, "command.view.gotopiano"),
            ("view.mixer", Key.D3, "command.view.gotomixer"),
        };

        [Fact]
        public void ViewCommands_AreRegistered_WithCtrl123() {
            foreach (var (id, key, nameKey) in Expected) {
                var def = CommandRegistry.All.SingleOrDefault(c => c.Id == id);
                Assert.True(def != null, $"注册表里缺少命令 {id}");
                Assert.Equal(nameKey, def!.NameKey);
                Assert.NotNull(def.Gesture);
                Assert.Equal(key, def.Gesture!.Key);
                Assert.Equal(CommandRegistry.PrimaryModifier, def.Gesture.KeyModifiers);
                Assert.Equal(CommandGroup.View, def.Group);
                Assert.NotNull(def.Execute);
            }
        }

        [Fact]
        public void ViewCommands_HavePairedEnAndZhDisplayNames() {
            var en = KeysOf("Strings.axaml");
            var zh = KeysOf("Strings.zh-CN.axaml");
            foreach (var (id, _, nameKey) in Expected) {
                Assert.True(en.Contains(nameKey), $"EN 缺键 {nameKey}（{id}）");
                Assert.True(zh.Contains(nameKey), $"zh-CN 缺键 {nameKey}（{id}）");
            }
        }

        [Fact]
        public void ViewCommands_HaveNoGestureConflicts() {
            // 注册表自带冲突检测：同组 ≥2 条不同命令 = 冲突（别名手势用同一 nameKey，不算冲突）
            var groups = CommandRegistry.Conflicts(CommandRegistry.PrimaryModifier);
            var real = groups
                .Select(g => g.Select(c => c.NameKey).Distinct().ToList())
                .Where(names => names.Count > 1)
                .ToList();
            Assert.True(real.Count == 0,
                "存在真实手势冲突：" + string.Join("；", real.Select(n => string.Join("/", n))));
            // 顺带确认三条命令自己没被算进任何冲突组
            var conflicted = groups.SelectMany(g => g).Select(c => c.Id).ToHashSet();
            foreach (var (id, _, _) in Expected) {
                Assert.False(conflicted.Contains(id), $"{id} 落在冲突组里");
            }
        }

        [Fact]
        public void ViewCommands_AreResolvedByTheRegistryDispatchPath() {
            // W47 之后窗口级分发 = 纯注册表（HandleGlobalShortcut → TryExecuteShortcut）
            foreach (var (id, key, _) in Expected) {
                var hit = CommandRegistry.Match(key, CommandRegistry.PrimaryModifier, CommandRegistry.PrimaryModifier);
                Assert.NotNull(hit);
                Assert.Equal(id, hit!.Id);
                Assert.Equal(CommandScope.Window, hit.Scope);
                Assert.False(hit.PreFocus);
                Assert.Null(CommandRegistry.Match(key,
                    CommandRegistry.PrimaryModifier | KeyModifiers.Shift, CommandRegistry.PrimaryModifier));
            }
        }
        static HashSet<string> KeysOf(string fileName) {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "OpenUtau", "Strings"))) {
                dir = dir.Parent;
            }
            Assert.True(dir != null, "找不到仓库根（OpenUtau/Strings）");
            var path = Path.Combine(dir!.FullName, "OpenUtau", "Strings", fileName);
            Assert.True(File.Exists(path), $"缺文件 {path}");
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            return XDocument.Load(path).Root!
                .Elements()
                .Select(e => (string?)e.Attribute(x + "Key"))
                .Where(k => k != null)
                .Select(k => k!)
                .ToHashSet();
        }
    }
}
