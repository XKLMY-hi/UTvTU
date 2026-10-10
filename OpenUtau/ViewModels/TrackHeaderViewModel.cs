using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using OpenUtau.Api;
using OpenUtau.App.Editing;
using OpenUtau.App.Views;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class TrackHeaderViewModel : ViewModelBase, IDisposable, IActivatableViewModel, ICmdSubscriber {
        public int TrackNo => track.TrackNo + 1;
        public USinger Singer => track.Singer;
        public Phonemizer Phonemizer => track.Phonemizer;
        public string PhonemizerTag => track.Phonemizer.Tag;
        public Core.Render.IRenderer Renderer => track.RendererSettings.Renderer;
        public IReadOnlyList<MenuItemViewModel>? SingerMenuItems { get; set; }
        public ReactiveCommand<USinger, Unit> SelectSingerCommand { get; }
        public IReadOnlyList<MenuItemViewModel>? PhonemizerMenuItems { get; set; }
        public ReactiveCommand<PhonemizerFactory, Unit> SelectPhonemizerCommand { get; }
        public IReadOnlyList<MenuItemViewModel>? RenderersMenuItems { get; set; }
        public ReactiveCommand<string, Unit> SelectRendererCommand { get; }
        [Reactive] public string TrackName { get; set; } = string.Empty;
        [Reactive] public SolidColorBrush TrackAccentColor { get; set; } = ThemeManager.GetTrackColor("Blue").AccentColor;
        [Reactive] public TrackColor TrackColor { get; set; } = ThemeManager.GetTrackColor("Blue");
        [Reactive] public double Volume { get; set; }
        [Reactive] public double Pan { get; set; }
        [Reactive] public bool Mute { get; set; }
        [Reactive] public bool Muted { get; set; }
        [Reactive] public bool Solo { get; set; }
        [Reactive] public bool IsSelected { get; set; }
        [Reactive] public Bitmap? Avatar { get; set; }
        [Reactive] public bool IsSingerVisible { get; set; }
        [Reactive] public bool IsPhonemizerVisible { get; set; }
        [Reactive] public bool IsRendererVisible { get; set; }
        /// <summary>W33 紧凑档：渲染器内联在名字行右侧（不占独立行），由 code-behind 按轨道高设置。</summary>
        [Reactive] public bool IsRendererInline { get; set; }
        /// <summary>
        /// 次行的 `⋯` 溢出按钮是否显示（W33）：歌手/音素器被按高裁掉时，仍能从那里进它们的菜单。
        /// **渲染器不再进溢出菜单** —— 它恒可见，避免同一入口两处（用户报的"渲染器被挤掉"）。
        /// </summary>
        public bool ShowOverflow => !IsSingerVisible || !IsPhonemizerVisible;
        [Reactive] public bool MixFxEnabled { get; set; }
        [Reactive] public IBrush HeaderBorderBrush { get; set; } = ThemeManager.NeutralAccentBrushSemi;

        // W33：MessageBus 订阅句柄（此前"发出即丢"，VM 永不退订 ⇒ 跨用例/跨视图串扰）
        private IDisposable? volumeSub;
        private IDisposable? panSub;
        private bool disposed;
        readonly CompositeDisposable subscriptions = new();

        public ViewModelActivator Activator { get; }

        private readonly UTrack track;
        private readonly TrackMixEditSession? mixEdits;
        private bool _syncing;

        public void BeginMixGesture() => mixEdits?.BeginGesture();
        public void EndMixGesture() => mixEdits?.EndGesture();

        // Parameterless constructor for Avalonia preview only.
        public TrackHeaderViewModel() {
            SelectSingerCommand = ReactiveCommand.Create<USinger>(_ => { });
            SelectPhonemizerCommand = ReactiveCommand.Create<PhonemizerFactory>(_ => { });
            SelectRendererCommand = ReactiveCommand.Create<string>(_ => { });
            Activator = new ViewModelActivator();
            track = new UTrack(DocManager.Inst.Project);
            this.WhenAnyValue(x => x.IsSelected)
                .Subscribe(_ => RefreshSelectionStyle()).DisposeWith(subscriptions);
            RefreshSelectionStyle();
        }

        public TrackHeaderViewModel(UTrack track) {
            this.track = track;
            mixEdits = new TrackMixEditSession(track);
            DocManager.Inst.AddSubscriber(this);
            SelectSingerCommand = ReactiveCommand.Create<USinger>(singer => {
                if (track.Singer != singer) {
                    DocManager.Inst.StartUndoGroup("command.track.singer");
                    ApplySingerToTrack(track, singer);
                    DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(track.TrackNo, true));
                    DocManager.Inst.EndUndoGroup();
                    UpdateRecentSingers(singer);
                    Preferences.Save();
                    MessageBus.Current.SendMessage(new PianorollRefreshEvent("Part"));
                }
                MessageBus.Current.SendMessage(new TracksRefreshEvent());
                this.RaisePropertyChanged(nameof(Singer));
                this.RaisePropertyChanged(nameof(Renderer));
                RefreshAvatar();
            });
            SelectPhonemizerCommand = ReactiveCommand.Create<PhonemizerFactory>(factory => {
                if (track.Phonemizer.GetType() != factory.type) {
                    DocManager.Inst.StartUndoGroup("command.track.setting");
                    var phonemizer = factory.Create();
                    Log.Information($"Loading Phonemizer: {phonemizer.ToString()}");
                    DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, track, phonemizer));
                    DocManager.Inst.EndUndoGroup();
                    var name = phonemizer.GetType().FullName!;
                    if (!string.IsNullOrEmpty(Singer?.Id) && phonemizer != null) {
                        Preferences.Default.SingerPhonemizers[Singer.Id] = name;
                    }
                    Preferences.Default.RecentPhonemizers.Remove(name);
                    Preferences.Default.RecentPhonemizers.Insert(0, name);
                    while (Preferences.Default.RecentPhonemizers.Count > 8) {
                        Preferences.Default.RecentPhonemizers.RemoveRange(
                            8, Preferences.Default.RecentPhonemizers.Count - 8);
                    }
                    Preferences.Save();
                }
                this.RaisePropertyChanged(nameof(Phonemizer));
                this.RaisePropertyChanged(nameof(PhonemizerTag));
            });
            SelectRendererCommand = ReactiveCommand.Create<string>(name => {
                var settings = new URenderSettings {
                    renderer = name,
                };
                DocManager.Inst.StartUndoGroup("command.track.setting");
                DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, track, settings));
                DocManager.Inst.EndUndoGroup();
                this.RaisePropertyChanged(nameof(Renderer));
            });

            Activator = new ViewModelActivator();

            TrackName = track.TrackName;
            TrackAccentColor = ThemeManager.GetTrackColor(track.TrackColor).AccentColor;
            TrackColor = Preferences.Default.UseTrackColor
                ? ThemeManager.GetTrackColor(track.TrackColor)
                : ThemeManager.GetTrackColor("Blue");
            Volume = track.Volume;
            Pan = track.Pan * 100.0;
            Mute = track.Mute;
            Muted = track.Muted;
            Solo = track.Solo;
            MixFxEnabled = track.MixFx?.Enabled ?? false;
            this.WhenAnyValue(x => x.Volume)
                .Skip(1)
                .Subscribe(volume => {
                    if (_syncing) return;
                    if (!mixEdits.SetVolume(volume) && track.Volume != volume) {
                        _syncing = true;
                        try { Volume = track.Volume; } finally { _syncing = false; }
                    }
                }).DisposeWith(subscriptions);
            this.WhenAnyValue(x => x.Pan)
                .Skip(1)
                .Subscribe(pan => {
                    if (_syncing) return;
                    if (!mixEdits.SetPan(pan) && track.Pan * 100 != pan) {
                        _syncing = true;
                        try { Pan = track.Pan * 100; } finally { _syncing = false; }
                    }
                }).DisposeWith(subscriptions);

            // Listen for volume/pan changes from mixer window
            // W33：这两条订阅必须留住句柄并在 Dispose 里释放 —— 此前它们**永不退订**，
            // 于是"已无主的轨头 VM"会一直留在 MessageBus 上：之后任何人发
            // Volume/PanChangeNotification 都会回调到它（headless 下跨用例串扰，
            // MixerTrackStripTest 的"只发一次声像通知"就是被这种泄漏踩红的）。
            volumeSub = MessageBus.Current.Listen<VolumeChangeNotification>()
                .Where(n => n.TrackNo == track.TrackNo)
                .Subscribe(n => {
                    _syncing = true;
                    // 静音哨兵（-24，Solo/Mute 静音时广播）不写回 Volume——
                    // 否则真实音量被污染为 -24，取消 Solo/Mute 后无法恢复（"取消独奏不恢复"根因）
                    if (!(n.Volume <= -24 && track.Muted)) {
                        Volume = n.Volume;
                    }
                    _syncing = false;
                });
            panSub = MessageBus.Current.Listen<PanChangeNotification>()
                .Where(n => n.TrackNo == track.TrackNo)
                .Subscribe(n => {
                    _syncing = true;
                    Pan = n.Pan;
                    _syncing = false;
                });
            this.WhenAnyValue(x => x.Mute)
                .Skip(1)
                .Subscribe(mute => {
                    if (!_syncing) mixEdits.SetMute(mute);
                }).DisposeWith(subscriptions);
            this.WhenAnyValue(x => x.Solo)
                .Skip(1)
                .Subscribe(solo => {
                    if (!_syncing) mixEdits.SetSolo(solo);
                }).DisposeWith(subscriptions);
            // W33：次行 `⋯` 的显隐由"歌手/音素器是否被按高裁掉"派生 ⇒ 两者变了要通知它
            this.WhenAnyValue(x => x.IsSingerVisible, x => x.IsPhonemizerVisible)
                .Subscribe(_ => this.RaisePropertyChanged(nameof(ShowOverflow))).DisposeWith(subscriptions);
            this.WhenAnyValue(x => x.MixFxEnabled)
                .Subscribe(enabled => {
                    if (_syncing || (track.MixFx?.Enabled ?? false) == enabled) return;
                    var fx = track.MixFx?.Clone() ?? new UMixFx();
                    fx.Enabled = enabled;
                    if (mixEdits.SetEffects(fx)) MessageBus.Current.SendMessage(new MixFxChangedNotification(track.TrackNo));
                }).DisposeWith(subscriptions);
            this.WhenAnyValue(x => x.IsSelected)
                .Subscribe(_ => RefreshSelectionStyle()).DisposeWith(subscriptions);

            RefreshAvatar();
            RefreshSelectionStyle();
        }

        public void RefreshSelectionStyle() {
            HeaderBorderBrush = IsSelected
                ? TrackAccentColor
                : ThemeManager.NeutralAccentBrushSemi;
        }

        public void ToggleSolo() {
            mixEdits?.SetSolo(!track.Solo);
        }

        public void SoloAdditionally() {
            mixEdits?.SetSolo(!track.Solo, additionally: true);
        }

        public void UnsoloAll() {
            mixEdits?.UnsoloAll();
        }

        public void ToggleMute() {
            mixEdits?.SetMute(!track.Mute);
        }

        public void ToggleMute(bool mute) {
            mixEdits?.SetMute(mute);
        }

        public void MuteOnly() {
            mixEdits?.SetMutes(t => ReferenceEquals(t, track));
        }

        public void MuteAllOthers() {
            mixEdits?.SetMutes(t => !ReferenceEquals(t, track));
        }

        public void UnmuteAll() {
            mixEdits?.SetMutes(_ => false);
        }

        public void JudgeMuted() {
            Muted = track.Muted;
        }

        private void ApplySingerToTrack(UTrack targetTrack, USinger? singer) {
            if (singer is USinger selectedSinger) {
                Log.Information($"Loading Singer: {selectedSinger.Name}");
                DocManager.Inst.ExecuteCmd(new TrackChangeSingerCommand(DocManager.Inst.Project, targetTrack, selectedSinger));
                if (!string.IsNullOrEmpty(selectedSinger.Id) &&
                    Preferences.Default.SingerPhonemizers.TryGetValue(selectedSinger.Id, out var phonemizerName) &&
                    TryChangePhonemizer(targetTrack, phonemizerName)) {
                } else if (!string.IsNullOrEmpty(selectedSinger.DefaultPhonemizer)) {
                    TryChangePhonemizer(targetTrack, selectedSinger.DefaultPhonemizer);
                }
                if (!selectedSinger.Found || selectedSinger.SingerType != targetTrack.RendererSettings.Renderer?.SingerType) {
                    var settings = new URenderSettings();
                    if (selectedSinger.Found) {
                        settings = new URenderSettings {
                            renderer = Core.Render.Renderers.GetDefaultRenderer(selectedSinger.SingerType),
                        };
                    }
                    DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, targetTrack, settings));
                }
            } else {
                DocManager.Inst.ExecuteCmd(new TrackChangeSingerCommand(DocManager.Inst.Project, targetTrack, USinger.CreateMissing(string.Empty)));
                var settings = new URenderSettings();
                DocManager.Inst.ExecuteCmd(new TrackChangeRenderSettingCommand(DocManager.Inst.Project, targetTrack, settings));
            }
        }

        private void UpdateRecentSingers(USinger? singer) {
            if (!string.IsNullOrEmpty(singer?.Id) && singer.Found) {
                Preferences.Default.RecentSingers.Remove(singer.Id);
                Preferences.Default.RecentSingers.Insert(0, singer.Id);
                if (Preferences.Default.RecentSingers.Count > 16) {
                    Preferences.Default.RecentSingers.RemoveRange(
                        16, Preferences.Default.RecentSingers.Count - 16);
                }
            }
        }

        private SingerMenuItemViewModel CreateSingerMenuItem(USinger singer) {
            return new SingerMenuItemViewModel() {
                Header = singer.LocalizedName,
                Command = SelectSingerCommand,
                CommandParameter = singer,
            };
        }

        private bool TryChangePhonemizer(UTrack targetTrack, string phonemizerName) {
            try {
                var factory = PhonemizerFactory.Get(phonemizerName);
                var phonemizer = factory?.Create();
                if (phonemizer != null) {
                    DocManager.Inst.ExecuteCmd(new TrackChangePhonemizerCommand(DocManager.Inst.Project, targetTrack, phonemizer));
                    return true;
                }
            } catch (Exception e) {
                Log.Error(e, $"Failed to load phonemizer {phonemizerName}");
            }
            return false;
        }

        public void RefreshSingers() {
            var items = new List<MenuItemViewModel>();
            if (SingerManager.Inst.Singers.Count > 0) {
                items.AddRange(Preferences.Default.RecentSingers
                .Select(id => SingerManager.Inst.Singers.Values.FirstOrDefault(singer => singer.Id == id))
                .OfType<USinger>()
                .Select(CreateSingerMenuItem));
                items.Add(new MenuItemViewModel() {
                    Header = ThemeManager.GetString("tracks.favorite") + " ...",
                    Items = Preferences.Default.FavoriteSingers
                        .Select(id => SingerManager.Inst.Singers.Values.FirstOrDefault(singer => singer.Id == id))
                        .OfType<USinger>()
                        .LocalizedOrderBy(singer => singer.LocalizedName)
                        .Select(CreateSingerMenuItem).ToArray(),
                });
                var keys = SingerManager.Inst.SingerGroups.Keys.OrderBy(k => k);
                foreach (var key in keys) {
                    items.Add(new MenuItemViewModel() {
                        Header = $"{key} ...",
                        Items = SingerManager.Inst.SingerGroups[key]
                            .Select(CreateSingerMenuItem).ToArray(),
                    });
                }
            } else {
                items.Add(new MenuItemViewModel() {
                    Header = ThemeManager.GetString("tracks.nosinger"),
                    IsEnabled = false
                });
            }

            items.Add(new MenuItemViewModel() { // Separator
                Header = "-",
                Height = 1
            });
            items.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("tracks.installsinger"),
                Command = ReactiveCommand.Create(async () => {
                    var mainWindow = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
                        ?.MainWindow as MainWindow;
                    if (mainWindow == null) {
                        return;
                    }
                    var file = await FilePicker.OpenFileAboutSinger(
                        mainWindow, "menu.tools.singer.install", FilePicker.ArchiveFiles);
                    if (file == null) {
                        return;
                    }
                    try {
                        if (file.EndsWith(Core.Vogen.VogenSingerInstaller.FileExt)) {
                            Core.Vogen.VogenSingerInstaller.Install(file);
                            return;
                        }
                        if (file.EndsWith(PackageManager.OudepExt)) {
                            await PackageManager.Inst.InstallFromFileAsync(file);
                            return;
                        }

                        var setup = new SingerSetupDialog() {
                            DataContext = new SingerSetupViewModel() {
                                ArchiveFilePath = file,
                            },
                        };
                        _ = setup.ShowDialog(mainWindow);
                        if (setup.Position.Y < 0) {
                            setup.Position = setup.Position.WithY(0);
                        }
                    } catch (Exception e) {
                        Log.Error(e, $"Failed to install singer {file}");
                        _ = await MessageBox.ShowError(mainWindow, new MessageCustomizableException($"Failed to install singer {file}", $"<translate:errors.failed.installsinger>: {file}", e));
                    }
                })
            });
            items.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("tracks.opensingers"),
                Command = ReactiveCommand.Create(() => {
                    try {
                        OS.OpenFolder(PathManager.Inst.SingersPath);
                    } catch (Exception e) {
                        DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                    }
                })
            });
            if (!string.IsNullOrWhiteSpace(PathManager.Inst.AdditionalSingersPath) && Directory.Exists(PathManager.Inst.AdditionalSingersPath)) {
                items.Add(new MenuItemViewModel() {
                    Header = ThemeManager.GetString("tracks.openaddsingers"),
                    Command = ReactiveCommand.Create(() => {
                        try {
                            OS.OpenFolder(PathManager.Inst.AdditionalSingersPath);
                        } catch (Exception e) {
                            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                        }
                    })
                });
            }
            items.Add(new MenuItemViewModel() {
                Header = ThemeManager.GetString("singers.refresh"),
                Command = ReactiveCommand.Create(() => {
                    DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(MainWindow), true, "singer"));
                    SingerManager.Inst.SearchAllSingers();
                    DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
                    DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(MainWindow), false, "singer"));
                })
            });

            SingerMenuItems = items;
            this.RaisePropertyChanged(nameof(SingerMenuItems));
        }

        public string GetPhonemizerGroupHeader(string key) {
            if (key is null) {
                return "General";
            }
            if (ThemeManager.TryGetString($"languages.{key.ToLowerInvariant()}", out var value)) {
                return $"{key}: {value}";
            }
            return key;
        }

        PhonemizerFactory? FindPhonemizerByName(string name) {
            return PhonemizerFactory.Get(name);
        }

        public void RefreshPhonemizers() {
            var items = new List<MenuItemViewModel>();
            //Singer default
            if (track != null && track.Singer != null && track.Singer.Found) {
                var factory = FindPhonemizerByName(track.Singer.DefaultPhonemizer);
                if (factory != null) {
                    items.Add(new MenuItemViewModel() {
                        Header = ThemeManager.GetString("tracks.singerdefault") + factory.ToString(),
                        Command = SelectPhonemizerCommand,
                        CommandParameter = factory,
                    });
                }
            }
            //Recently used phonemizers
            items.AddRange(Preferences.Default.RecentPhonemizers
                .Select(name => FindPhonemizerByName(name))
                .OfType<PhonemizerFactory>()
                .OrderBy(factory => factory.tag)
                .Select(factory => new MenuItemViewModel() {
                    Header = factory.ToString(),
                    Command = SelectPhonemizerCommand,
                    CommandParameter = factory,
                }));
            //more phonemizers grouped by singing language
            items.Add(new MenuItemViewModel() {
                Header = $"{ThemeManager.GetString("tracks.more")} ...",
                Items = PhonemizerFactory.GetAll().GroupBy(factory => factory.language)
                .OrderBy(group => group.Key)
                .Select(group => new MenuItemViewModel() {
                    Header = GetPhonemizerGroupHeader(group.Key),
                    Items = group.Select(factory => new MenuItemViewModel() {
                        Header = factory.ToString(),
                        Command = SelectPhonemizerCommand,
                        CommandParameter = factory,
                    }).ToArray(),
                }).ToArray()
            });
            PhonemizerMenuItems = items.ToArray();
            this.RaisePropertyChanged(nameof(PhonemizerMenuItems));
        }

        public void RefreshRenderers() {
            var items = new List<MenuItemViewModel>();
            if (track != null && track.Singer != null && track.Singer.Found) {
                items.AddRange(Core.Render.Renderers.GetSupportedRenderers(track.Singer.SingerType)
                    .Select(name => new MenuItemViewModel() {
                        Header = name,
                        Command = SelectRendererCommand,
                        CommandParameter = name,
                    }));
            }
            RenderersMenuItems = items.ToArray();
            this.RaisePropertyChanged(nameof(RenderersMenuItems));
        }

        public void RefreshAvatar() {
            var singer = track?.Singer;
            if (singer == null || singer.AvatarData == null) {
                Avatar = new RenderTargetBitmap(new PixelSize(1, 1));
                return;
            }
            try {
                using var stream = new MemoryStream(singer.AvatarData);
                Avatar = new Bitmap(stream).CreateScaledBitmap(new PixelSize(100, 100));
            } catch (Exception e) {
                Avatar = null;
                Log.Error(e, "Failed to decode avatar.");
            }
        }

        public void ManuallyRaise() {
            SyncMixState();
            _syncing = true;
            TrackName = track.TrackName;
            TrackAccentColor = ThemeManager.GetTrackColor(track.TrackColor).AccentColor;
            TrackColor = Preferences.Default.UseTrackColor
                ? ThemeManager.GetTrackColor(track.TrackColor)
                : ThemeManager.GetTrackColor("Blue");
            RefreshSelectionStyle();
            this.RaisePropertyChanged(nameof(Singer));
            this.RaisePropertyChanged(nameof(TrackNo));
            this.RaisePropertyChanged(nameof(TrackName));
            this.RaisePropertyChanged(nameof(TrackAccentColor));
            this.RaisePropertyChanged(nameof(TrackColor));
            this.RaisePropertyChanged(nameof(Phonemizer));
            this.RaisePropertyChanged(nameof(PhonemizerTag));
            this.RaisePropertyChanged(nameof(Renderer));
            this.RaisePropertyChanged(nameof(Mute));
            this.RaisePropertyChanged(nameof(Muted));
            this.RaisePropertyChanged(nameof(Solo));
            MixFxEnabled = track.MixFx?.Enabled ?? false;
            this.RaisePropertyChanged(nameof(MixFxEnabled));
            this.RaisePropertyChanged(nameof(Volume));
            this.RaisePropertyChanged(nameof(Pan));
            _syncing = false;
            RefreshAvatar();
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (!disposed && mixEdits?.IsCurrent == true &&
                cmd is TrackMixChangedNotification mix && mix.Tracks.Contains(track)) {
                SyncMixState();
            }
        }

        void SyncMixState() {
            _syncing = true;
            try {
                Volume = track.Volume;
                Pan = track.Pan * 100;
                Mute = track.Mute;
                Muted = track.Muted;
                Solo = track.Solo;
                MixFxEnabled = track.MixFx?.Enabled ?? false;
            } finally {
                _syncing = false;
            }
        }

        public void Remove() {
            DocManager.Inst.StartUndoGroup("command.track.delete");
            DocManager.Inst.ExecuteCmd(new RemoveTrackCommand(DocManager.Inst.Project, track));
            DocManager.Inst.EndUndoGroup();
        }

        public void MoveUp() {
            if (track == DocManager.Inst.Project.tracks.First()) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.track.order");
            DocManager.Inst.ExecuteCmd(new MoveTrackCommand(DocManager.Inst.Project, track, true));
            DocManager.Inst.EndUndoGroup();
        }

        public void MoveDown() {
            if (track == DocManager.Inst.Project.tracks.Last()) {
                return;
            }
            DocManager.Inst.StartUndoGroup("command.track.order");
            DocManager.Inst.ExecuteCmd(new MoveTrackCommand(DocManager.Inst.Project, track, false));
            DocManager.Inst.EndUndoGroup();
        }

        public void Rename() {
            var dialog = new TypeInDialog();
            dialog.Title = ThemeManager.GetString("tracks.rename");
            dialog.SetText(track.TrackName);
            dialog.onFinish = name => {
                if (!string.IsNullOrWhiteSpace(name) && name != track.TrackName) {
                    DocManager.Inst.StartUndoGroup("command.track.setting");
                    this.TrackName = name;
                    DocManager.Inst.ExecuteCmd(new RenameTrackCommand(DocManager.Inst.Project, track, name));
                    DocManager.Inst.EndUndoGroup();
                }
            };
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null) {
                dialog.ShowDialog(desktop.MainWindow);
            }
        }

        public async void SelectTrackColor() {
            var dialog = new TrackColorDialog();
            dialog.DataContext = new TrackColorViewModel(track);

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null) {
                await dialog.ShowDialog(desktop.MainWindow);
                TrackAccentColor = ThemeManager.GetTrackColor(track.TrackColor).AccentColor;
                TrackColor = Preferences.Default.UseTrackColor
                ? ThemeManager.GetTrackColor(track.TrackColor)
                : ThemeManager.GetTrackColor("Blue");
                RefreshSelectionStyle();
            }
        }

        public void Duplicate() {
            DocManager.Inst.StartUndoGroup("command.track.duplicate");
            var newTrack = new UTrack(track.TrackName + "_copy") {
                TrackNo = track.TrackNo + 1,
                Singer = track.Singer,
                Phonemizer = track.Phonemizer,
                RendererSettings = track.RendererSettings,
                Mute = track.Mute,
                Muted = track.Muted,
                Solo = false,
                Volume = track.Volume,
                Pan = track.Pan,
                TrackColor = track.TrackColor,
                TrackExpressions = track.TrackExpressions.Select(exp => exp.Clone()).ToList()
            };
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(DocManager.Inst.Project, newTrack));
            var parts = DocManager.Inst.Project.parts
                .Where(part => part.trackNo == track.TrackNo)
                .Select(part => part.Clone()).ToList();
            foreach (var part in parts) {
                part.trackNo = newTrack.TrackNo;
                DocManager.Inst.ExecuteCmd(new AddPartCommand(DocManager.Inst.Project, part));
            }
            DocManager.Inst.EndUndoGroup();
        }

        public void DuplicateSettings() {
            DocManager.Inst.StartUndoGroup("command.track.duplicate");
            DocManager.Inst.ExecuteCmd(new AddTrackCommand(DocManager.Inst.Project, new UTrack(track.TrackName + "_copy") {
                TrackNo = track.TrackNo + 1,
                Singer = track.Singer,
                Phonemizer = track.Phonemizer,
                RendererSettings = track.RendererSettings,
                Mute = track.Mute,
                Muted = track.Muted,
                Solo = false,
                Volume = track.Volume,
                Pan = track.Pan,
                TrackColor = track.TrackColor,
                TrackExpressions = track.TrackExpressions.Select(exp => exp.Clone()).ToList()
            }));
            DocManager.Inst.EndUndoGroup();
        }

        public void VoiceColorRemapping() {
            if (track.Singer != null && track.Singer.Found && track.VoiceColorExp != null) {
                DocManager.Inst.ExecuteCmd(new VoiceColorRemappingNotification(track.TrackNo, false));
            }
        }

        public void OpenMixFxDialog() {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow != null) {
                // 轨道头 fx 按钮 = 这条轨的实时效果机架（三面板，非模态、每轨单窗）。
                // 效果链的**管理入口**已收归混音台右侧链面板（FxChainPanel，决策 B3）：
                // 内置三件套与 VST 槽在那里同级呈现、双击开各自编辑器。
                MixFxDialog.Open(desktop.MainWindow, track);
            }
        }
        /// <summary>释放 MessageBus 订阅（W33）。轨头被移除/重建时必须调用，否则监听者越积越多。</summary>
        public void Dispose() {
            if (disposed) {
                return;
            }
            disposed = true;
            DocManager.Inst.RemoveSubscriber(this);
            mixEdits?.Dispose();
            subscriptions.Dispose();
            SelectSingerCommand.Dispose();
            SelectPhonemizerCommand.Dispose();
            SelectRendererCommand.Dispose();
            volumeSub?.Dispose();
            volumeSub = null;
            panSub?.Dispose();
            panSub = null;
        }
    }
}
