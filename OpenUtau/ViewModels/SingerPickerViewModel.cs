using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using Avalonia.Threading;
using OpenUtau.Core;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace OpenUtau.App.ViewModels;

public sealed class SingerPickerRow {
    public required USinger Singer { get; init; }
    public required string Section { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsFavorite { get; init; }
    public int AvatarRevision { get; init; }
    public string FavoriteGlyph => IsFavorite ? "★" : "☆";
    public string Name => Singer.LocalizedName;
    public string Detail => $"{Section} · {Singer.SingerType}";
}

public sealed class SingerTypeFilterOption {
    public USingerType? Type { get; init; }
    public required string Label { get; init; }
    public required string ToolTip { get; init; }
    public bool IsSelected { get; init; }
}

public sealed class SingerPickerViewModel : ViewModelBase, ICmdSubscriber, IDisposable {
    readonly Func<IEnumerable<USinger>> source;
    readonly IList<string> favorites;
    readonly IList<string> recent;
    readonly Action save;
    readonly string? currentId;
    readonly IDisposable searchSubscription;
    bool disposed;
    bool subscribed;
    int avatarRevision;
    [Reactive] public string Search { get; set; } = "";
    [Reactive] public USingerType? TypeFilter { get; set; }
    [Reactive] public IReadOnlyList<SingerTypeFilterOption> TypeFilters { get; private set; } = Array.Empty<SingerTypeFilterOption>();
    [Reactive] public IReadOnlyList<SingerPickerRow> Rows { get; private set; } = Array.Empty<SingerPickerRow>();
    [Reactive] public SingerPickerRow? Selected { get; set; }
    public bool IsEmpty => Rows.Count == 0;
    public bool HasCurrent => !string.IsNullOrEmpty(CurrentName);
    public bool CanClearSinger { get; }
    public bool HasTypeFilters => TypeFilters.Count > 1;
    public string CurrentName { get; }

    public SingerPickerViewModel(USinger? current) : this(current,
        () => SingerManager.Inst.Singers.Values, Preferences.Default.FavoriteSingers,
        Preferences.Default.RecentSingers, Preferences.Save) { }

    public SingerPickerViewModel(USinger? current, Func<IEnumerable<USinger>> source,
        IList<string> favorites, IList<string> recent, Action save) {
        this.source = source; this.favorites = favorites; this.recent = recent; this.save = save;
        currentId = current?.Id;
        CanClearSinger = current != null && (current.Found || !string.IsNullOrEmpty(current.Name));
        CurrentName = current == null ? "" : current.LocalizedName + (current.Found ? "" : " · " + ThemeManager.GetString("singerpicker.missing"));
        searchSubscription = this.WhenAnyValue(vm => vm.Search, vm => vm.TypeFilter).Subscribe(_ => Refresh());
    }
    public void Open() {
        if (disposed || subscribed) return;
        subscribed = true;
        DocManager.Inst.AddSubscriber(this);
        Refresh();
    }
    public void SetTypeFilter(USingerType? type) {
        if (disposed) return;
        TypeFilter = TypeFilter == type ? null : type;
    }
    public void Refresh() {
        if (disposed) return;
        var foundSingers = source().Where(s => s.Found).DistinctBy(s => s.Id).ToArray();
        var availableTypes = foundSingers
            .Select(s => s.SingerType)
            .Distinct()
            .OrderBy(t => t)
            .ToArray();
        if (TypeFilter.HasValue && !availableTypes.Contains(TypeFilter.Value)) {
            TypeFilter = null;
            return;
        }
        var filters = new List<SingerTypeFilterOption>();
        if (availableTypes.Length > 1) {
            filters.Add(new SingerTypeFilterOption {
                Type = null,
                Label = ThemeManager.GetString("tracks.filtertype.all"),
                ToolTip = ThemeManager.GetString("tracks.filtertype.all.tip"),
                IsSelected = TypeFilter == null,
            });
            string onlyTemplate = ThemeManager.GetString("tracks.filtertype.only");
            foreach (var type in availableTypes) {
                string label = type switch {
                    USingerType.Classic => "UTAU",
                    USingerType.DiffSinger => "DiffSinger",
                    USingerType.Enunu => "ENUNU",
                    USingerType.Voicevox => "VOICEVOX",
                    USingerType.Vogen => "Vogen",
                    _ => type.ToString(),
                };
                filters.Add(new SingerTypeFilterOption {
                    Type = type,
                    Label = label,
                    ToolTip = string.Format(onlyTemplate, label),
                    IsSelected = TypeFilter == type,
                });
            }
        }
        TypeFilters = filters;
        this.RaisePropertyChanged(nameof(HasTypeFilters));

        string? highlighted = Selected?.Singer.Id ?? currentId;
        Rows = foundSingers
            .Where(s => !TypeFilter.HasValue || s.SingerType == TypeFilter.Value)
            .Where(s => SingerSearch.Matches(s, Search))
            .OrderBy(s => favorites.Contains(s.Id) ? 0 : recent.Contains(s.Id) ? 1 : 2)
            .ThenBy(s => favorites.Contains(s.Id) ? 0 : recent.Contains(s.Id) ? recent.IndexOf(s.Id) : int.MaxValue)
            .ThenBy(s => s.LocalizedName, StringComparer.CurrentCultureIgnoreCase)
            .Select(s => new SingerPickerRow {
                Singer = s, IsCurrent = s.Id == currentId, IsFavorite = favorites.Contains(s.Id), AvatarRevision = avatarRevision,
                Section = ThemeManager.GetString(favorites.Contains(s.Id) ? "singerpicker.favorite" : recent.Contains(s.Id) ? "singerpicker.recent" : "singerpicker.all"),
            }).ToArray();
        Selected = Rows.FirstOrDefault(r => r.Singer.Id == highlighted) ?? Rows.FirstOrDefault();
        this.RaisePropertyChanged(nameof(IsEmpty));
    }
    public void ToggleFavorite(SingerPickerRow row) {
        if (disposed) return;
        if (favorites.Contains(row.Singer.Id)) favorites.Remove(row.Singer.Id);
        else favorites.Add(row.Singer.Id);
        save(); Refresh();
    }
    public void Move(int delta) {
        if (Rows.Count == 0) return;
        int index = Selected == null ? -1 : Rows.ToList().IndexOf(Selected);
        Selected = Rows[Math.Clamp(index + delta, 0, Rows.Count - 1)];
    }
    public void OnNext(UCommand cmd, bool isUndo) {
        if (cmd is SingersChangedNotification or SingersRefreshedNotification)
            Dispatcher.UIThread.Post(() => { if (!disposed) { avatarRevision++; Refresh(); } });
    }
    public void Dispose() {
        if (disposed) return;
        disposed = true;
        searchSubscription.Dispose();
        if (subscribed) DocManager.Inst.RemoveSubscriber(this);
    }
}
