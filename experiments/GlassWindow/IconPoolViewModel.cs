using System.Collections.ObjectModel;
using System.Windows.Media;

namespace ClearDesk.GlassWindow;

public sealed class PoolEntryViewModel(CatalogEntry entry, bool isAdded, Action selectionChanged) : ObservableObject
{
    public CatalogEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public ImageSource? Icon => Entry.Icon;
    public bool IsAdded { get; } = isAdded;
    public bool CanSelect => !IsAdded;
    public string SelectionHint => IsAdded ? "已添加" : IsSelected ? "已选择" : "选择";
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (!CanSelect || value == _isSelected) return;
            _isSelected = value;
            Changed(); Changed(nameof(SelectionHint)); selectionChanged();
        }
    }
}

public sealed class IconPoolViewModel : ObservableObject
{
    private readonly LauncherViewModel _launcher;
    private List<PoolEntryViewModel> _entries = [];
    private string _searchText = "";
    private string _status = "";
    public ObservableCollection<PoolEntryViewModel> VisibleEntries { get; } = [];
    public string SearchText { get => _searchText; set { _searchText = value; Filter(); Changed(); } }
    public string Status { get => _status; set { _status = value; Changed(); } }
    public int SelectedCount => _entries.Count(entry => entry.IsSelected);
    public bool CanAdd => SelectedCount > 0;
    public string SelectionLabel => $"已选择 {SelectedCount} 个";
    public string PoolLabel => $"图标池共 {_entries.Count} 个应用";

    public IconPoolViewModel(LauncherViewModel launcher) { _launcher = launcher; Refresh(); }

    public void Refresh()
    {
        var selected = _entries.Where(entry => entry.IsSelected).Select(entry => entry.Entry.LaunchKey).ToHashSet(StringComparer.Ordinal);
        var added = _launcher.Entries.Select(entry => entry.Entry.LaunchKey).ToHashSet(StringComparer.Ordinal);
        _entries = _launcher.PoolEntries.Select(entry =>
        {
            var item = new PoolEntryViewModel(entry, added.Contains(entry.LaunchKey), SelectionChanged);
            item.IsSelected = selected.Contains(entry.LaunchKey);
            return item;
        }).ToList();
        Filter(); SelectionChanged(); Changed(nameof(PoolLabel));
    }

    public void SelectEntries(IReadOnlyList<CatalogEntry> entries)
    {
        Refresh();
        var keys = entries.Select(entry => entry.LaunchKey).ToHashSet(StringComparer.Ordinal);
        foreach (var item in _entries.Where(item => keys.Contains(item.Entry.LaunchKey))) item.IsSelected = true;
    }

    public bool AddSelected()
    {
        if (!CanAdd) return false;
        _launcher.Add(_entries.Where(entry => entry.IsSelected).Select(entry => entry.Entry).ToArray());
        Refresh();
        Status = _launcher.Status;
        return !_launcher.Status.Contains("保存失败", StringComparison.Ordinal);
    }

    private void SelectionChanged() { Changed(nameof(SelectedCount)); Changed(nameof(CanAdd)); Changed(nameof(SelectionLabel)); }
    private void Filter()
    {
        VisibleEntries.Clear();
        foreach (var entry in _entries.Where(entry => entry.Name.Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)))
            VisibleEntries.Add(entry);
    }
}
