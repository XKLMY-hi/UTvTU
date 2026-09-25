using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using OpenUtau.App.Controls;
using OpenUtau.App.ViewModels;
using OpenUtau.Core;
using OpenUtau.Core.SignalChain.Effects;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Vst;
using OpenUtau.Core.Util;
using ReactiveUI;

namespace OpenUtau.App.Views {
    public partial class TrackEffectRack : WindowEx, Core.ICmdSubscriber {
        private readonly UTrack track;
        private UMixFx fx => track.MixFx ??= new();

        private readonly List<Preferences.MixFxUserPreset> userPresets = new();
        private readonly Preferences.MixFxUserPreset defaultPreset;
        private bool _builtInExpanded;

        public TrackEffectRack() : this(new UTrack()) { }

        public TrackEffectRack(UTrack track) {
            InitializeComponent();
            this.track = track;
            _builtInExpanded = false;
            TitleLabel.Text = $"{track.TrackName}";

            // 订阅 VST 槽位变更（异步 Load 完成后重建行 UI）
            DocManager.Inst.AddSubscriber(this);
            Closed += (_, _) => DocManager.Inst.RemoveSubscriber(this);

            defaultPreset = new Preferences.MixFxUserPreset {
                Name = ThemeManager.GetString("mixfx.library.default"),
                Fx = RecommendedFx(),
            };
            userPresets.Add(defaultPreset);
            foreach (var p in Preferences.Default.MixFxUserPresets ?? new())
                userPresets.Add(p);
            RefreshPresetCombo();

            if (track.VstSlots == null || track.VstSlots.Count == 0)
                track.VstSlots = VstPluginManager.CreateDefaultSlots(3);

            EnableToggle.IsChecked = fx.Enabled;
            ExportCheck.IsChecked = Preferences.Default.MixFxApplyOnExportMixdown;

            RecBtn.Click += (_, _) => LoadPreset(defaultPreset);
            SaveBtn.Click += (_, _) => SavePreset();
            DelBtn.Click += (_, _) => DeletePreset();
            PresetCombo.SelectionChanged += (_, _) => {
                if (PresetCombo.SelectedItem is Preferences.MixFxUserPreset p) LoadPreset(p);
            };
            EnableToggle.Tapped += (_, _) => {
                fx.Enabled = !fx.Enabled;
                EnableToggle.IsChecked = fx.Enabled;
                NotifyChanged(); BuildUI();
            };

            BuildUI();
        }

        // ═══════════════════════════════════════════════════════════════
        //  Presets
        // ═══════════════════════════════════════════════════════════════

        private static UMixFx RecommendedFx() => new() {
            Enabled = true,
            EqPreset = "vocal_air", CompPreset = "gentle", ReverbPreset = "small_room",
            EqLowDb = 2, EqMidFreq = 3000, EqMidDb = 1.5, EqHighDb = 3,
            CompThresholdDb = -18, CompRatio = 2, CompMakeupDb = 2.5,
            ReverbSize = 0.3, ReverbDamp = 0.7, ReverbWet = 1, ReverbPreDelayMs = 0,
        };

        private void RefreshPresetCombo() {
            PresetCombo.ItemsSource = null;
            PresetCombo.ItemsSource = userPresets.ToList();
            PresetCombo.SelectedItem = defaultPreset;
        }

        private void LoadPreset(Preferences.MixFxUserPreset p) {
            if (p.Fx == null) return;
            var f = p.Fx;
            fx.Enabled = f.Enabled; fx.EqPreset = f.EqPreset; fx.CompPreset = f.CompPreset; fx.ReverbPreset = f.ReverbPreset;
            fx.EqBypassed = f.EqBypassed; fx.CompBypassed = f.CompBypassed; fx.ReverbBypassed = f.ReverbBypassed;
            fx.EqLowDb = f.EqLowDb; fx.EqMidFreq = f.EqMidFreq; fx.EqMidDb = f.EqMidDb; fx.EqHighDb = f.EqHighDb;
            fx.CompThresholdDb = f.CompThresholdDb; fx.CompRatio = f.CompRatio; fx.CompMakeupDb = f.CompMakeupDb;
            fx.ReverbSize = f.ReverbSize; fx.ReverbDamp = f.ReverbDamp; fx.ReverbWet = f.ReverbWet; fx.ReverbPreDelayMs = f.ReverbPreDelayMs;
            EnableToggle.IsChecked = fx.Enabled;
            NotifyChanged(); BuildUI();
        }

