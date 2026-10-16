using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using OpenUtau.Core;
using ReactiveUI.Fody.Helpers;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;
using Serilog;

namespace OpenUtau.App.ViewModels {
    public class PackageRowViewModel : ViewModelBase {
        public RegistrySoftware? Software { get; }
        public string Id { get; }
        public string Name { get; }
        public string Developer { get; }
        public string Version { get; }
        [Reactive] public bool IsInstalled { get; set; }
        [Reactive] public string InstalledVersion { get; set; } = string.Empty;

        public bool HasRegistry => Software != null;
        public bool IsUpToDate => IsInstalled && HasRegistry && !string.IsNullOrEmpty(InstalledVersion) && InstalledVersion == Version;
        public bool CanInstallOrUpdate => HasRegistry && (!IsInstalled || !IsUpToDate);
        public string PrimaryActionLabel => !HasRegistry ? ThemeManager.GetString("packages.install") : (!IsInstalled ? ThemeManager.GetString("packages.install") : (IsUpToDate ? ThemeManager.GetString("packages.install") : ThemeManager.GetString("packages.update")));
        public bool CanUninstall => IsInstalled;
        public string InstalledDisplay => IsInstalled ? (string.IsNullOrEmpty(InstalledVersion) ? ThemeManager.GetString("packages.unknownversion") : InstalledVersion) : string.Empty;

        public PackageRowViewModel(RegistrySoftware s) {
            Software = s;
            Id = s.id;
            Name = s.LocalizedName();
            Developer = (s.developers != null && s.developers.Length > 0) ? string.Join(", ", s.developers) : string.Empty;
            Version = (s.versions != null && s.versions.Length > 0) ? PackageManager.GetLatestVersionString(s.versions) : string.Empty;

            this.WhenAnyValue(x => x.IsInstalled, x => x.InstalledVersion)
                .Subscribe(_ => {
                    this.RaisePropertyChanged(nameof(IsUpToDate));
                    this.RaisePropertyChanged(nameof(CanInstallOrUpdate));
                    this.RaisePropertyChanged(nameof(PrimaryActionLabel));
                    this.RaisePropertyChanged(nameof(CanUninstall));
                    this.RaisePropertyChanged(nameof(InstalledDisplay));
                });
        }

        public PackageRowViewModel(string id, string name, string developer, string version) {
            Software = null;
            Id = id;
            Name = name;
            Developer = developer;
            Version = version;
            IsInstalled = false;
        }

        public void SetInstalled(OudepMetadata info) {
            IsInstalled = true;
            InstalledVersion = info.version ?? string.Empty;
            this.RaisePropertyChanged(nameof(IsUpToDate));
            this.RaisePropertyChanged(nameof(CanInstallOrUpdate));
            this.RaisePropertyChanged(nameof(PrimaryActionLabel));
            this.RaisePropertyChanged(nameof(CanUninstall));
            this.RaisePropertyChanged(nameof(InstalledDisplay));
        }
    }

