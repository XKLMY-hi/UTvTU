using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using OpenUtau.App.ViewModels;
using OpenUtau.App.Views;

namespace OpenUtau.App.Commands {
    /// <summary>命令分组（快捷键总览按它分区；与菜单一/二级结构对齐但不强绑）。</summary>
    public enum CommandGroup {
        File,
        Edit,
        Project,
        Tools,
        View,
        Help,
    }

    /// <summary>
    /// 命令作用域：决定它由谁负责触发。
    /// 只登记**窗口级**命令；视图内部命令（如卷帘的 Ctrl+F 搜索音符）由视图自己处理，不入此表。
    /// </summary>
    public enum CommandScope {
        /// <summary>主窗口全局：任何视图下都可触发（若焦点在被授权消费按键的视图内，视图可先行处理）。</summary>
        Window,
        /// <summary>仅当遮罩（欢迎页 / 偏好设置）打开时生效（此时其余快捷键被吞掉）。</summary>
        Overlay,
    }

    /// <summary>
    /// 一条命令的**唯一定义**（W28 / 产品设计 M09）。
    ///
    /// 设计意图：`Id / 显示名键 / 默认手势 / 处理体 / 启用条件 / 分组` 六件事**只写这一处**，
    /// 菜单的 `InputGesture=` 与键盘分发都从这里取 —— 杜绝"显示一套、行为另一套"。
    /// </summary>
    /// <param name="Id">稳定标识（点号分层，如 <c>file.save</c>）。持久化与测试都用它，**不要改名**。</param>
    /// <param name="NameKey">显示名（i18n 键，EN/zh 必须成对；由契约测试守住）。</param>
    /// <param name="Group">分组（总览分区）。</param>
    /// <param name="Gesture">默认手势；<c>null</c> = 无默认绑定。主修饰键统一写 <see cref="KeyModifiers.Control"/>，
    /// 运行时由 <see cref="CommandRegistry.Resolve"/> 按平台换成 Meta（macOS）。</param>
    /// <param name="Execute">处理体。<c>MainWindow</c> 由分发器传入（窗口级命令都作用在窗口上）。</param>
    /// <param name="CanExecute">启用条件；<c>null</c> = 恒可用。仅用于总览显示与将来的命令面板，**不改变现有分发行为**。</param>
    /// <param name="Scope">作用域。</param>
    /// <param name="PreFocus">true = 在**焦点检查之前**分发（隧道 + 冒泡都会走；当前只有 Ctrl+M / Ctrl+W / Ctrl+S 三条）。</param>
    /// <param name="Note">备注（总览里显示"为什么没有快捷键"之类）。</param>
    public sealed record CommandDefinition(
        string Id,
        string NameKey,
        CommandGroup Group,
        KeyGesture? Gesture,
        Action<MainWindow> Execute,
        Func<MainWindow, bool>? CanExecute = null,
        CommandScope Scope = CommandScope.Window,
        bool PreFocus = false,
        string? Note = null);

    /// <summary>
    /// 窗口级命令注册表（单一事实来源）。
    ///
    /// 现状（迁移中）：菜单仍是 XAML 硬编码、键盘分发仍在 <c>MainWindow.OnKeyDown</c>；
    /// 本表先把**快捷键涉及的全部窗口级命令**收敛到一处，并成为
    /// ①快捷键总览 ②冲突检测 ③显示↔行为一致性清单 的共同输入。
    /// 菜单/快捷键随后**逐组**改为从本表生成（每组一次提交）。
    /// </summary>
    public static class CommandRegistry {
        /// <summary>主修饰键的规范写法：表里一律写 Control，运行时按平台解析。</summary>
        public const KeyModifiers PrimaryModifier = KeyModifiers.Control;

        static CommandDefinition Cmd(
            string id, string nameKey, CommandGroup group, KeyGesture? gesture,
            Action<MainWindow> execute, Func<MainWindow, bool>? canExecute = null,
            CommandScope scope = CommandScope.Window, bool preFocus = false, string? note = null) =>
            new(id, nameKey, group, gesture, execute, canExecute, scope, preFocus, note);