        private async void SavePreset() {
            var dlg = new TypeInDialog { Title = ThemeManager.GetString("mixfx.library.save") };
            dlg.SetText("");
            string? name = null;
            dlg.onFinish = n => { if (!string.IsNullOrWhiteSpace(n)) name = n; };
            await dlg.ShowDialog(this);
            if (string.IsNullOrWhiteSpace(name) || name == defaultPreset.Name) return;
            var snap = new Preferences.MixFxUserPreset { Name = name, Fx = Snap() };
            userPresets.RemoveAll(x => x.Name == name && x != defaultPreset);
            userPresets.Add(snap);
            SavePresets();
            RefreshPresetCombo();
            PresetCombo.SelectedItem = snap;
        }

        private void DeletePreset() {
            if (PresetCombo.SelectedItem is not Preferences.MixFxUserPreset p || p == defaultPreset) return;
            userPresets.Remove(p);
            SavePresets();
            RefreshPresetCombo();
            PresetCombo.SelectedItem = defaultPreset;
        }

        private void SavePresets() {
            Preferences.Default.MixFxUserPresets = userPresets.Where(x => x != defaultPreset).ToList();
            Preferences.Save();
        }

        private UMixFx Snap() => new() {
            Enabled = fx.Enabled,
            EqPreset = fx.EqPreset, CompPreset = fx.CompPreset, ReverbPreset = fx.ReverbPreset,
            EqBypassed = fx.EqBypassed, CompBypassed = fx.CompBypassed, ReverbBypassed = fx.ReverbBypassed,
            EqLowDb = fx.EqLowDb, EqMidFreq = fx.EqMidFreq, EqMidDb = fx.EqMidDb, EqHighDb = fx.EqHighDb,
            CompThresholdDb = fx.CompThresholdDb, CompRatio = fx.CompRatio, CompMakeupDb = fx.CompMakeupDb,
            ReverbSize = fx.ReverbSize, ReverbDamp = fx.ReverbDamp, ReverbWet = fx.ReverbWet, ReverbPreDelayMs = fx.ReverbPreDelayMs,
        };

        // ═══════════════════════════════════════════════════════════════
        //  UI Builder
        // ═══════════════════════════════════════════════════════════════

