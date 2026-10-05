using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Input;
using OpenUtau.App.Commands;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    /// <summary>
    /// 快捷键总览（W28 / 产品设计 M11 的只读前置）。
    ///
    /// 数据全部来自 <see cref="CommandRegistry"/>（单一事实来源），所以**不会再出现
    /// "菜单显示的快捷键与实际行为不一致"** —— 两边读的是同一张表。
    /// 本页只读：展示 名称 / 手势 / 分组 / 作用域，并**自动标出冲突**（同一手势绑到多个命令）。
    /// </summary>
    public sealed class ShortcutOverviewViewModel : ViewModelBase {
        /// <summary>一行命令（总览的显示单元）。</summary>
        public sealed class ShortcutRow {
            public string Id { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public string Gesture { get; init; } = string.Empty;
            public string Scope { get; init; } = string.Empty;
            public string Note { get; init; } = string.Empty;
            public CommandGroup Group { get; init; }
            public bool HasNote => !string.IsNullOrEmpty(Note);
            public bool IsConflicting { get; init; }
        }

        /// <summary>一个分组（总览按分组分区）。</summary>
        public sealed class ShortcutGroup {
            public string Title { get; init; } = string.Empty;
            public IReadOnlyList<ShortcutRow> Rows { get; init; } = Array.Empty<ShortcutRow>();
        }

        readonly IReadOnlyList<CommandDefinition> commands;
        readonly KeyModifiers cmdKey;
        readonly IReadOnlyList<ShortcutRow> allRows;
        readonly HashSet<string> conflictingGestures;

        /// <summary>搜索词（匹配 显示名 / 手势 / Id，大小写不敏感）。</summary>
        [Reactive] public string Search { get; set; } = string.Empty;

        /// <summary>过滤后的分组列表（空分组不显示）。</summary>
        public ObservableCollection<ShortcutGroup> Groups { get; } = new();

        /// <summary>冲突摘要（无冲突时走 <c>shortcut.conflict.none</c>）。</summary>
        public string ConflictSummary =>
            conflicts.Count == 0
                ? ThemeManager.GetString("shortcut.conflict.none")
                : string.Format(ThemeManager.GetString("shortcut.conflict.some"),
                    conflicts.Count, string.Join(" / ", conflicts.Select(g => g.Key)));

        public bool HasConflicts => conflicts.Count > 0;
        public bool IsEmpty => Groups.Count == 0;

        readonly IReadOnlyList<IGrouping<string, CommandDefinition>> conflicts;

        /// <param name="commands">命令集合；默认取注册表（测试可传入合成集合以验证冲突标记）。</param>
        /// <param name="cmdKey">主修饰键（macOS = Meta），用于把手势渲染成当前平台写法。</param>
        public ShortcutOverviewViewModel(
            IReadOnlyList<CommandDefinition>? commands = null,
            KeyModifiers cmdKey = CommandRegistry.PrimaryModifier) {
            this.commands = commands ?? CommandRegistry.All;
            this.cmdKey = cmdKey;
            conflicts = CommandRegistry.Conflicts(this.commands, cmdKey);
            conflictingGestures = conflicts.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);

            allRows = this.commands.Select(c => {
                var gesture = CommandRegistry.Resolve(c.Gesture, cmdKey);
                string gestureText = gesture == null ? string.Empty : CommandRegistry.GestureText(gesture);
                return new ShortcutRow {
                    Id = c.Id,
                    Name = ThemeManager.GetString(c.NameKey),
                    Gesture = gestureText,
                    Scope = ThemeManager.GetString(c.Scope == CommandScope.Overlay
                        ? "shortcut.scope.overlay"
                        : "shortcut.scope.window"),
                    Note = c.Note ?? string.Empty,
                    Group = c.Group,
                    // 无手势的命令不会冲突；有手势的看该手势是否被多个命令占用
                    IsConflicting = gestureText.Length > 0 && conflictingGestures.Contains(gestureText),
                };
            }).ToList();

            this.WhenAnyValue(x => x.Search).Subscribe(_ => Rebuild());
            Rebuild();
        }

        void Rebuild() {
            string q = (Search ?? string.Empty).Trim();
            Groups.Clear();
            foreach (CommandGroup group in Enum.GetValues<CommandGroup>()) {
                var rows = allRows
                    .Where(r => r.Group == group)
                    .Where(r => q.Length == 0
                        || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || r.Gesture.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || r.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (rows.Count == 0) {
                    continue;
                }
                Groups.Add(new ShortcutGroup {
                    Title = ThemeManager.GetString(GroupKey(group)),
                    Rows = rows,
                });
            }
            this.RaisePropertyChanged(nameof(IsEmpty));
        }

        internal static string GroupKey(CommandGroup group) => group switch {
            CommandGroup.File => "command.group.file",
            CommandGroup.Edit => "command.group.edit",
            CommandGroup.Project => "command.group.project",
            CommandGroup.Tools => "command.group.tools",
            CommandGroup.View => "command.group.view",
            _ => "command.group.help",
        };
    }
}
