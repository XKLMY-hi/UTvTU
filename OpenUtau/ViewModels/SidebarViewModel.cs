using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtau.App.ViewModels {

    /// <summary>侧栏歌手卡片：歌手 + 懒加载头像 + 引擎铭牌（仅 Classic/DiffSinger）。</summary>
    public class SingerItem : ReactiveObject, IDisposable {
        bool disposed;
        public USinger Singer { get; }
        public string Name => Singer.LocalizedName;
        public string SingerTypeText => Singer.SingerType switch {
            USingerType.Classic => "UTAU",
            USingerType.Enunu => "ENUNU",
            USingerType.Vogen => "Vogen",
            USingerType.DiffSinger => "DiffSinger",
            USingerType.Voicevox => "VOICEVOX",
            _ => Singer.SingerType.ToString(),
        };
        public string AvatarFallback => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

        private Bitmap? avatar;
        private bool avatarLoaded;
        private bool avatarLoading;
        /// <summary>加载中/无图时返回 null（显示首字符回退），头像就绪后通知刷新。</summary>
        public Bitmap? Avatar {
            get {
                if (!disposed && !avatarLoaded && !avatarLoading) {
                    avatarLoading = true;
                    LoadAvatarAsync();
                }
                return avatar;
            }
        }

        public bool ShowAvatarFallback => !avatarLoaded || avatar == null;

        public SingerItem(USinger singer) {
            Singer = singer;
        }

        /// <summary>
        /// 头像异步加载：歌手需 EnsureLoaded 才填充 AvatarData（轨道头选歌手后已加载，
        /// 侧栏直接读未加载实例为 null）——后台线程加载，解码回 UI 线程。
        /// </summary>
        private void LoadAvatarAsync() {
            Task.Run(() => {
                try {
                    Singer.EnsureLoaded();
                } catch (Exception e) {
                    Log.Error(e, $"Failed to load singer {Singer.Id}");
                }
                byte[] data = Singer.AvatarData;
                Dispatcher.UIThread.Post(() => {
                    if (disposed) return;
                    if (data != null) {
                        try {
                            using (var stream = new MemoryStream(data)) {
                                avatar = new Bitmap(stream);
                            }
                        } catch (Exception e) {
                            Log.Error(e, "Failed to load avatar.");
                        }
                    }
                    avatarLoaded = true;
                    this.RaisePropertyChanged(nameof(Avatar));
                    this.RaisePropertyChanged(nameof(ShowAvatarFallback));
                });
            });
        }

        public void Dispose() {
            disposed = true;
            avatar?.Dispose();
            avatar = null;
        }
    }

    /// <summary>侧栏伴奏卡片：仅文件名 + 路径（不读元数据，试听/添加时才读取）。</summary>
    public class SampleItem : ReactiveObject {
        public string Name { get; }
        public string Path { get; }
        public string Format => System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();
        [Reactive] public bool IsPreviewing { get; set; }

        public SampleItem(string path) {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
        }
    }

    /// <summary>
    /// 侧栏素材库 VM（阶段 E4）：歌手列表 + 伴奏库。
    /// - 歌手：SingerManager 扁平列表，订阅 SingersRefreshedNotification 自动刷新
    /// - 伴奏：枚举 SampleSearchPaths + SamplesPath 内音频格式文件（不读元数据）
    /// </summary>
    public class SidebarViewModel : ViewModelBase, ICmdSubscriber, IDisposable {
        bool disposed;
        static readonly string[] SampleExts = { ".wav", ".mp3", ".ogg", ".opus", ".flac" };

        public ObservableCollection<SingerItem> Singers { get; } = new ObservableCollection<SingerItem>();
        public ObservableCollection<SampleItem> Samples { get; } = new ObservableCollection<SampleItem>();
        [Reactive] public bool HasSingers { get; set; }
        [Reactive] public bool HasSamples { get; set; }

        public SidebarViewModel() {
            DocManager.Inst.AddSubscriber(this);
            // 试听状态变化 → 更新伴奏卡片播放状态（跨线程，回 UI 线程处理）
            PlaybackManager.Inst.PreviewChanged += OnPreviewChanged;
            RefreshSingers();
            RefreshSamples();
        }

        public void RefreshSingers() {
            if (disposed) return;
            var singers = SingerManager.Inst.SingerGroups.Values
                .SelectMany(list => list)
                .ToList();
            foreach (var item in Singers) item.Dispose();
            Singers.Clear();
            foreach (var singer in singers) {
                Singers.Add(new SingerItem(singer));
            }
            HasSingers = Singers.Count > 0;
        }

        /// <summary>重扫伴奏目录：仅枚举文件名（性能：不读时长/元数据）。</summary>
        public void RefreshSamples() {
            if (disposed) return;
            var files = new List<string>();
            foreach (var dir in PathManager.Inst.SamplesPaths) {
                try {
                    // A fresh installation can have no sample library yet.
                    if (!Directory.Exists(dir)) continue;
                    foreach (var file in Directory.GetFiles(dir)) {
                        if (SampleExts.Contains(System.IO.Path.GetExtension(file).ToLower())) {
                            files.Add(file);
                        }
                    }
                } catch (Exception e) {
                    Log.Error(e, $"Failed to scan samples directory {dir}");
                }
            }
            Samples.Clear();
            foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase)) {
                Samples.Add(new SampleItem(file));
            }
            HasSamples = Samples.Count > 0;
        }

        public void OnNext(UCommand cmd, bool isUndo) {
            if (cmd is SingersRefreshedNotification) {
                RefreshSingers();
            }
        }

        void OnPreviewChanged(string? path) => Dispatcher.UIThread.Post(() => {
            if (disposed) return;
            foreach (var item in Samples) item.IsPreviewing = item.Path == path;
        });

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            PlaybackManager.Inst.PreviewChanged -= OnPreviewChanged;
            DocManager.Inst.RemoveSubscriber(this);
            foreach (var item in Singers) item.Dispose();
            Singers.Clear();
            Samples.Clear();
        }
    }
}