        /// <summary>全部窗口级命令。顺序 = 总览里的显示顺序（按分组聚集）。</summary>
        public static readonly IReadOnlyList<CommandDefinition> All = new[] {
            // ── File ────────────────────────────────────────────────────────────
            Cmd("file.new", "menu.file.new", CommandGroup.File,
                new KeyGesture(Key.N, PrimaryModifier), w => w.NewProject()),
            Cmd("file.open", "menu.file.open", CommandGroup.File,
                new KeyGesture(Key.O, PrimaryModifier), w => w.Open()),
            Cmd("file.save", "menu.file.save", CommandGroup.File,
                new KeyGesture(Key.S, PrimaryModifier), w => _ = w.Save(), preFocus: true),
            Cmd("file.saveas", "menu.file.saveas", CommandGroup.File,
                new KeyGesture(Key.S, PrimaryModifier | KeyModifiers.Shift), w => _ = w.SaveAs()),
            Cmd("file.render", "menu.file.render", CommandGroup.File,
                new KeyGesture(Key.R, PrimaryModifier | KeyModifiers.Shift),
                w => w.OnMenuRender(w, new Avalonia.Interactivity.RoutedEventArgs())),

            // ── Edit ────────────────────────────────────────────────────────────
            Cmd("edit.undo", "menu.edit.undo", CommandGroup.Edit,
                new KeyGesture(Key.Z, PrimaryModifier), w => w.ViewModel.Undo(),
                w => w.ViewModel.CanUndo),
            Cmd("edit.redo", "menu.edit.redo", CommandGroup.Edit,
                new KeyGesture(Key.Y, PrimaryModifier), w => w.ViewModel.Redo(),
                w => w.ViewModel.CanRedo),
            // Ctrl+Shift+Z 是 Redo 的第二手势：注册表显式登记，总览会把它标成"同一命令"的别名而不是冲突
            Cmd("edit.redo.alt", "menu.edit.redo", CommandGroup.Edit,
                new KeyGesture(Key.Z, PrimaryModifier | KeyModifiers.Shift), w => w.ViewModel.Redo(),
                w => w.ViewModel.CanRedo, scope: CommandScope.Window, note: "别名手势（Redo）"),
            Cmd("edit.selectallparts", "menu.edit.selectall", CommandGroup.Edit,
                new KeyGesture(Key.A, PrimaryModifier), w => w.ViewModel.TracksViewModel.SelectAllParts()),
            Cmd("edit.cutparts", "menu.edit.cut", CommandGroup.Edit,
                new KeyGesture(Key.X, PrimaryModifier), w => w.ViewModel.TracksViewModel.CutParts()),
            Cmd("edit.copyparts", "menu.edit.copy", CommandGroup.Edit,
                new KeyGesture(Key.C, PrimaryModifier), w => w.ViewModel.TracksViewModel.CopyParts()),
            Cmd("edit.pasteparts", "menu.edit.paste", CommandGroup.Edit,
                new KeyGesture(Key.V, PrimaryModifier), w => w.ViewModel.TracksViewModel.PasteParts()),
            Cmd("edit.deleteparts", "menu.edit.delete", CommandGroup.Edit,
                new KeyGesture(Key.Delete), w => w.ViewModel.TracksViewModel.DeleteSelectedParts()),

            // ── Project ─────────────────────────────────────────────────────────
            Cmd("track.solopart", "command.track.solopart", CommandGroup.Project,
                new KeyGesture(Key.S, KeyModifiers.Shift), w => w.SoloSelectedPart()),
            Cmd("track.mutepart", "command.track.mutepart", CommandGroup.Project,
                new KeyGesture(Key.M, KeyModifiers.Shift), w => w.MuteSelectedPart()),

            // ── Tools ───────────────────────────────────────────────────────────
            Cmd("tools.mixer", "menu.tools.mixer", CommandGroup.Tools,
                new KeyGesture(Key.M, PrimaryModifier),
                w => w.OnMenuMixer(w, new Avalonia.Interactivity.RoutedEventArgs()), preFocus: true),
            Cmd("tools.mixerattach", "command.view.mixerattach", CommandGroup.Tools,
                new KeyGesture(Key.W, PrimaryModifier), w => w.ToggleMixerWindow(), preFocus: true),
            Cmd("tools.fullscreen", "menu.tools.fullscreen", CommandGroup.Tools,
                new KeyGesture(Key.F11),
                w => w.OnMenuFullScreen(w, new Avalonia.Interactivity.RoutedEventArgs())),
            Cmd("tools.shortcutoverview", "command.shortcutoverview", CommandGroup.Tools, null,
                w => ShortcutOverviewWindow.Open(w), note: "只读总览（本表生成，含冲突标记）"),

            // ── 视图切换（W48）：与顶栏胶囊**同源**（同一条 ViewSwitcher.SwitchTo）
            //    ⚠ 接线待办（第二步）：MainWindow.MapGlobalShortcut 目前是**硬编码白名单**投影，
            //    新命令会被 Match 命中后落到 GlobalShortcut.None ⇒ 还需补 3 个枚举成员 + 3 行映射
            //    + 3 处分发；等 fx-ctl 的 W47 落地后再动 MainWindow.axaml.cs（避免两写者改同一文件）。
            Cmd("view.workspace", "command.view.gotoworkspace", CommandGroup.View,
                new KeyGesture(Key.D1, PrimaryModifier),
                w => w.ViewModel.ViewSwitcher.SwitchTo(AppSurface.Workspace)),
            Cmd("view.pianoroll", "command.view.gotopiano", CommandGroup.View,
                new KeyGesture(Key.D2, PrimaryModifier),
                w => w.ViewModel.ViewSwitcher.SwitchTo(AppSurface.PianoRoll)),
            Cmd("view.mixer", "command.view.gotomixer", CommandGroup.View,
                new KeyGesture(Key.D3, PrimaryModifier),
                w => w.ViewModel.ViewSwitcher.SwitchTo(AppSurface.Mixer)),

            // ── View / 播放 ─────────────────────────────────────────────────────
            Cmd("playback.playpause", "command.playback.playpause", CommandGroup.View,
                new KeyGesture(Key.Space), w => w.PlayOrPause()),
            Cmd("playback.gohome", "command.playback.gohome", CommandGroup.View,
                new KeyGesture(Key.Home), w => w.ViewModel.PlaybackViewModel.MovePlayPos(0)),
            Cmd("playback.goend", "command.playback.goend", CommandGroup.View,
                new KeyGesture(Key.End), w => w.MovePlayPosToEnd()),

            // ── 窗口级杂项 ──────────────────────────────────────────────────────
            Cmd("window.escape", "command.window.escape", CommandGroup.View,
                new KeyGesture(Key.Escape), w => w.CloseOverlay(), canExecute: null, scope: CommandScope.Overlay,
                note: "遮罩打开时生效（欢迎页 / 偏好设置）"),
            Cmd("window.quit", "command.window.quit", CommandGroup.File,
                new KeyGesture(Key.F4, KeyModifiers.Alt), w => w.QuitApplication()),
        };

