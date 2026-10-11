using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using DynamicData.Binding;
using NAudio.Wave;
using NWaves.Signals;
using OpenUtau.Api;
using OpenUtau.App.Views;
using OpenUtau.Classic;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class SingersViewModel : ViewModelBase, IDisposable {
        readonly CompositeDisposable subscriptions = new();
        readonly IEnumerable<USinger>? singerSource;
        public IEnumerable<USinger> Singers => singerSource ?? SingerManager.Inst.SingerGroups.Values.SelectMany(l => l);
        [Reactive] public string SearchSinger { get; set; } = "";
        public IEnumerable<USinger> FilteredSingers => Singers.Where(s =>
            string.IsNullOrWhiteSpace(SearchSinger) ||
            s.LocalizedName.Contains(SearchSinger.Trim(), StringComparison.OrdinalIgnoreCase) ||
            s.Id?.Contains(SearchSinger.Trim(), StringComparison.OrdinalIgnoreCase) == true);
        public int SingerCount => FilteredSingers.Count();
        public bool NoSingerMatches => SingerCount == 0;
        public bool HasSinger => Singer != null;
        [Reactive] public USinger? Singer { get; set; }
        [Reactive] public Bitmap? Avatar { get; set; }
        [Reactive] public int AvatarRevision { get; set; }
        [Reactive] public string? Info { get; set; }
        [Reactive] public bool HasWebsite { get; set; }
        public bool IsClassic => Singer != null && Singer.SingerType == USingerType.Classic;
        public bool UseSearchAlias => Singer != null && (Singer.SingerType == USingerType.Classic || Singer.SingerType == USingerType.Enunu);
        public ObservableCollectionExtended<USubbank> Subbanks => subbanks;
        public ObservableCollectionExtended<UOto> Otos => otos;
        public ObservableCollectionExtended<UOto> DisplayedOtos { get; set; } = new ObservableCollectionExtended<UOto>();
        [Reactive] public bool ZoomInMel { get; set; }
        [Reactive] public UOto? SelectedOto { get; set; }
        [Reactive] public int SelectedIndex { get; set; }
        public List<MenuItemViewModel> SetEncodingMenuItems => setEncodingMenuItems;
        public List<MenuItemViewModel> SetSingerTypeMenuItems => setSingerTypeMenuItems;
        public List<MenuItemViewModel> SetDefaultPhonemizerMenuItems => setDefaultPhonemizerMenuItems;
        [Reactive] public bool UseFilenameAsAlias { get; set; } = false;

        [Reactive] public string SearchAlias { get; set; } = "";

        private readonly ObservableCollectionExtended<USubbank> subbanks
            = new ObservableCollectionExtended<USubbank>();
        private readonly ObservableCollectionExtended<UOto> otos
            = new ObservableCollectionExtended<UOto>();
        private readonly ReactiveCommand<Encoding, Unit> setEncodingCommand;
        private List<MenuItemViewModel> setEncodingMenuItems;
        private readonly ReactiveCommand<string, Unit> setSingerTypeCommand;
        private List<MenuItemViewModel> setSingerTypeMenuItems;
        private readonly ReactiveCommand<Api.PhonemizerFactory, Unit> setDefaultPhonemizerCommand;
        private List<MenuItemViewModel> setDefaultPhonemizerMenuItems;

        public SingersViewModel() : this(null) { }

        public SingersViewModel(IEnumerable<USinger>? singers) {
            singerSource = singers;
#if DEBUG
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
            setEncodingMenuItems = new List<MenuItemViewModel>();
            setSingerTypeMenuItems = new List<MenuItemViewModel>();
            setDefaultPhonemizerMenuItems = new List<MenuItemViewModel>();
            setEncodingCommand = ReactiveCommand.Create<Encoding>(SetEncoding);
            setSingerTypeCommand = ReactiveCommand.Create<string>(SetSingerType);
            setDefaultPhonemizerCommand = ReactiveCommand.Create<Api.PhonemizerFactory>(SetDefaultPhonemizer);
            subscriptions.Add(this.WhenAnyValue(vm => vm.SearchSinger).Subscribe(_ => RefreshSingerFilter()));
            subscriptions.Add(this.WhenAnyValue(vm => vm.Singer).Subscribe(singer => {
                this.RaisePropertyChanged(nameof(HasSinger));
                if (singer == null) {
                    Avatar?.Dispose(); Avatar = null;
                    Otos.Clear(); DisplayedOtos.Clear(); Subbanks.Clear();
                    Info = string.Empty; HasWebsite = false; SelectedOto = null;
                    this.RaisePropertyChanged(nameof(IsClassic));
                    this.RaisePropertyChanged(nameof(UseSearchAlias));
                }
            }));
            if (Singers.Count() > 0) {
                Singer = Singers.FirstOrDefault();
            }
            subscriptions.Add(this.WhenAnyValue(vm => vm.Singer)
                .WhereNotNull()
                .Subscribe(singer => {
                    if (LoadingWindow.IsLoading()) {
                        try {
                            AttachSinger();
                        } catch (Exception e) {
                            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                        }
                    } else {
                        DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(SingersDialog), true, "singer"));
                        try {
                            AttachSinger();
                        } catch (Exception e) {
                            DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                        } finally {
                            DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(SingersDialog), false, "singer"));
                        }
                    }
                    void AttachSinger() {
                        singer.EnsureLoaded();
                        var previousAvatar = Avatar;
                        Avatar = LoadAvatar(singer);
                        previousAvatar?.Dispose();
                        AvatarRevision++;
                        SelectedOto = null;
                        SelectedIndex = -1;
                        Otos.Clear();
                        Otos.AddRange(singer.Otos);
                        Search();
                        Info = $"Author: {singer.Author}\nVoice: {singer.Voice}\nWeb: {singer.Web}\nVersion: {singer.Version}\n{singer.OtherInfo}\n\n{string.Join("\n", singer.Errors)}";
                        HasWebsite = !string.IsNullOrEmpty(singer.Web);
                        if (Singer is ClassicSinger cSinger) {
                            UseFilenameAsAlias = cSinger.UseFilenameAsAlias ?? false;
                        }
                        LoadSubbanks();
                        DocManager.Inst.ExecuteCmd(new OtoChangedNotification());
                        this.RaisePropertyChanged(nameof(IsClassic));
                        this.RaisePropertyChanged(nameof(UseSearchAlias));
                        var encodings = new Encoding[] {
                            Encoding.GetEncoding("shift_jis"),
                            Encoding.ASCII,
                            Encoding.UTF8,
                            Encoding.GetEncoding("gb2312"),
                            Encoding.GetEncoding("big5"),
                            Encoding.GetEncoding("ks_c_5601-1987"),
                            Encoding.GetEncoding("Windows-1252"),
                            Encoding.GetEncoding("macintosh"),
                        };
                        setEncodingMenuItems = encodings.Select(encoding =>
                            new MenuItemViewModel(singer.TextFileEncoding == encoding) {
                                Header = encoding.EncodingName,
                                Command = setEncodingCommand,
                                CommandParameter = encoding,
                            }
                        ).ToList();
                        var singerTypes = new string[] {
                            "utau", "enunu", "diffsinger", "voicevox"
                        };
                        setSingerTypeMenuItems = singerTypes.Select(singerType =>
                            new MenuItemViewModel((SingerTypeUtils.SingerTypeNames.TryGetValue(singer.SingerType, out var name) ? name : "") == singerType) {
                                Header = singerType,
                                Command = setSingerTypeCommand,
                                CommandParameter = singerType,
                            }
                        ).ToList();
                        setDefaultPhonemizerMenuItems = PhonemizerFactory.GetAll().Select(factory => 
                            new MenuItemViewModel(singer.DefaultPhonemizer == factory.type.FullName) {
                                Header = factory.ToString(),
                                Command = setDefaultPhonemizerCommand,
                                CommandParameter = factory,
                            }
                        ).ToList();
                        this.RaisePropertyChanged(nameof(SetEncodingMenuItems));
                        this.RaisePropertyChanged(nameof(SetSingerTypeMenuItems));
                        this.RaisePropertyChanged(nameof(SetDefaultPhonemizerMenuItems));
                    }
                }));
            subscriptions.Add(this.WhenAnyValue(vm => vm.SearchAlias)
                .Subscribe(alias => {
                    Search();
                }));
        }

        void RefreshSingerFilter() {
            this.RaisePropertyChanged(nameof(FilteredSingers));
            this.RaisePropertyChanged(nameof(SingerCount));
            this.RaisePropertyChanged(nameof(NoSingerMatches));
        }

        private void SetEncoding(Encoding encoding) {
            if (Singer == null) {
                return;
            }
            try {
                ModifyConfig(Singer, config => config.TextFileEncoding = encoding.WebName);
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to save singer config", "<translate:errors.failed.savesingerconfig>", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        public void SetImage(string filepath) {
            if (Singer == null) {
                return;
            }
            try {
                ModifyConfig(Singer, config => config.Image = filepath);
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to save singer config", "<translate:errors.failed.savesingerconfig>", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        public void SetPortrait(string filepath) {
            if (Singer == null) {
                return;
            }
            try {
                ModifyConfig(Singer, config => config.Portrait = filepath);
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to save singer config", "<translate:errors.failed.savesingerconfig>", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        private void SetSingerType(string singerType) {
            if (Singer == null) {
                return;
            }
            try {
                ModifyConfig(Singer, config => config.SingerType = singerType);
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to save singer config", "<translate:errors.failed.savesingerconfig>", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        private void SetDefaultPhonemizer(Api.PhonemizerFactory factory) {
            if (Singer == null) {
                return;
            }
            try {
                ModifyConfig(Singer, config => config.DefaultPhonemizer = factory.type.FullName ?? string.Empty);
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to save singer config", "<translate:errors.failed.savesingerconfig>", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        public void SetUseFilenameAsAlias() {
            if (Singer == null || !IsClassic) {
                return;
            }
            try {
                ModifyConfig(Singer, config => config.UseFilenameAsAlias = !this.UseFilenameAsAlias);
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to save singer config", "<translate:errors.failed.savesingerconfig>", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        private void ModifyConfig(USinger singer, Action<VoicebankConfig> modify) {
            var yamlFile = Path.Combine(singer.Location, "character.yaml");
            VoicebankConfig? config = null;
            if (File.Exists(yamlFile)) {
                using (var stream = File.OpenRead(yamlFile)) {
                    config = VoicebankConfig.Load(stream);
                }
            }
            if (config == null) {
                config = new VoicebankConfig();
            }
            modify(config);
            using (var stream = File.Open(yamlFile, FileMode.Create)) {
                config.Save(stream);
            }
            RefreshSinger();
        }

        public void ErrorReport() {
            if (Singer == null || Singer.SingerType != USingerType.Classic) {
                return;
            }
            Task task = Task.Run(() => {
                DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(SingersDialog), true, "singer error report"));
                var checker = new VoicebankErrorChecker(Singer.Location, Singer.BasePath);
                checker.Check();
                string outFile = Path.Combine(Singer.Location, "errors.txt");
                using (var stream = File.Open(outFile, FileMode.Create)) {
                    using (var writer = new StreamWriter(stream)) {
                        writer.WriteLine($"------ Informations ------");
                        writer.WriteLine();
                        for (var i = 0; i < checker.Infos.Count; i++) {
                            writer.WriteLine($"--- Info {i + 1} ---");
                            writer.WriteLine(GetErrorText(checker.Infos[i]));
                        }
                        writer.WriteLine();
                        writer.WriteLine($"------ Errors ------");
                        writer.WriteLine($"Total errors: {checker.Errors.Count}");
                        writer.WriteLine();
                        for (var i = 0; i < checker.Errors.Count; i++) {
                            writer.WriteLine($"--- Error {i + 1} ---");
                            writer.WriteLine(GetErrorText(checker.Errors[i]));
                        }
                    }
                }
                OS.GotoFile(outFile);
            });
            task.ContinueWith(task => {
                DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(SingersDialog), false, "singer error report"));
                if (task.IsFaulted && task.Exception != null) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(task.Exception));
                }
            });

            string GetErrorText(VoicebankError error) {
                var builder = new StringBuilder();
                if (!string.IsNullOrEmpty(error.messageKey)) {
                    var message = ThemeManager.GetString(error.messageKey);
                    builder.AppendLine(string.Format(message, error.strings));
                }
                if (error.trace != null) {
                    builder.AppendLine(error.trace.ToString());
                }
                if (!string.IsNullOrEmpty(error.soundFile)) {
                    builder.AppendLine(error.soundFile);
                }
                if (error.e != null) {
                    builder.AppendLine(error.e.ToString());
                }
                return builder.ToString();
            }
        }

        public void Refresh() {
            string singerId = string.Empty;
            if (Singer != null) {
                singerId = Singer.Id;
            }
            DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(SingersDialog), true, "singer"));
            SingerManager.Inst.SearchAllSingers();
            this.RaisePropertyChanged(nameof(Singers));
            RefreshSingerFilter();
            if (!string.IsNullOrEmpty(singerId) && SingerManager.Inst.Singers.TryGetValue(singerId, out var singer)) {
                Singer = singer;
            } else {
                Singer = Singers.FirstOrDefault();
            }
            DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification());
            DocManager.Inst.ExecuteCmd(new LoadingNotification(typeof(SingersDialog), false, "singer"));
        }

        Bitmap? LoadAvatar(USinger singer) {
            if (singer.AvatarData == null) {
                return null;
            }
            try {
                using (var stream = new MemoryStream(singer.AvatarData)) {
                    return new Bitmap(stream);
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to load avatar.");
                return null;
            }
        }

        public void OpenLocation() {
            try {
                if (Singer != null) {
                    var location = Singer.Location;
                    if (File.Exists(location)) {
                        //Vogen voicebank is a singlefile
                        OS.GotoFile(location);
                    } else {
                        //classic or ENUNU voicebank is a folder
                        OS.OpenFolder(location);
                    }
                }
            } catch (Exception e) {
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public void LoadSubbanks() {
            Subbanks.Clear();
            if (Singer == null) {
                return;
            }
            try {
                Subbanks.AddRange(Singer.Subbanks);
            } catch (Exception e) {
                var customEx = new MessageCustomizableException("Failed to load subbanks", "<translate:errors.failed.load>: subbanks", e);
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(customEx));
            }
        }

        public void RefreshSinger() {
            if (Singer == null) {
                return;
            }
            int index = SelectedIndex;

            Singer.Reload();
            var previousAvatar = Avatar;
            Avatar = LoadAvatar(Singer);
            previousAvatar?.Dispose();
            AvatarRevision++;
            Otos.Clear();
            Otos.AddRange(Singer.Otos);
            Search();
            LoadSubbanks();

            DocManager.Inst.ExecuteCmd(new SingersRefreshedNotification(Singer));
            DocManager.Inst.ExecuteCmd(new OtoChangedNotification());
            if (DisplayedOtos.Count > 0) {
                index = Math.Clamp(index, 0, DisplayedOtos.Count - 1);
                SelectedIndex = index;
                SelectedOto = DisplayedOtos[index];
            } else {
                SelectedIndex = -1; SelectedOto = null;
            }
        }

        private void Search() {
            if (string.IsNullOrWhiteSpace(SearchAlias)) {
                DisplayedOtos.Clear();
                DisplayedOtos.AddRange(Otos);
            } else {
                DisplayedOtos.Clear();
                DisplayedOtos.AddRange(Otos.Where(o => o.Alias.Contains(SearchAlias.Trim(), StringComparison.OrdinalIgnoreCase)));
            }
        }

        public void SetOffset(double value, double totalDur) {
            if (SelectedOto == null) {
                return;
            }
            var delta = value - SelectedOto.Offset;
            SelectedOto.Offset += delta;
            SelectedOto.Consonant -= delta;
            SelectedOto.Preutter -= delta;
            SelectedOto.Overlap -= delta;
            if (SelectedOto.Cutoff < 0) {
                SelectedOto.Cutoff += delta;
            }
            FixCutoff(SelectedOto, totalDur);
            NotifyOtoChanged();
        }

        public void SetOverlap(double value, double totalDur) {
            if (SelectedOto == null) {
                return;
            }
            SelectedOto.Overlap = value - SelectedOto.Offset;
            FixCutoff(SelectedOto, totalDur);
            NotifyOtoChanged();
        }

        public void SetPreutter(double value, double totalDur) {
            if (SelectedOto == null) {
                return;
            }
            SelectedOto.Preutter = value - SelectedOto.Offset;
            FixCutoff(SelectedOto, totalDur);
            NotifyOtoChanged();
        }

        public void SetFixed(double value, double totalDur) {
            if (SelectedOto == null) {
                return;
            }
            SelectedOto.Consonant = value - SelectedOto.Offset;
            FixCutoff(SelectedOto, totalDur);
            NotifyOtoChanged();
        }

        public void SetCutoff(double value, double totalDur) {
            if (SelectedOto == null || value < SelectedOto.Offset) {
                return;
            }
            SelectedOto.Cutoff = -(value - SelectedOto.Offset);
            FixCutoff(SelectedOto, totalDur);
            NotifyOtoChanged();
        }

        private static void FixCutoff(UOto oto, double totalDur) {
            double cutoff = oto.Cutoff >= 0
                ? totalDur - oto.Cutoff
                : oto.Offset - oto.Cutoff;
            // 1ms is inserted between consonant and cutoff to avoid resample problem.
            double minCutoff = oto.Offset + Math.Max(Math.Max(oto.Overlap, oto.Preutter), oto.Consonant + 1);
            if (cutoff < minCutoff) {
                oto.Cutoff = -(minCutoff - oto.Offset);
            }
        }

        public void NotifyOtoChanged() {
            if (Singer != null) {
                Singer.OtoDirty = true;
            }
            DocManager.Inst.ExecuteCmd(new OtoChangedNotification());
        }

        public void SaveOtos() {
            if (Singer != null) {
                try {
                    Singer.Save();
                } catch (Exception e) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                }
            }
            RefreshSinger();
        }

        public void GotoOto(USinger singer, UOto? oto) {
            if (Singers.Contains(singer)) {
                Singer = singer;
                if (oto != null && Singer.Otos.Contains(oto)) {
                    if (!DisplayedOtos.Contains(oto)) SearchAlias = string.Empty;
                    SelectedOto = oto;
                }
            }
        }

        public Task RegenFrq(string[] files, string? method, Action<int> progress) {
            return Task.Run(() => {
                double stepMs = Frq.kHopSize * 1000.0 / 44100;
                int count = 0;
                if (method == "crepe") {
                    Parallel.For(0, files.Length, new ParallelOptions {
                        MaxDegreeOfParallelism = Environment.ProcessorCount / 2
                    },
                    () => new Core.Analysis.Crepe.Crepe(),
                    (i, loop, crepe) => {
                        string file = files[i];
                        if (!File.Exists(file)) {
                            throw new FileNotFoundException(string.Format("File {0} missing!", file));
                        }
                        string frqFile = VoicebankFiles.GetFrqFile(file);
                        DiscreteSignal? signal = null;
                        using (var waveStream = Core.Format.Wave.OpenFile(file)) {
                            signal = Core.Format.Wave.GetSignal(waveStream.ToSampleProvider().ToMono(1, 0));
                        }
                        if (signal != null) {
                            var frq = Frq.Build(signal.Samples, crepe.ComputeF0(signal, stepMs));
                            using (var stream = File.OpenWrite(frqFile)) {
                                frq.Save(stream);
                            }
                        }
                        progress.Invoke(Interlocked.Increment(ref count));
                        return crepe;
                    },
                    crepe => crepe.Dispose());
                } else {
                    Parallel.ForEach(files, parallelOptions: new ParallelOptions {
                        MaxDegreeOfParallelism = Environment.ProcessorCount / 2
                    }, body: file => {
                        if (!File.Exists(file)) {
                            throw new FileNotFoundException(string.Format("File {0} missing!", file));
                        }
                        string frqFile = VoicebankFiles.GetFrqFile(file);
                        float[]? samples;
                        using (var waveStream = Core.Format.Wave.OpenFile(file)) {
                            samples = Core.Format.Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                        }
                        if (samples != null) {
                            int f0Method;
                            switch (method) {
                                case "harvest":
                                    f0Method = 1;
                                    break;
                                case "pyin":
                                    f0Method = 2;
                                    break;
                                default:
                                    f0Method = 0;
                                    break;
                            }
                            var f0 = Core.Render.Worldline.F0(samples, 44100, stepMs, f0Method);
                            var frq = Frq.Build(samples, f0);
                            using (var stream = File.OpenWrite(frqFile)) {
                                frq.Save(stream);
                            }
                        }
                        progress.Invoke(Interlocked.Increment(ref count));
                    });
                }
            });
        }
        public void Dispose() {
            subscriptions.Dispose();
            Avatar?.Dispose(); Avatar = null;
            setEncodingCommand.Dispose(); setSingerTypeCommand.Dispose(); setDefaultPhonemizerCommand.Dispose();
        }

    }
}