        private void BuildUI() {
            SlotList.Children.Clear();

            // ── Built-in FX section (collapsible) ─────────────────────
            int activeCount = 0;
            if (!fx.EqBypassed) activeCount++;
            if (!fx.CompBypassed) activeCount++;
            if (!fx.ReverbBypassed) activeCount++;

            // Section header with expand chevron
            var sectionHeader = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto") };
            var chevron = new Path {
                Data = this.FindResource(_builtInExpanded ? "icon-chevron-down" : "icon-chevron-right") as StreamGeometry,
                Stroke = this.FindResource("SystemControlForegroundBaseHighBrush") as IBrush,
                StrokeThickness = 1.5, Width = 12, Height = 12,
                Stretch = Stretch.Uniform, Opacity = 0.5,
                VerticalAlignment = VerticalAlignment.Center, Margin = new(0, 0, 6, 0),
            };
            var label = new TextBlock {
                Text = string.Format(ThemeManager.GetString("effects.builtin.active"), activeCount),
                FontSize = 11, FontWeight = FontWeight.SemiBold, Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
            };
            sectionHeader.Children.Add(chevron);
            sectionHeader.Children.Add(label); Grid.SetColumn(label, 1);

            // Quick toggles for each built-in
            var qPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            foreach (var (name, bp, toggle) in new[] {
                ("EQ", fx.EqBypassed, (Action<bool>)(v => { fx.EqBypassed = v; NotifyChanged(); })),
                ("Comp", fx.CompBypassed, v => { fx.CompBypassed = v; NotifyChanged(); }),
                ("Reverb", fx.ReverbBypassed, v => { fx.ReverbBypassed = v; NotifyChanged(); }),
            }) {
                bool on = !bp;
                var pill = new Border {
                    Classes = { on ? "fxPillOn" : "fxPillOff" },
                    CornerRadius = new(8), Padding = new(6, 1), Margin = new(1, 0),
                    Background = on
                        ? ThemeManager.AccentBrush1
                        : ThemeManager.NeutralAccentBrushSemi,
                    Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = name, FontSize = 9,
                        Foreground = on ? Brushes.White : Brushes.Gray,
                        FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal,
                    },
                };
                var capName = name; var capBp = bp; var capToggle = toggle;
                pill.PointerPressed += (_, _) => {
                    capToggle(!capBp);
                    BuildUI();
                };
                qPanel.Children.Add(pill);
            }
            sectionHeader.Children.Add(qPanel); Grid.SetColumn(qPanel, 2);