    public class PackageManagerViewModel : ViewModelBase, IDisposable {
        readonly CompositeDisposable subscriptions = new();
        readonly HashSet<PackageRowViewModel> observedRows = new();
        readonly Func<Task<List<RegistrySoftware>>> fetchRegistry;
        readonly Func<Task<List<OudepMetadata>>> listInstalled;
        readonly Func<RegistrySoftware, IProgress<int>, Task> install;
        Task? refreshing;
        int operation;
        [Reactive] public bool IsInstalling { get; private set; }
        bool disposed;
        public ObservableCollection<PackageRowViewModel> Available { get; } = new ObservableCollection<PackageRowViewModel>();
        [Reactive] public string SearchText { get; set; } = string.Empty;
        [Reactive] public int FilterIndex { get; set; }
        public IEnumerable<PackageRowViewModel> FilteredAvailable => Available.Where(r =>
            (FilterIndex != 1 || r.IsInstalled) &&
            (FilterIndex != 2 || (r.IsInstalled && r.CanInstallOrUpdate)) &&
            (string.IsNullOrWhiteSpace(SearchText) ||
             r.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) ||
             r.Id.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) ||
             r.Developer.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase) ||
             r.Version.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase)));
        public int MatchCount => FilteredAvailable.Count();
        public bool NoMatches => MatchCount == 0;
        [Reactive] public string Status { get; set; } = string.Empty;
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
        public ReactiveCommand<PackageRowViewModel, Unit> InstallCommand { get; }
        public ReactiveCommand<PackageRowViewModel, Unit> UninstallCommand { get; }

        public PackageManagerViewModel() : this(true) { }

        public PackageManagerViewModel(bool refreshOnStart,
            Func<Task<List<RegistrySoftware>>>? fetchRegistry = null,
            Func<Task<List<OudepMetadata>>>? listInstalled = null,
            Func<RegistrySoftware, IProgress<int>, Task>? install = null) {
            this.fetchRegistry = fetchRegistry ?? PackageManager.Inst.FetchRegistryAsync;
            this.listInstalled = listInstalled ?? PackageManager.Inst.GetInstalledAsync;
            this.install = install ?? ((software, progress) => PackageManager.Inst.InstallAsync(software, progress));
            var idle = this.WhenAnyValue(vm => vm.IsInstalling).Select(busy => !busy);
            RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync, idle);
            InstallCommand = ReactiveCommand.CreateFromTask<PackageRowViewModel>(InstallAsync, idle);
            UninstallCommand = ReactiveCommand.CreateFromTask<PackageRowViewModel>(UninstallAsync, idle);
            Available.CollectionChanged += OnAvailableChanged;
            subscriptions.Add(this.WhenAnyValue(x => x.SearchText, x => x.FilterIndex).Subscribe(_ => RefreshFilter()));
            if (refreshOnStart) _ = RefreshAsync();
        }

        void OnAvailableChanged(object? sender, NotifyCollectionChangedEventArgs e) {
            if (e.Action == NotifyCollectionChangedAction.Reset) {
                foreach (var row in observedRows) row.PropertyChanged -= OnRowChanged;
                observedRows.Clear();
            }
            if (e.OldItems != null) foreach (PackageRowViewModel row in e.OldItems) {
                row.PropertyChanged -= OnRowChanged; observedRows.Remove(row);
            }
            if (e.NewItems != null) foreach (PackageRowViewModel row in e.NewItems) {
                if (observedRows.Add(row)) row.PropertyChanged += OnRowChanged;
            }
            RefreshFilter();
        }
        void OnRowChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(PackageRowViewModel.IsInstalled) ||
                e.PropertyName == nameof(PackageRowViewModel.InstalledVersion)) RefreshFilter();
        }
        void RefreshFilter() {
            this.RaisePropertyChanged(nameof(FilteredAvailable));
            this.RaisePropertyChanged(nameof(MatchCount));
            this.RaisePropertyChanged(nameof(NoMatches));
        }

        public void Dispose() {
            disposed = true;
            Available.CollectionChanged -= OnAvailableChanged;
            foreach (var row in observedRows) row.PropertyChanged -= OnRowChanged;
            observedRows.Clear();
            subscriptions.Dispose();
            RefreshCommand.Dispose(); InstallCommand.Dispose(); UninstallCommand.Dispose();
        }

        public Task RefreshAsync() => disposed ? Task.CompletedTask :
            refreshing is { IsCompleted: false } ? refreshing : refreshing = RefreshCoreAsync();

        async Task RefreshCoreAsync() {
            if (disposed) return;
            try {
                Status = ThemeManager.GetString("packages.status.fetching");
                List<RegistrySoftware> registry;
                bool offline = false;
                try { registry = await fetchRegistry(); }
                catch (Exception e) {
                    Log.Warning(e, "Registry unavailable; listing locally installed packages.");
                    registry = new(); offline = true;
                }
                if (disposed) return;
                Status = ThemeManager.GetString("packages.status.listinginstalled");
                var installed = await listInstalled();
                if (disposed) return;

                var installedById = installed.ToDictionary(i => i.id, i => i);

                var rows = new List<PackageRowViewModel>();

                foreach (var s in registry) {
                    var row = new PackageRowViewModel(s);
                    if (installedById.TryGetValue(row.Id, out var info)) {
                        row.SetInstalled(info);
                        installedById.Remove(row.Id);
                    }
                    rows.Add(row);
                }

                foreach (var info in installedById.Values) {
                    var id = info.id;
                    var row = new PackageRowViewModel(id, id, string.Empty, string.Empty);
                    row.SetInstalled(info);
                    rows.Add(row);
                }

                var ordered = rows.OrderByDescending(r => r.IsInstalled).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();

                Available.Clear();
                foreach (var r in ordered) Available.Add(r);

                Status = ThemeManager.GetString(offline ? "workspace.packages.offline" : "packages.status.ready");
            } catch (Exception e) {
                if (disposed) return;
                Status = ThemeManager.GetString("packages.status.error");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            }
        }

        public async Task<bool> InstallRequiredAsync(string id) {
            if (disposed || !MissingPackageException.IsRegistryId(id)) return false;
            SearchText = id; FilterIndex = 0;
            await RefreshAsync();
            if (disposed) return false;
            var row = Available.FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
            if (row?.Software == null) {
                Status = string.Format(ThemeManager.GetString("packages.required.unavailable"), id);
                return false;
            }
            // This request follows a missing-file error; even an installed receipt may need repair.
            return await InstallCoreAsync(row, repair: true);
        }

        public async Task InstallAsync(PackageRowViewModel row) => await InstallCoreAsync(row, repair: false);

        async Task<bool> InstallCoreAsync(PackageRowViewModel row, bool repair) {
            if (disposed || IsInstalling) return false;
            IsInstalling = true;
            int request = ++operation;
            try {
                if (row.Software == null) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(new InvalidOperationException("No registry entry to install.")));
                    return false;
                }
                if (row.IsUpToDate && !repair) return true;
                var installingTemplate = ThemeManager.GetString("packages.status.installing");
                var baseStatus = string.Format(installingTemplate, row.Id);
                Status = baseStatus;
                var progress = new Progress<int>(p => {
                    if (!disposed && IsInstalling && request == operation) Status = $"{baseStatus} ({p}%)";
                });
                await install(row.Software, progress);
                if (disposed) return false;
                await RefreshAsync();
                if (disposed) return false;
                Status = ThemeManager.GetString("packages.status.installfinished");
                return true;
            } catch (Exception e) {
                if (disposed) return false;
                Status = ThemeManager.GetString("packages.status.installfailed");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
                return false;
            } finally { IsInstalling = false; }
        }

        public async Task UninstallAsync(PackageRowViewModel row) {
            if (disposed || IsInstalling) return;
            IsInstalling = true;
            operation++;
            try {
                if (!row.IsInstalled) {
                    DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(new InvalidOperationException("Package is not installed.")));
                    return;
                }
                Status = string.Format(ThemeManager.GetString("packages.status.uninstalling"), row.Id);
                await PackageManager.Inst.UninstallAsync(row.Id);
                if (disposed) return;
                await RefreshAsync();
                if (disposed) return;
                Status = ThemeManager.GetString("packages.status.uninstallfinished");
            } catch (Exception e) {
                if (disposed) return;
                Status = ThemeManager.GetString("packages.status.uninstallfailed");
                DocManager.Inst.ExecuteCmd(new ErrorMessageNotification(e));
            } finally { IsInstalling = false; }
        }
    }
}