        static readonly Dictionary<string, CommandDefinition> ById =
            All.ToDictionary(c => c.Id, StringComparer.Ordinal);

        /// <summary>按 Id 取命令；不存在返回 null。</summary>
        public static CommandDefinition? Find(string id) =>
            ById.TryGetValue(id, out var def) ? def : null;

        public static IReadOnlyList<CommandDefinition> InGroup(CommandGroup group) =>
            All.Where(c => c.Group == group).ToList();

        /// <summary>把表里的规范手势解析成当前平台的实际手势（macOS 把 Control 换成 Meta）。</summary>
        public static KeyGesture? Resolve(KeyGesture? gesture, KeyModifiers cmdKey) {
            if (gesture == null) {
                return null;
            }
            if (!gesture.KeyModifiers.HasFlag(PrimaryModifier)) {
                return gesture;
            }
            var modifiers = (gesture.KeyModifiers & ~PrimaryModifier) | cmdKey;
            return new KeyGesture(gesture.Key, modifiers);
        }

        /// <summary>
        /// 按（键 + 修饰键）查找命令。<paramref name="cmdKey"/> 由调用方给出（macOS = Meta）。
        /// 多个命令命中同一手势时返回**第一个**（顺序 = <see cref="All"/> 顺序），冲突由 <see cref="Conflicts"/> 负责暴露。
        /// </summary>
        public static CommandDefinition? Match(Key key, KeyModifiers modifiers, KeyModifiers cmdKey,
            CommandScope scope = CommandScope.Window) =>
            Matches(key, modifiers, cmdKey, scope).FirstOrDefault();

        /// <summary>同 <see cref="Match"/>，但返回全部命中项（冲突诊断用）。</summary>
        public static IReadOnlyList<CommandDefinition> Matches(Key key, KeyModifiers modifiers,
            KeyModifiers cmdKey, CommandScope scope = CommandScope.Window) =>
            All.Where(c => c.Scope == scope)
               .Where(c => {
                   var g = Resolve(c.Gesture, cmdKey);
                   return g != null && g.Key == key && g.KeyModifiers == modifiers;
               })
               .ToList();

        /// <summary>
        /// 冲突 = 同一手势被多个命令占用。**同命令的别名手势不算冲突**（按 Id 去重后再比手势）。
        /// </summary>
        public static IReadOnlyList<IGrouping<string, CommandDefinition>> Conflicts(KeyModifiers cmdKey) =>
            Conflicts(All, cmdKey);

        /// <summary>同 <see cref="Conflicts(KeyModifiers)"/>，但作用于任意命令集合（总览的自检与测试用）。</summary>
        public static IReadOnlyList<IGrouping<string, CommandDefinition>> Conflicts(
            IReadOnlyList<CommandDefinition> commands, KeyModifiers cmdKey) {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var unique = new List<CommandDefinition>();
            foreach (var c in commands) {
                var g = Resolve(c.Gesture, cmdKey);
                if (g == null) {
                    continue;
                }
                // 同一 Id 的重复手势只算一次
                if (seen.Add(c.Id + "|" + g.Key + "|" + g.KeyModifiers)) {
                    unique.Add(c);
                }
            }
            return unique
                .GroupBy(c => GestureText(Resolve(c.Gesture, cmdKey)!), StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .ToList();
        }

        /// <summary>手势的显示文本（总览与诊断共用；与 XAML 的 <c>InputGesture</c> 写法一致）。</summary>
        public static string GestureText(KeyGesture gesture) {
            var sb = new System.Text.StringBuilder();
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Control)) {
                sb.Append("Ctrl+");
            }
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Meta)) {
                sb.Append("Meta+");
            }
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Alt)) {
                sb.Append("Alt+");
            }
            if (gesture.KeyModifiers.HasFlag(KeyModifiers.Shift)) {
                sb.Append("Shift+");
            }
            sb.Append(gesture.Key);
            return sb.ToString();
        }
    }
}