            // Expand chevron button
            var expBtn = new Button {
                Content = _builtInExpanded ? ThemeManager.GetString("effects.collapse") : ThemeManager.GetString("effects.expand"),
                FontSize = 9, Padding = new(6, 1), Margin = new(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            expBtn.Click += (_, _) => { _builtInExpanded = !_builtInExpanded; BuildUI(); };
            sectionHeader.Children.Add(expBtn); Grid.SetColumn(expBtn, 3);

            var section = new Border { Classes = { "slotRow" }, Child = sectionHeader,
                Margin = new(0, 0, 0, 4) };
            SlotList.Children.Add(section);

            // Expanded built-in detail
            if (_builtInExpanded) {
                BuildBuiltInRow("EQ", fx.EqBypassed,
                    v => { fx.EqBypassed = v; NotifyChanged(); BuildUI(); },
                    () => fx.EqPreset, v => { fx.EqPreset = v; LoadEqPreset(v); NotifyChanged(); },
                    FxPresets.EqPresetNames,
                    new[] { Param("Low", -12, 12, fx.EqLowDb, v => fx.EqLowDb = v, "F1"),
                            Param("Mid Freq", 200, 6000, fx.EqMidFreq, v => fx.EqMidFreq = v, "F0"),
                            Param("Mid", -12, 12, fx.EqMidDb, v => fx.EqMidDb = v, "F1"),
                            Param("High", -12, 12, fx.EqHighDb, v => fx.EqHighDb = v, "F1") });

                BuildBuiltInRow("Compressor", fx.CompBypassed,
                    v => { fx.CompBypassed = v; NotifyChanged(); BuildUI(); },
                    () => fx.CompPreset, v => { fx.CompPreset = v; LoadCompPreset(v); NotifyChanged(); },
                    FxPresets.CompPresetNames,
                    new[] { Param("Thresh", -40, 0, fx.CompThresholdDb, v => fx.CompThresholdDb = v, "F1"),
                            Param("Ratio", 1, 20, fx.CompRatio, v => fx.CompRatio = v, "F1"),
                            Param("Makeup", 0, 12, fx.CompMakeupDb, v => fx.CompMakeupDb = v, "F1") });

                BuildBuiltInRow("Reverb", fx.ReverbBypassed,
                    v => { fx.ReverbBypassed = v; NotifyChanged(); BuildUI(); },
                    () => fx.ReverbPreset, v => { fx.ReverbPreset = v; LoadReverbPreset(v); NotifyChanged(); },
                    FxPresets.ReverbPresetNames,
                    new[] { Param("Size", 0, 1, fx.ReverbSize, v => fx.ReverbSize = v, "F2"),
                            Param("Damp", 0, 1, fx.ReverbDamp, v => fx.ReverbDamp = v, "F2"),
                            Param("Wet", 0, 2, fx.ReverbWet, v => fx.ReverbWet = v, "F2"),
                            Param("Pre-Delay", 0, 200, fx.ReverbPreDelayMs, v => fx.ReverbPreDelayMs = v, "F0") });
            }

            // ── VST section ──────────────────────────────────────────
            var vstHeader = new Grid { ColumnDefinitions = new("*,Auto") };
            vstHeader.Children.Add(new TextBlock {
                Text = ThemeManager.GetString("effects.vstplugins"), FontSize = 11, FontWeight = FontWeight.SemiBold,
                Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center,
            });
            SlotList.Children.Add(new Border { Classes = { "slotRow" }, Child = vstHeader,
                Margin = new(0, 8, 0, 4) });

            foreach (var slot in track.VstSlots)
                BuildVstRow(slot);

            if (track.VstSlots.Count < 8) {
                var add = new Button { Classes = { "addBtn" }, Content = ThemeManager.GetString("effects.addvstslot"),
                    Margin = new(0, 4, 0, 0) };
                // 走命令：可撤销（ExecuteCmd 拒绝 UndoGroup 外命令）
                add.Click += (_, _) => {
                    ExecuteVstCommand(TrackMixCommands.AddVstSlot(track, track.VstSlots.Count, ""));
                    BuildUI();
                };
                SlotList.Children.Add(add);
            }
        }

        // ── Built-in FX row (only shown when expanded) ──────────────

        private void BuildBuiltInRow(string name, bool bypassed,
            Action<bool> setBypassed, Func<string> getPreset, Action<string> setPreset,
            IReadOnlyList<string> presets, Control[] params_) {

            var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            headerRow.Children.Add(new TextBlock {
                Text = name, FontSize = 11, FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            });
            var toggle = new ToggleSwitch { IsChecked = !bypassed, OnContent = ThemeManager.GetString("effects.on"), OffContent = ThemeManager.GetString("effects.off"), FontSize = 10 };
            toggle.Tapped += (_, _) => {
                setBypassed(!bypassed);
                toggle.IsChecked = !bypassed;
                NotifyChanged(); BuildUI();
            };
            headerRow.Children.Add(toggle); Grid.SetColumn(toggle, 1);

            var combo = new ComboBox { ItemsSource = presets, SelectedItem = getPreset(),
                Margin = new(0, 2, 0, 4), IsEnabled = !bypassed };
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s) setPreset(s); };

            var paramStack = new StackPanel { Spacing = 1 };
            foreach (var c in params_)
                paramStack.Children.Add(c);

            var outer = new StackPanel();
            outer.Children.Add(headerRow);
            outer.Children.Add(combo);
            outer.Children.Add(paramStack);

            var row = new Border { Classes = { "slotRow" }, Child = outer, Margin = new(4, 0, 0, 3),
                BorderBrush = ThemeManager.NeutralAccentBrush };
            if (bypassed) row.Classes.Add("bypassed");
            SlotList.Children.Add(row);
        }

        private static Grid Param(string label, double min, double max, double val,
            Action<double> onChange, string fmt) {
            var g = new Grid { Margin = new(0, 1),
                ColumnDefinitions = new ColumnDefinitions("60,*,40") };
            g.Children.Add(new TextBlock { Text = label, FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 });
            var s = new Slider { Classes = { "param" }, Minimum = min, Maximum = max, Value = val };
            var vl = new TextBlock { Text = val.ToString(fmt), FontSize = 9, FontFamily = ThemeManager.MonoFontFamily,
                VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Opacity = 0.6 };
            s.PropertyChanged += (_, e) => {
                if (e.Property == RangeBase.ValueProperty) { onChange(s.Value); vl.Text = s.Value.ToString(fmt); }
            };
            g.Children.Add(s); Grid.SetColumn(s, 1);
            g.Children.Add(vl); Grid.SetColumn(vl, 2);
            return g;
        }

