using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;

namespace ClearDesk.GlassWindow;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class EntryViewModel(CatalogEntry entry) : ObservableObject
{
    public CatalogEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public ImageSource? Icon => Entry.Icon;
    private bool _isDragging;
    public bool IsDragging { get => _isDragging; set { _isDragging = value; Changed(); } }
}

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged { add => CommandManager.RequerySuggested += value; remove => CommandManager.RequerySuggested -= value; }
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
}

public sealed class LauncherViewModel : ObservableObject
{
    public const int PageSize = 28;
    private readonly string? _stateFile;
    private readonly string? _dockPreferencesFile;
    private readonly List<string> _recentLaunchKeys;
    public DockSortMode DockSortMode { get; private set; }
    private readonly List<EntryViewModel> _entries = [];
    private IReadOnlyList<CatalogEntry> _poolEntries = [];
    private string _searchText = "";
    private string _status = "正在读取应用…";
    private int _page;
    public IReadOnlyList<EntryViewModel> Entries => _entries;
    public IReadOnlyList<CatalogEntry> PoolEntries => _poolEntries;
    public bool IsCatalogReady { get; private set; }
    public string EmptyMessage => _entries.Count == 0 ? "点击右上角「添加」，从图标池选择应用" : "没有匹配的应用";
    public ObservableCollection<EntryViewModel> VisibleEntries { get; } = [];
    public ObservableCollection<EntryViewModel> DockEntries { get; } = [];
    public string SearchText { get => _searchText; set { _searchText = value; _page = 0; Refresh(); Changed(); } }
    public string Status { get => _status; set { _status = value; Changed(); } }
    public string PageLabel => $"{_page + 1} / {PageCount}";
    public int PageCount => Math.Max(1, (Filtered().Count() + PageSize - 1) / PageSize);
    public bool CanReorder => string.IsNullOrWhiteSpace(SearchText);
    internal Func<CatalogEntry, bool>? LaunchOverride { get; set; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand EscapeCommand { get; set; } = new RelayCommand(() => { });

    public LauncherViewModel(string? stateFile, string? dockPreferencesFile = null)
    {
        _stateFile = stateFile;
        _dockPreferencesFile = dockPreferencesFile;
        var preferences = DockPreferencesStore.Read(dockPreferencesFile);
        DockSortMode = preferences.SortMode;
        _recentLaunchKeys = [.. preferences.RecentLaunchKeys ?? []];
        PreviousPageCommand = new RelayCommand(() => { _page--; Refresh(); }, () => _page > 0);
        NextPageCommand = new RelayCommand(() => { _page++; Refresh(); }, () => _page + 1 < PageCount);
    }

    public async Task InitializeAsync(IReadOnlyList<CatalogEntry> catalog, CancellationToken token = default)
    {
        UpdatePool(catalog);
        var selected = new List<CatalogEntry>();
        bool failed = false;
        if (_stateFile is not null && File.Exists(_stateFile))
        {
            try
            {
                var paths = JsonSerializer.Deserialize<string[]>(await File.ReadAllTextAsync(_stateFile, token)) ?? [];
                var byPath = _poolEntries.SelectMany(entry => entry.SourcePaths.Select(path => (path, entry)))
                    .GroupBy(pair => pair.path, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().entry, StringComparer.OrdinalIgnoreCase);
                var imported = await CatalogService.ImportAsync(paths.Where(path => !byPath.ContainsKey(path)), token);
                foreach (var entry in imported)
                    foreach (string path in entry.SourcePaths) byPath.TryAdd(path, entry);
                foreach (string path in paths)
                    if (byPath.TryGetValue(path, out var entry)) selected.Add(entry);
                UpdatePool(imported);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
            { failed = true; }
        }
        Load(selected);
        IsCatalogReady = true;
        Changed(nameof(IsCatalogReady));
        if (failed) Status = "已添加应用无法读取，请从图标池重新选择";
    }

    public void UpdatePool(IReadOnlyList<CatalogEntry> entries)
    {
        _poolEntries = CatalogService.MergeDuplicates(_poolEntries.Concat(entries));
        Changed(nameof(PoolEntries));
    }

    public void Load(IReadOnlyList<CatalogEntry> entries)
    {
        _entries.Clear();
        _entries.AddRange(entries.Select(entry => new EntryViewModel(entry)));
        bool stateFailed = false;
        if (_stateFile is not null && File.Exists(_stateFile))
        {
            try
            {
                var order = JsonSerializer.Deserialize<string[]>(File.ReadAllText(_stateFile)) ?? [];
                var ranks = order.Select((path, index) => (path, index)).GroupBy(pair => pair.path, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
                var sorted = _entries.OrderBy(entry => entry.Entry.SourcePaths.Min(path => ranks.GetValueOrDefault(path, int.MaxValue))).ToArray();
                _entries.Clear(); _entries.AddRange(sorted);
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            { stateFailed = true; }
        }
        var unique = CatalogService.MergeDuplicates(_entries.Select(entry => entry.Entry));
        _entries.Clear(); _entries.AddRange(unique.Select(entry => new EntryViewModel(entry)));
        Refresh(); RefreshDock();
        Status = stateFailed ? "排序文件无法读取，使用默认顺序" : $"{_entries.Count} 个入口";
    }

    public int Add(IReadOnlyList<CatalogEntry> entries)
    {
        UpdatePool(entries);
        int added = 0;
        foreach (var entry in entries)
        {
            if (_entries.Any(existing => existing.Entry.LaunchKey == entry.LaunchKey
                || existing.Entry.SourcePath.Equals(entry.SourcePath, StringComparison.OrdinalIgnoreCase))) continue;
            _entries.Add(new EntryViewModel(entry)); added++;
        }
        Refresh(); RefreshDock();
        if (Save()) Status = added > 0 ? $"已添加 {added} 个入口" : "这些入口已经存在";
        return added;
    }

    public void Remove(EntryViewModel entry)
    {
        if (!_entries.Remove(entry)) return;
        Refresh(); RefreshDock();
        if (Save()) Status = $"已从面板移除 {entry.Name}";
    }

    public bool MoveVisible(EntryViewModel entry, int target)
    {
        if (!CanReorder || target < 0 || target >= VisibleEntries.Count) return false;
        int source = VisibleEntries.IndexOf(entry);
        if (source < 0 || source == target) return false;
        int sourceIndex = _entries.IndexOf(entry);
        int targetIndex = _entries.IndexOf(VisibleEntries[target]);
        _entries.RemoveAt(sourceIndex); _entries.Insert(targetIndex, entry);
        VisibleEntries.Move(source, target);
        return true;
    }

    public void MoveBy(EntryViewModel entry, int direction)
    {
        if (MoveVisible(entry, VisibleEntries.IndexOf(entry) + direction)) CommitOrder();
    }
    public EntryViewModel[] SnapshotOrder() => _entries.ToArray();
    public void RestoreOrder(IReadOnlyList<EntryViewModel> entries) { _entries.Clear(); _entries.AddRange(entries); Refresh(); }
    public void CommitOrder() { RefreshDock(); Save(); }
    public void Launch(EntryViewModel entry)
    {
        string? error = null;
        bool opened = LaunchOverride is not null ? LaunchOverride(entry.Entry) : CatalogService.Launch(entry.Entry, out error);
        if (!opened) { Status = $"无法打开：{error ?? "启动未完成"}"; return; }
        Status = $"已打开 {entry.Name}";
        string key = entry.Entry.LaunchKey;
        _recentLaunchKeys.Remove(key); _recentLaunchKeys.Insert(0, key);
        if (_recentLaunchKeys.Count > DockPreferencesStore.HistoryLimit)
            _recentLaunchKeys.RemoveRange(DockPreferencesStore.HistoryLimit, _recentLaunchKeys.Count - DockPreferencesStore.HistoryLimit);
        if (DockSortMode == DockSortMode.Recent) RefreshDock();
        SaveDockPreferences();
    }
    public bool SetDockSortMode(DockSortMode mode)
    {
        if (!Enum.IsDefined(mode)) return false;
        DockSortMode = mode; RefreshDock(); Changed(nameof(DockSortMode));
        return SaveDockPreferences();
    }
    private bool SaveDockPreferences()
    {
        if (DockPreferencesStore.Save(_dockPreferencesFile, new(DockSortMode, _recentLaunchKeys.ToArray()))) return true;
        Status = "Dock 排序设置与使用记录保存失败，请检查目录权限"; return false;
    }
    private IEnumerable<EntryViewModel> Filtered() => _entries.Where(entry => entry.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase));
    private void Refresh()
    {
        _page = Math.Clamp(_page, 0, PageCount - 1);
        VisibleEntries.Clear();
        foreach (var entry in Filtered().Skip(_page * PageSize).Take(PageSize)) VisibleEntries.Add(entry);
        Changed(nameof(PageLabel)); Changed(nameof(EmptyMessage)); CommandManager.InvalidateRequerySuggested();
    }
    private void RefreshDock()
    {
        DockEntries.Clear();
        IEnumerable<EntryViewModel> ordered = _entries;
        if (DockSortMode == DockSortMode.Recent)
        {
            var ranks = _recentLaunchKeys.Select((key, index) => (key, index))
                .ToDictionary(pair => pair.key, pair => pair.index, StringComparer.Ordinal);
            ordered = _entries.OrderBy(entry => ranks.GetValueOrDefault(entry.Entry.LaunchKey, int.MaxValue));
        }
        foreach (var entry in ordered.Take(8)) DockEntries.Add(entry);
    }
    private bool Save()
    {
        if (_stateFile is null) return true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_stateFile)!);
            File.WriteAllText(_stateFile + ".tmp", JsonSerializer.Serialize(_entries.Select(entry => entry.Entry.SourcePath)));
            File.Move(_stateFile + ".tmp", _stateFile, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { Status = "排序保存失败，请检查目录权限"; return false; }
    }
}
