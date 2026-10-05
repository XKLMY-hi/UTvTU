using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using OpenUtau.App.Commands;
using OpenUtau.App.ViewModels;
using Xunit;

namespace OpenUtau.Test.App {
    /// <summary>
    /// 快捷键总览（只读页）行为测试。
    ///
    /// 需要 <see cref="ThemeManager"/> 取显示名 ⇒ 走 headless 会话（[AvaloniaFact]）。
    /// 冲突标记用**合成命令集合**验证 —— 注册表当前无冲突，只有合成数据才能证明"标记真的会亮"。
    /// </summary>
    [Collection("Theme")]
    public class ShortcutOverviewTests {
        const KeyModifiers Cmd = KeyModifiers.Control;

        static CommandDefinition Fake(string id, string nameKey, KeyGesture? gesture) =>
            new(id, nameKey, CommandGroup.Tools, gesture, _ => { });

        [AvaloniaFact]
        public void Overview_ListsEveryRegistryCommand_Grouped() {
            var vm = new ShortcutOverviewViewModel(cmdKey: Cmd);
            int rows = vm.Groups.Sum(g => g.Rows.Count);
            Assert.Equal(CommandRegistry.All.Count, rows);
            Assert.True(vm.Groups.Count > 1, "应按分组分区（至少两个分组）");
            Assert.False(vm.IsEmpty);
            // 注册表当前无冲突 ⇒ 摘要走"无冲突"文案，且没有任何行被标记
            Assert.False(vm.HasConflicts);
            Assert.DoesNotContain(vm.Groups.SelectMany(g => g.Rows), r => r.IsConflicting);
        }

        [AvaloniaFact]
        public void Overview_MarksConflicts_WhenTwoCommandsShareAGesture() {
            var commands = new List<CommandDefinition> {
                Fake("test.a", "menu.file.new", new KeyGesture(Key.K, Cmd)),
                Fake("test.b", "menu.file.open", new KeyGesture(Key.K, Cmd)),   // 与 a 抢同一手势
                Fake("test.c", "menu.file.save", new KeyGesture(Key.L, Cmd)),
            };
            var vm = new ShortcutOverviewViewModel(commands, Cmd);

            var rows = vm.Groups.SelectMany(g => g.Rows).ToList();
            Assert.Equal(3, rows.Count);
            Assert.Equal(2, rows.Count(r => r.IsConflicting));
            Assert.False(rows.Single(r => r.Id == "test.c").IsConflicting);
            Assert.True(vm.HasConflicts);
            Assert.Contains("Ctrl+K", vm.ConflictSummary);   // 摘要点名冲突手势
        }

        [AvaloniaFact]
        public void Overview_AliasGestureOfTheSameCommand_IsNotAConflict() {
            // 同一命令两条手势（如 Redo 的 Ctrl+Y / Ctrl+Shift+Z）不算冲突
            var commands = new List<CommandDefinition> {
                Fake("test.main", "menu.edit.redo", new KeyGesture(Key.Y, Cmd)),
                Fake("test.alias", "menu.edit.redo", new KeyGesture(Key.Z, Cmd | KeyModifiers.Shift)),
            };
            var vm = new ShortcutOverviewViewModel(commands, Cmd);
            Assert.False(vm.HasConflicts);
            Assert.DoesNotContain(vm.Groups.SelectMany(g => g.Rows), r => r.IsConflicting);
        }

        [AvaloniaFact]
        public void Overview_Search_FiltersByNameGestureAndId() {
            var vm = new ShortcutOverviewViewModel(cmdKey: Cmd);
            int allRows = vm.Groups.Sum(g => g.Rows.Count);

            vm.Search = "Ctrl+M";
            var byGesture = vm.Groups.SelectMany(g => g.Rows).ToList();
            Assert.Contains(byGesture, r => r.Id == "tools.mixer");
            Assert.True(byGesture.Count < allRows);
            Assert.All(byGesture, r => Assert.Contains("Ctrl+M", r.Gesture + r.Id + r.Name));

            vm.Search = "playback.gohome";   // 按 id
            Assert.Single(vm.Groups.SelectMany(g => g.Rows));

            vm.Search = "zzz-no-such-command";   // 无匹配 ⇒ 空态
            Assert.True(vm.IsEmpty);
            Assert.Empty(vm.Groups);

            vm.Search = string.Empty;            // 清空 ⇒ 恢复全量
            Assert.Equal(allRows, vm.Groups.Sum(g => g.Rows.Count));
        }

        [AvaloniaFact]
        public void Overview_CommandWithoutGesture_IsListedButNeverConflicting() {
            var vm = new ShortcutOverviewViewModel(cmdKey: Cmd);
            var row = vm.Groups.SelectMany(g => g.Rows).Single(r => r.Id == "tools.shortcutoverview");
            Assert.Equal(string.Empty, row.Gesture);
            Assert.False(row.IsConflicting);
            Assert.True(row.HasNote);   // 该命令带 note（说明为何没有快捷键）
        }

        [AvaloniaFact]
        public void Overview_GestureText_FollowsThePlatformCommandKey() {
            var mac = new ShortcutOverviewViewModel(cmdKey: KeyModifiers.Meta);
            var saveRow = mac.Groups.SelectMany(g => g.Rows).Single(r => r.Id == "file.save");
            Assert.Equal("Meta+S", saveRow.Gesture);   // macOS 下不再显示 Ctrl+S
        }
    }
}