        // ── VST row ─────────────────────────────────────────────────

        private void BuildVstRow(VstPluginSlot slot) {
            var row = new Border { Classes = { "slotRow" } };
            var g = new Grid { ColumnDefinitions = new("Auto,*,Auto,Auto,Auto") };

            var leftStack = new StackPanel { Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center };
            leftStack.Children.Add(new TextBlock {
                Text = $"{slot.SlotIndex + 1}", FontSize = 11, Opacity = 0.35,
                VerticalAlignment = VerticalAlignment.Center, FontFamily = ThemeManager.MonoFontFamily,
                Margin = new(0, 0, 6, 0),
            });

            if (slot.IsLoaded) {
                // Type badge
                var entry = slot.Entry;
                string badge = entry?.Type == VstPluginType.VST3
                    ? (entry.IsEffect ? "VST3" : "VST3i")
                    : (entry?.IsEffect == true ? "VST2" : "VST2i");
                var badgeColor = entry is { IsEffect: false }
                    ? new SolidColorBrush(Color.FromRgb(220, 120, 50))
                    : new SolidColorBrush(Color.FromRgb(60, 150, 100));
                leftStack.Children.Add(new Border {
                    Background = badgeColor, CornerRadius = new(3),
                    Padding = new(5, 1), Margin = new(0, 0, 6, 0),
                    Child = new TextBlock { Text = badge, FontSize = 9,
                        Foreground = Brushes.White, FontWeight = FontWeight.SemiBold },
                });

                var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                nameStack.Children.Add(new TextBlock {
                    Classes = { "slotName" }, Text = slot.DisplayName,
                });
                if (!string.IsNullOrEmpty(slot.PluginVendor))
                    nameStack.Children.Add(new TextBlock {
                        Text = slot.PluginVendor, FontSize = 9, Opacity = 0.45,
                    });
                leftStack.Children.Add(nameStack);
            } else {
                leftStack.Children.Add(new TextBlock {
                    Classes = { "empty" }, Text = ThemeManager.GetString("effects.emptyslot"),
                });
            }
            g.Children.Add(leftStack);
            Grid.SetColumn(leftStack, 1);

            if (slot.IsLoaded) {
                var bt = new ToggleSwitch {
                    IsChecked = !slot.Bypassed, OnContent = ThemeManager.GetString("effects.on"), OffContent = ThemeManager.GetString("effects.off"),
                    FontSize = 10, Margin = new(6, 0, 2, 0),
                };
                bt.Tapped += (_, _) => {
                    // 走命令：可撤销（数据级，无需重载实例）
                    ExecuteVstCommand(TrackMixCommands.ToggleVstBypass(track, slot.SlotIndex));
                    bt.IsChecked = !slot.Bypassed;
                    BuildUI();
                };
                g.Children.Add(bt); Grid.SetColumn(bt, 2);

                var edit = new Button { Classes = { "browseBtn" }, Margin = new(2, 0, 2, 0),
                    Content = new TextBlock { Text = ThemeManager.GetString("effects.edit"), FontSize = 9 },
                };
                var s2 = slot; edit.Click += async (_, _) => await OpenVstEditor(s2);
                g.Children.Add(edit); Grid.SetColumn(edit, 3);

                var rm = new Button { Classes = { "removeBtn" } };
                // 走命令：可撤销（undo 恢复 UID + StateData 重载还原参数）
                rm.Click += (_, _) => {
                    ExecuteVstCommand(TrackMixCommands.RemoveVstSlot(track, slot.SlotIndex));
                    BuildUI();
                };
                g.Children.Add(rm); Grid.SetColumn(rm, 4);
            } else {
                var browse = new Button { Classes = { "browseBtn" },
                    HorizontalAlignment = HorizontalAlignment.Right };
                var s = slot; browse.Click += (_, _) => BrowsePlugin(s);
                g.Children.Add(browse); Grid.SetColumn(browse, 4);
            }

            row.Child = g;
            if (slot.IsLoaded && slot.Bypassed) row.Classes.Add("bypassed");
            SlotList.Children.Add(row);
        }

