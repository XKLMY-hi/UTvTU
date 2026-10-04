using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Input;
using OpenUtau.Core;
using OpenUtau.Core.Theming;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels {
    /// <summary>链行的来源类别（内置伪插件 / VST 槽）。</summary>
    public enum FxChainItemKind {
        /// <summary>内置伪插件（EQ / 压缩 / 混响）。</summary>
        BuiltIn,
        /// <summary>VST / VST3 槽位。</summary>
        Vst,
    }

    /// <summary>
    /// 内置模块的**唯一定义处**（冻结契约 §1.1 第 4 条）：
    /// 名称键、格式徽标、稳定 id、强调色、读写器全在这一张表里。
    /// W1（混音台本体）与 W4（素材库「效果器」页签）复用本表，不得另行定义。
    /// 模块枚举用 Core 的 <see cref="MixFxModule"/>（命令层同一类型，避免第二套枚举）。
    /// </summary>
    public sealed class FxBuiltInDescriptor {
        /// <summary>模块（顺序即 DSP 链顺序：EQ → 压缩 → 混响）。</summary>
        public MixFxModule Module { get; init; }
        /// <summary>稳定 id（拖拽负载用）：eq / comp / rev。</summary>
        public string Id { get; init; } = string.Empty;
        /// <summary>显示名键（既有键，混音台与机架共用）。</summary>
        public string NameKey { get; init; } = string.Empty;
        /// <summary>行内格式徽标短代码（技术标识，不翻译）。</summary>
        public string BadgeCode { get; init; } = string.Empty;
        /// <summary>行强调色角色（颜色只取色池）。</summary>
        public Md3Role Accent { get; init; } = Md3Role.Primary;
        /// <summary>当前预设 id（行副标题展示）。</summary>
        public Func<UMixFx, string> GetPreset { get; init; } = _ => string.Empty;
        /// <summary>读模块电源（映射在 Core 的 <c>TrackMixCommands.IsModuleEnabled</c>，此处不复制一份）。</summary>
        public bool GetEnabled(UMixFx? fx) => TrackMixCommands.IsModuleEnabled(fx, Module);
    }

    /// <summary>链的**静态描述表**：内置三件套 + VST 格式徽标映射（W1/W4 共用）。</summary>
    public static class FxChainCatalog {
        /// <summary>内置三件套（固定序，与 DSP 链一致）。</summary>
        public static readonly IReadOnlyList<FxBuiltInDescriptor> BuiltIns = new[] {
            new FxBuiltInDescriptor {
                Module = MixFxModule.Eq, Id = "eq", NameKey = "mixfx.eq",
                BadgeCode = "EQ", Accent = Md3Role.Primary,
                GetPreset = fx => fx.EqPreset,
            },
            new FxBuiltInDescriptor {
                Module = MixFxModule.Compressor, Id = "comp", NameKey = "mixfx.compressor",
                BadgeCode = "COMP", Accent = Md3Role.Tertiary,
                GetPreset = fx => fx.CompPreset,
            },
            new FxBuiltInDescriptor {
                Module = MixFxModule.Reverb, Id = "rev", NameKey = "mixfx.reverb",
                BadgeCode = "REV", Accent = Md3Role.Secondary,
                GetPreset = fx => fx.ReverbPreset,
            },
        };

        /// <summary>内置模块的统一徽标键（B4：格式徽标 = 「内置」）。</summary>
        public const string BuiltInBadgeKey = "fxchain.badge.builtin";

        /// <summary>按稳定 id 找内置模块（素材库拖入按 id 反查）。</summary>
        public static FxBuiltInDescriptor? ById(string? id) =>
            BuiltIns.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>按枚举找描述（行投影与「＋」菜单共用）。</summary>
        public static FxBuiltInDescriptor Descriptor(MixFxModule module) =>
            BuiltIns.First(d => d.Module == module);

        /// <summary>
        /// VST 槽的格式徽标（VST3 / VST2 / VST3i …）。与迁移前 <c>TrackEffectRack</c> 口径一致：
        /// 未加载或注册表查不到时退回 "VST"。
        /// </summary>
        public static string VstBadge(VstPluginSlot slot) {
            string display = slot.PluginTypeDisplay;
            return string.IsNullOrEmpty(display) ? "VST" : display;
        }
    }

    /// <summary>
    /// 效果链面板的拖入数据格式（**W4 素材库「效果器」页签按同一常量发起拖拽**）：
    /// 负载为纯字符串 —— VST 插件 UID，或 <c>builtin:&lt;id&gt;</c>。
    /// 拖拽源三行：
    /// <code>
    /// var data = FxChainDragData.CreateDataTransfer(entry.Uid);
    /// await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
    /// </code>
    /// </summary>
    public static class FxChainDragData {
        /// <summary>拖拽数据格式名（Avalonia 12 的进程内强类型格式，与 Singer/Audio 拖拽同款）。</summary>
        public const string FormatName = "OpenUtau.FxChainItem";

        /// <summary>强类型格式（拖拽源与拖入端共用的**唯一**格式对象）。</summary>
        public static readonly DataFormat<string> Format = DataFormat.CreateInProcessFormat<string>(FormatName);

        /// <summary>内置模块负载前缀。</summary>
        public const string BuiltInPrefix = "builtin:";

        /// <summary>构造内置模块的拖拽负载。</summary>
        public static string BuiltInPayload(MixFxModule module) =>
            BuiltInPrefix + FxChainCatalog.Descriptor(module).Id;

        /// <summary>构造 VST 插件的拖拽负载。</summary>
        public static string VstPayload(string pluginUid) => pluginUid ?? string.Empty;

        /// <summary>打包成拖拽数据（拖拽源用）。</summary>
        public static DataTransfer CreateDataTransfer(string payload) {
            var data = new DataTransfer();
            data.Add(DataTransferItem.Create(Format, payload));
            return data;
        }

        /// <summary>从拖拽事件取出负载（拖入端用；取不到返回 null）。</summary>
        public static string? Read(DragEventArgs? e) {
            string? payload = e?.DataTransfer?.TryGetValue(Format);
            return string.IsNullOrWhiteSpace(payload) ? null : payload;
        }

        /// <summary>负载是否为内置模块；是则给出描述。</summary>
        public static FxBuiltInDescriptor? BuiltInOf(string? payload) {
            if (payload == null || !payload.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
            return FxChainCatalog.ById(payload.Substring(BuiltInPrefix.Length));
        }
    }

    /// <summary>
    /// 编辑器派发端（B5）：VST → 原生窗口；内置 → <c>Views.MixFxDialog</c>（非模态 MD3 弹层）；
    /// 插件浏览 → 既有选择对话框。抽成接口是为了让面板在 headless 下用 Fake 断言"双击派发给了谁"。
    /// </summary>
    public interface IFxChainEditorLauncher {
        /// <summary>打开内置效果编辑器（整轨三面板；EQ/压缩/混响共用同一个窗口）。</summary>
        void OpenBuiltInEditor(UTrack track, MixFxModule module);
        /// <summary>打开 VST 插件的原生编辑器（B5；打不开时由实现方回退到诊断窗）。</summary>
        void OpenVstEditor(UTrack track, int slotIndex);
        /// <summary>浏览并加载插件到指定槽位（内部走 <c>TrackMixCommands.SetVstPlugin</c>）。</summary>
        void BrowsePlugin(UTrack track, int slotIndex);
    }

    /// <summary>
    /// 同步委托命令（<see cref="ICommand"/>）。
    ///
    /// 这里**刻意不用** <c>ReactiveCommand</c>：它把执行挂到 Rx 输出调度器上
    /// （headless 测试里表现为"调了 Execute 却什么都没发生"），且把执行期异常转进
    /// <c>ThrownExceptions</c> 吞掉——两者都会把真实缺陷伪装成"命令无效"。
    /// 链行按钮只需要"立刻执行 + 异常可传播"。
    /// </summary>
    internal sealed class DelegateCommand : System.Windows.Input.ICommand {
        readonly Action execute;
        readonly Func<bool>? canExecute;

        public DelegateCommand(Action execute, Func<bool>? canExecute = null) {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler? CanExecuteChanged {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;

        public void Execute(object? parameter) => execute();
    }

    /// <summary>
    /// 链上的一行（内置伪插件与 VST 槽**同一等级**的投影，B2/B4）。
    /// 行只读模型 + 发命令；不持有窗口，也不直接改 <c>MixerViewModel</c>。
    /// </summary>
    public sealed class FxChainRowViewModel : ViewModelBase {
        readonly FxChainViewModel owner;

        internal FxChainRowViewModel(FxChainViewModel owner, FxBuiltInDescriptor? builtIn, int slotIndex) {
            this.owner = owner;
            BuiltIn = builtIn;
            Kind = builtIn != null ? FxChainItemKind.BuiltIn : FxChainItemKind.Vst;
            SlotIndex = slotIndex;
            TogglePowerCommand = new DelegateCommand(() => owner.SetPower(this, !IsPowered));
            RemoveCommand = new DelegateCommand(() => owner.RemoveRow(this));
            MoveUpCommand = new DelegateCommand(() => owner.MoveRow(this, -1));
            MoveDownCommand = new DelegateCommand(() => owner.MoveRow(this, +1));
        }

        /// <summary>行来源类别。</summary>
        public FxChainItemKind Kind { get; }
        /// <summary>内置描述（VST 行为 null）。</summary>
        public FxBuiltInDescriptor? BuiltIn { get; }
        /// <summary>内置模块（VST 行为 null）。</summary>
        public MixFxModule? Module => BuiltIn?.Module;
        /// <summary>VST 槽下标（内置行为 -1）。</summary>
        public int SlotIndex { get; }

        /// <summary>链上序号（1-based，与 DSP 顺序一致）。</summary>
        [Reactive] public int Order { get; set; }
        /// <summary>显示名键（内置行；VST 行为空串）。</summary>
        [Reactive] public string NameKey { get; set; } = string.Empty;
        /// <summary>显示名数据（VST 行；内置行为空串）。</summary>
        [Reactive] public string Name { get; set; } = string.Empty;
        /// <summary>副标题（内置 = 预设 id；VST = 厂商）。</summary>
        [Reactive] public string Detail { get; set; } = string.Empty;
        /// <summary>格式徽标文字（VST3 / VST2 …；内置行走字符串键）。</summary>
        [Reactive] public string BadgeText { get; set; } = string.Empty;
        /// <summary>徽标是否为"内置"（走字符串键；VST 徽标是技术代码，不翻译）。</summary>
        [Reactive] public bool BadgeIsBuiltIn { get; set; }
        /// <summary>开关状态：通过声 = true（内置 = 模块电源；VST = 未旁通）。</summary>
        [Reactive] public bool IsPowered { get; set; }
        /// <summary>不过声（旁通 / 模块关 / 链路总电源关）⇒ 名称转 on-surface-variant + 状态角标。</summary>
        [Reactive] public bool IsMuted { get; set; }
        /// <summary>可重排（仅 VST 段内；内置段是固定 DSP 顺序 ⇒ 把手为禁用态）。</summary>
        [Reactive] public bool CanReorder { get; set; }

        /// <summary>可移除（VST 槽；内置模块恒在链上，无删除语义 —— 见报告）。</summary>
        public bool CanRemove => Kind == FxChainItemKind.Vst;
        /// <summary>行强调色角色（画布只取色池）。</summary>
        public Md3Role Accent => BuiltIn?.Accent ?? Md3Role.Primary;

        /// <summary>切换电源 / 旁通（走 <c>DocManager</c> 命令，可撤销）。</summary>
        public ICommand TogglePowerCommand { get; }
        /// <summary>移除该行（仅 VST；走既有 <c>RemoveVstSlot</c> 命令）。</summary>
        public ICommand RemoveCommand { get; }
        /// <summary>上移（VST 段内，走命令）。</summary>
        public ICommand MoveUpCommand { get; }
        /// <summary>下移（VST 段内，走命令）。</summary>
        public ICommand MoveDownCommand { get; }

        /// <summary>双击 / Enter：请求打开该行的编辑器。</summary>
        public event EventHandler<FxChainRowViewModel>? Activated;

        /// <summary>开关落点（由行内开关触发；模型同步引起的回环由值比较挡掉）。</summary>
        public void RequestPower(bool powered) => owner.SetPower(this, powered);

        /// <summary>把手拖拽落点（<paramref name="steps"/> = 行高倍数，正数向下；仅 VST 段内生效）。</summary>
        public void RequestMove(int steps) => owner.MoveRow(this, steps);

        /// <summary>双击 / Enter 的落点。</summary>
        public void Activate() => Activated?.Invoke(this, this);

        /// <summary>调试/测试用的一行摘要。</summary>
        public override string ToString() =>
            $"[{Order}] {Kind} {BuiltIn?.Id ?? "vst"} slot={SlotIndex} powered={IsPowered} muted={IsMuted} badge={BadgeText}";
    }

    /// <summary>
    /// 效果链面板的 ViewModel（B2/B3/B4/B8）：
    /// · 数据源 = 当前选中轨道的**真实模型**（<c>UTrack.MixFx</c> + <c>UTrack.VstSlots</c>）；
    /// · 行序 = 内置三件套（固定 DSP 序）→ VST 槽（按 SlotIndex），与渲染链一致
    ///   （`WaveMix → Fader → MixFxSource → EffectChain → LevelTracker`）；
    /// · 一切模型变更走 <c>DocManager.ExecuteCmd</c>（可撤销）；本类**不写** <c>MixerViewModel</c>。
    /// </summary>
    public sealed class FxChainViewModel : ViewModelBase, ICmdSubscriber, IDisposable {
        UTrack? track;
        IDisposable? messageSub;
        bool subscribed;

        /// <summary>链行（按 DSP 顺序）。</summary>
        public ObservableCollection<FxChainRowViewModel> Rows { get; } = new();

        /// <summary>表头文字 = 轨道名（无背景，B3）。</summary>
        [Reactive] public string HeaderText { get; set; } = string.Empty;
        /// <summary>是否已绑定轨道。</summary>
        [Reactive] public bool HasTrack { get; set; }
        /// <summary>链为空。</summary>
        [Reactive] public bool IsEmpty { get; set; } = true;
        /// <summary>显示空态提示（有轨道 + 链为空）。</summary>
        [Reactive] public bool ShowEmptyHint { get; set; }
        /// <summary>链路总电源关闭（内置段整体不过声；VST 段不受影响）。</summary>
        [Reactive] public bool IsBuiltInMuted { get; set; }

        /// <summary>双击 / 「＋」的派发端（由面板注入；headless 测试注入 Fake）。</summary>
        public IFxChainEditorLauncher? Launcher { get; set; }

        /// <summary>请求打开某行的编辑器（面板订阅它）。</summary>
        public event EventHandler<FxChainRowViewModel>? EditorRequested;
        /// <summary>请求浏览插件（面板调 <see cref="Launcher"/>）。</summary>
        public event EventHandler<(UTrack track, int slotIndex)>? PluginBrowseRequested;

        public FxChainViewModel() {
            Subscribe();
        }

        /// <summary>当前轨道（只读；换轨请用 <see cref="Attach"/>）。</summary>
        public UTrack? Track => track;

        /// <summary>
        /// 订阅模型通知。挂载/卸载随宿主生命周期（视图切换时面板会被 reparent）——
        /// 重复调用无副作用。
        /// </summary>
        public void Subscribe() {
            if (subscribed) {
                return;
            }
            subscribed = true;
            DocManager.Inst.AddSubscriber(this);
            // 内置模块参数在 MixFxDialog / 轨道头里直接改模型（不进 undo 队列），
            // 靠这条通知让链行实时跟上（否则开关与旁通状态停留在旧值）。
            messageSub = MessageBus.Current.Listen<MixFxChangedNotification>()
                .Subscribe(n => {
                    if (track != null && n.trackNo == track.TrackNo) {
                        Rebuild();
                    }
                });
        }

        /// <summary>退订（面板离开视觉树时调用）。</summary>
        public void Unsubscribe() {
            if (!subscribed) {
                return;
            }
            subscribed = false;
            DocManager.Inst.RemoveSubscriber(this);
            messageSub?.Dispose();
            messageSub = null;
        }

        public void Dispose() => Unsubscribe();

        /// <summary>
        /// 绑定当前轨道（冻结契约 §1.1 第 2 条：宿主把 <c>MixerViewModel.SelectedTrack</c> 送进来）。
        /// 传 null = 未选中轨道 ⇒ 空态。
        /// </summary>
        public void Attach(UTrack? newTrack) {
            if (ReferenceEquals(track, newTrack)) {
                Rebuild();
                return;
            }
            track = newTrack;
            Rebuild();
        }

        /// <summary>
        /// 从模型重投影链行（真实数据，不猜）。内置段存在的判据与渲染管线一致：
        /// <c>MixFx == null</c> = 该轨未配置内置效果 ⇒ 不出内置行；<c>MixFx.Enabled == false</c>
        /// = 整段旁通（行仍在，但标注"不过声"）。**只读**：不写模型、不创建 UMixFx。
        /// </summary>
        public void Rebuild() {
            foreach (var row in Rows) {
                row.Activated -= OnRowActivated;
            }
            Rows.Clear();

            HasTrack = track != null;
            HeaderText = track?.TrackName ?? string.Empty;

            var fx = track?.MixFx;
            IsBuiltInMuted = fx != null && !fx.Enabled;

            int order = 0;
            if (fx != null) {
                foreach (var descriptor in FxChainCatalog.BuiltIns) {
                    var builtIn = descriptor;
                    var row = new FxChainRowViewModel(this, builtIn, -1) {
                        Order = ++order,
                        NameKey = builtIn.NameKey,
                        Detail = builtIn.GetPreset(fx) ?? string.Empty,
                        BadgeIsBuiltIn = true,
                        IsPowered = builtIn.GetEnabled(fx),
                    };
                    row.IsMuted = IsBuiltInMuted || !row.IsPowered;
                    Rows.Add(Wire(row));
                }
            }

            var slots = track?.VstSlots;
            if (slots != null) {
                foreach (var slot in slots.Where(s => s.IsLoaded).OrderBy(s => s.SlotIndex)) {
                    var row = new FxChainRowViewModel(this, null, slot.SlotIndex) {
                        Order = ++order,
                        Name = slot.DisplayName,
                        Detail = slot.PluginVendor,
                        BadgeText = FxChainCatalog.VstBadge(slot),
                        BadgeIsBuiltIn = false,
                        IsPowered = !slot.Bypassed,
                    };
                    row.IsMuted = slot.Bypassed;
                    Rows.Add(Wire(row));
                }
            }

            int vstRows = Rows.Count(r => r.Kind == FxChainItemKind.Vst);
            foreach (var row in Rows) {
                row.CanReorder = row.Kind == FxChainItemKind.Vst && vstRows > 1;
            }

            IsEmpty = Rows.Count == 0;
            ShowEmptyHint = HasTrack && IsEmpty;
        }

        FxChainRowViewModel Wire(FxChainRowViewModel row) {
            row.Activated += OnRowActivated;
            return row;
        }

        void OnRowActivated(object? sender, FxChainRowViewModel row) {
            // 双击 = 打开编辑器（B4/B5）；派发目标由 Launcher 决定，VM 不碰窗口。
            EditorRequested?.Invoke(this, row);
            if (Launcher == null || track == null) {
                return;
            }
            if (row.Kind == FxChainItemKind.BuiltIn && row.Module.HasValue) {
                Launcher.OpenBuiltInEditor(track, row.Module.Value);
            } else if (row.Kind == FxChainItemKind.Vst) {
                Launcher.OpenVstEditor(track, row.SlotIndex);
            }
        }

        // ══════════════════ 命令（全部可撤销） ══════════════════

        /// <summary>切换一行的电源 / 旁通（内置 = 模块电源；VST = 槽位旁通）。</summary>
        public void SetPower(FxChainRowViewModel row, bool powered) {
            if (track == null || row.IsPowered == powered) {
                return;
            }
            UCommand? cmd = null;
            if (row.Kind == FxChainItemKind.BuiltIn && row.Module.HasValue) {
                if (track.MixFx == null) {
                    return;   // 内置段不存在（Rebuild 不会产出这种行）
                }
                cmd = TrackMixCommands.SetMixFxModule(track, row.Module.Value, powered);
            } else if (row.Kind == FxChainItemKind.Vst) {
                // 既有命令：槽位旁通（数据级，下次渲染快照生效；不必重载实例）
                cmd = TrackMixCommands.ToggleVstBypass(track, row.SlotIndex);
            }
            if (cmd == null) {
                return;
            }
            Execute(cmd);
            Rebuild();
        }

        /// <summary>移除一行（仅 VST 槽；内置模块无删除语义 —— 见报告）。</summary>
        public void RemoveRow(FxChainRowViewModel row) {
            if (track == null || row.Kind != FxChainItemKind.Vst) {
                return;
            }
            Execute(TrackMixCommands.RemoveVstSlot(track, row.SlotIndex));
            Rebuild();
        }

        /// <summary>
        /// 重排（**仅 VST 段内**；内置三件套是固定 DSP 顺序，不参与）。
        /// 走 <c>TrackMixCommands.ReorderVstSlot</c>（载荷互换 + 重载两侧实例，对合 ⇒ 可撤销）。
        /// 多步移动包在一个 UndoGroup 里 ⇒ Ctrl+Z 一次回到原位。
        /// </summary>
        public void MoveRow(FxChainRowViewModel row, int delta) {
            if (track == null || delta == 0 || row.Kind != FxChainItemKind.Vst) {
                return;
            }
            if (track.VstSlots == null) {
                return;
            }
            var order = Rows.Where(r => r.Kind == FxChainItemKind.Vst)
                .Select(r => r.SlotIndex).OrderBy(i => i).ToList();
            int at = order.IndexOf(row.SlotIndex);
            if (at < 0) {
                return;
            }
            int step = Math.Sign(delta);
            int remaining = delta;
            int index = at;
            DocManager.Inst.StartUndoGroup(deferValidate: true);
            try {
                while (remaining != 0) {
                    int next = index + step;
                    if (next < 0 || next >= order.Count) {
                        break;   // 到 VST 段边界即停
                    }
                    Execute(TrackMixCommands.ReorderVstSlot(track, order[index], order[next]), ownGroup: false);
                    // 被移动的插件载荷现在落在 next 位置
                    index = next;
                    remaining -= step;
                }
            } finally {
                DocManager.Inst.EndUndoGroup();
            }
            Rebuild();
        }

        /// <summary>
        /// 「＋」：把内置模块加入链（B2 —— 内置与 VST 同一等级，可被"加入"）。
        /// <c>MixFx == null</c> 时创建之（撤销回到 null）；已存在则打开该模块电源。
        /// </summary>
        public void AddBuiltIn(MixFxModule module) {
            if (track == null) {
                return;
            }
            var before = track.MixFx;
            if (before == null) {
                // 先按描述表把三路开关摆好（只有被点的那个开），再把整条链挂上去；
                // 撤销 = 摘回 null（不留空 UMixFx）
                var created = new UMixFx { Enabled = true };
                foreach (var d in FxChainCatalog.BuiltIns) {
                    TrackMixCommands.SetModuleEnabled(created, d.Module, d.Module == module);
                }
                Execute(TrackMixCommands.SetMixFx(track, created));
                Rebuild();
                return;
            }
            if (FxChainCatalog.Descriptor(module).GetEnabled(before)) {
                return;   // 已在链上
            }
            Execute(TrackMixCommands.SetMixFxModule(track, module, true));
            Rebuild();
        }


        /// <summary>「＋」：浏览插件并加载到第一个空槽（无空槽则追加）。</summary>
        public void BrowseVst() {
            if (track == null) {
                return;
            }
            int slotIndex = FirstFreeSlotIndex(track);
            PluginBrowseRequested?.Invoke(this, (track, slotIndex));
            Launcher?.BrowsePlugin(track, slotIndex);
        }

        /// <summary>选好插件后写入槽位（走既有 <c>SetVstPlugin</c> 命令，可撤销）。</summary>
        public void LoadPlugin(int slotIndex, string pluginUid) {
            if (track == null || string.IsNullOrWhiteSpace(pluginUid)) {
                return;
            }
            Execute(TrackMixCommands.SetVstPlugin(track, slotIndex, pluginUid.Trim()));
            Rebuild();
        }

        /// <summary>第一个空槽下标（没有空槽 = 追加到末尾）。</summary>
        public static int FirstFreeSlotIndex(UTrack track) {
            var slots = track.VstSlots;
            if (slots == null) {
                return 0;
            }
            for (int i = 0; i < slots.Count; i++) {
                if (!slots[i].IsLoaded) {
                    return i;
                }
            }
            return slots.Count;
        }

        /// <summary>
        /// 素材库「效果器」拖入（B4 空态 / B6）：VST 插件落新槽；内置模块 = 加入链。
        /// 返回是否被接受（拖拽源据此决定取消与否）。
        /// </summary>
        public bool DropPayload(string? payload, int slotIndex = -1) {
            if (track == null || string.IsNullOrWhiteSpace(payload)) {
                return false;
            }
            var builtIn = FxChainDragData.BuiltInOf(payload);
            if (builtIn != null) {
                AddBuiltIn(builtIn.Module);
                return true;
            }
            int target = slotIndex >= 0 ? slotIndex : FirstFreeSlotIndex(track);
            LoadPlugin(target, payload);
            return true;
        }

        /// <summary>
        /// 在 UndoGroup 内执行命令（<c>DocManager.ExecuteCmd</c> 拒绝组外命令）。
        /// <paramref name="ownGroup"/> = false 时由调用方管理组（多步重排 = 一次撤销）。
        /// </summary>
        void Execute(UCommand cmd, bool ownGroup = true) {
            if (!ownGroup) {
                DocManager.Inst.ExecuteCmd(cmd);
                return;
            }
            DocManager.Inst.StartUndoGroup(deferValidate: true);
            try {
                DocManager.Inst.ExecuteCmd(cmd);
            } finally {
                DocManager.Inst.EndUndoGroup();
            }
        }

        // ══════════════════ 订阅 ══════════════════

        public void OnNext(UCommand cmd, bool isUndo) {
            // 1) VST 槽异步加载完成 → 行重建（名字/徽标在加载完成后才解析得出）
            if (cmd is VstSlotChangedNotification n) {
                if (track != null && n.TrackNo == track.TrackNo) {
                    Rebuild();
                }
                return;
            }
            // 2) 效果链相关命令被撤销 / 重做（含全局 Ctrl+Z）→ 重建，
            //    否则界面停留在旧状态（开关 / 名称 / 行序与实际不符）
            string desc = cmd.ToString() ?? string.Empty;
            if (desc.Contains("VST", StringComparison.Ordinal) || desc.Contains("MixFx", StringComparison.Ordinal)) {
                Rebuild();
            }
        }
    }
}