        // ── Plugin Browser ──────────────────────────────────────────

        private void BrowsePlugin(VstPluginSlot slot) {
            VstPluginRegistry.Inst.ScanAll();
            var allPlugins = VstPluginRegistry.Inst.Effects;
            var instruments = VstPluginRegistry.Inst.All.Where(p => !p.IsEffect).ToList();

            if (allPlugins.Count == 0) {
                string msgText = ThemeManager.GetString("effects.noplugins");
                if (instruments.Count > 0)
                    msgText += $"\n\n{instruments.Count} {ThemeManager.GetString("effects.instruments.excluded")}";
                msgText += $"\n\n{ThemeManager.GetString("effects.addscanpaths")}";
                ShowMessage(msgText);
                return;
            }

            var picker = new WindowEx() { Title = ThemeManager.GetString("effects.selecteffect"), Width = 520, Height = 420,
                WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var layout = new StackPanel { Margin = new(12) };

            // Info
            layout.Children.Add(new TextBlock {
                Text = $"{allPlugins.Count} {ThemeManager.GetString("effects.available")}" +
                       (instruments.Count > 0 ? $" ({instruments.Count} {ThemeManager.GetString("effects.instruments.filtered")})" : ""),
                FontSize = 11, Margin = new(0, 0, 0, 6), Opacity = 0.55,
            });

            var search = new TextBox { PlaceholderText = ThemeManager.GetString("effects.filter"), FontSize = 11, Margin = new(0, 0, 0, 6) };
            layout.Children.Add(search);

            var lb = new ListBox { ItemsSource = allPlugins.ToList(), Height = 280 };
            layout.Children.Add(lb);

            search.TextChanged += (_, _) => {
                var f = search.Text?.ToLowerInvariant() ?? "";
                lb.ItemsSource = string.IsNullOrEmpty(f)
                    ? allPlugins
                    : allPlugins.Where(p => p.Name.ToLowerInvariant().Contains(f)
                        || p.Vendor.ToLowerInvariant().Contains(f)).ToList();
            };

            var btns = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 8, 0, 0), Spacing = 8 };
            var load = new Button { Content = ThemeManager.GetString("effects.load"), Width = 64 };
            load.Click += (_, _) => {
                if (lb.SelectedItem is VstPluginEntry e) {
                    // 选插件对话框留在命令外；写 UID + 异步加载进命令（可撤销，
                    // 完成后 VstSlotChangedNotification 触发行 UI 重建）
                    ExecuteVstCommand(TrackMixCommands.SetVstPlugin(track, slot.SlotIndex, e.Uid));
                    BuildUI();
                }
                picker.Close();
            };
            var cancel = new Button { Content = ThemeManager.GetString("effects.cancel"), Width = 64 };
            cancel.Click += (_, _) => picker.Close();
            btns.Children.Add(load); btns.Children.Add(cancel);
            layout.Children.Add(btns);

            lb.DoubleTapped += (_, _) => {
                if (lb.SelectedItem is VstPluginEntry e) {
                    ExecuteVstCommand(TrackMixCommands.SetVstPlugin(track, slot.SlotIndex, e.Uid));
                    BuildUI();
                }
                picker.Close();
            };

            // 窗口装饰由系统提供（WindowEx 已回归原生窗口），无需手动标题栏
            var dp = new DockPanel { LastChildFill = true };
            dp.Children.Add(new Border { Child = layout, Margin = new(0) });
            picker.Content = dp;
            picker.ShowDialog(this);
        }

        private void ShowMessage(string text) {
            var w = new WindowEx() { Title = ThemeManager.GetString("effects.info"), Width = 380, Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var sp = new StackPanel { Margin = new(14) };
            sp.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
            var ok = new Button { Content = ThemeManager.GetString("effects.ok"), Width = 60,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 8, 0, 0) };
            ok.Click += (_, _) => w.Close();
            sp.Children.Add(ok);
            w.Content = new Border { Child = sp };
            w.ShowDialog(this);
        }

        private async Task OpenVstEditor(VstPluginSlot slot) {
            if (!slot.IsLoaded) return;
            if (slot.Entry == null) return;

            // Get shared instance — do NOT create new one
            var fx = VstPluginManager.Inst.GetEffect(track.TrackNo, slot.SlotIndex);
            if (fx == null) {
                // Not yet loaded — load it now (async，原生调用移出 UI 线程)
                fx = await VstPluginManager.Inst.LoadEffectAsync(track.TrackNo, slot);
            }
            if (fx == null) {
                ShowMessage($"{ThemeManager.GetString("effects.error.load")}\n{VstBridge.LastError() ?? ThemeManager.GetString("effects.error.unknown")}");
                return;
            }

            var editor = new VstEditorWindow(fx);
            editor.Show();
        }

        /// <summary>
        /// 在 UndoGroup 内执行 VST 命令——DocManager.ExecuteCmd 拒绝组外命令
        /// （"No active UndoGroup"）。deferValidate：VST 槽位数据与音符无关，
        /// 组结束时补一次 ValidateFull。
        /// </summary>
        private void ExecuteVstCommand(Core.UCommand cmd) {
            DocManager.Inst.StartUndoGroup(deferValidate: true);
            try {
                DocManager.Inst.ExecuteCmd(cmd);
            } finally {
                DocManager.Inst.EndUndoGroup();
            }
        }

        // ── ICmdSubscriber ────────────────────────────────────────

        public void OnNext(Core.UCommand cmd, bool isUndo) {
            // 1) VST 槽位异步加载完成（通知）→ 重建行 UI（非 UI 线程自动回投）
            if (cmd is VstSlotChangedNotification n && n.TrackNo == track.TrackNo) {
                BuildUI();
                return;
            }
            // 2) VST 槽位命令被撤销/重做（Publish 原命令，isUndo=true）→ 重建行 UI，
            //    否则界面停留旧状态（开关/卡片与实际不符）
            if (isUndo && cmd.ToString().Contains("VST")) {
                BuildUI();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  Preset loaders
        // ═══════════════════════════════════════════════════════════════

        void LoadEqPreset(string k) {
            if (FxPresets.Eq.TryGetValue(k, out var p)) { fx.EqLowDb = p.LowDb; fx.EqMidFreq = p.MidFreq; fx.EqMidDb = p.MidDb; fx.EqHighDb = p.HighDb; }
        }
        void LoadCompPreset(string k) {
            if (FxPresets.Comp.TryGetValue(k, out var p)) { fx.CompThresholdDb = p.ThresholdDb; fx.CompRatio = p.Ratio; fx.CompMakeupDb = p.MakeupDb; }
        }
        void LoadReverbPreset(string k) {
            if (FxPresets.Reverb.TryGetValue(k, out var p)) { fx.ReverbSize = p.RoomSize; fx.ReverbDamp = p.Damp; fx.ReverbWet = 1.0; fx.ReverbPreDelayMs = p.PreDelayMs; }
        }

        void NotifyChanged() {
            // MixFx 滑杆直接改模型（不进 undo 队列）——标记工程已修改（防退出丢改动）
            DocManager.Inst.MarkProjectModified();
            MessageBus.Current.SendMessage(new MixFxChangedNotification(track.TrackNo));
        }

        protected override void OnClosed(EventArgs e) {
            base.OnClosed(e);
            Preferences.Default.MixFxApplyOnExportMixdown = ExportCheck.IsChecked == true;
            Preferences.Save();
        }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.Key == Key.Escape) Close(); }
    }
}
