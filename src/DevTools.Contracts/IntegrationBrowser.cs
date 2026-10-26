using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DevTools.Contracts;

public sealed record IntegrationItem(string Id, string Name, string ExecutablePath,
    bool IsAdded, bool ExecutableExists, string Locations, bool CanEdit = true,
    bool IsCustom = false, string Notice = "")
{
    public string Status => IsAdded ? (ExecutableExists ? "Added" : "Added · missing app") : "Available";
    public string Symbol => new string(Name.Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant();
}

/// <summary>UI-neutral list/inspector contract shared by integration modules and WPF.</summary>
public sealed class IntegrationBrowser : ObservableObject
{
    private IntegrationItem? selected;
    private bool customEditor;
    private bool scanned;
    private string notice = "";
    private string searchText = "";
    private readonly List<IntegrationItem> allItems = [];
    public bool SearchIncludesLocations
    {
        get; init;
    }
    public string SearchPlaceholder => SearchIncludesLocations ? "Search apps or protocols…" : "Search applications…";
    public string SearchText
    {
        get => searchText;
        set
        {
            if (!Set(ref searchText, value ?? ""))
                return;
            ApplyFilter(Selected?.Id);
            Raise(nameof(HasSearch));
        }
    }
    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);
    public ICommand ClearSearchCommand
    {
        get;
    }
    public IntegrationBrowser() => ClearSearchCommand = new ClearSearchAction(this);
    public ObservableCollection<IntegrationItem> Items { get; } = [];
    public ObservableCollection<ModuleField> InspectorFields { get; } = [];
    public ObservableCollection<ModuleField> ExtraFields { get; } = [];
    public ObservableCollection<ModuleAction> ToolbarActions { get; } = [];
    public ObservableCollection<ModuleAction> InspectorActions { get; } = [];
    public ObservableCollection<ModuleAction> ExtraActions { get; } = [];
    public ModuleAction? BrowseAction
    {
        get; set;
    }
    public List<string> CatalogNames { get; } = [];
    public event Action? SelectionChanged;
    public IntegrationItem? Selected
    {
        get => selected;
        set
        {
            if (!Set(ref selected, value))
                return;
            if (value is not null)
                IsCustomEditor = false;
            RaiseInspector();
            SelectionChanged?.Invoke();
        }
    }
    public bool IsCustomEditor
    {
        get => customEditor;
        set
        {
            if (Set(ref customEditor, value))
                RaiseInspector();
        }
    }
    public bool HasInspector => Selected is not null || IsCustomEditor;
    public bool CanEdit => IsCustomEditor || Selected?.CanEdit == true;
    public string InspectorTitle => IsCustomEditor ? "Add custom application" : Selected?.Name ?? "Choose an application";
    public string InspectorSubtitle => IsCustomEditor ? "Create a profile, then choose whether to register it." : Selected?.Status ?? "Select an item from the list, or scan for installed apps.";
    public string Notice
    {
        get => notice; set => Set(ref notice, value);
    }
    public bool HasScanned
    {
        get => scanned; set
        {
            if (Set(ref scanned, value))
                Raise(nameof(DiscoveryHint));
        }
    }
    public string DiscoveryHint => HasScanned ? "Scan complete · apps are added only when you choose." : "Scan to find other installed apps you can add.";
    public string Summary => (HasSearch ? $"{Items.Count} of {allItems.Count} shown · " : "") + $"{Items.Count(i => i.IsAdded)} added · {Items.Count(i => !i.IsAdded)} available";
    public bool IsEmpty => Items.Count == 0;
    public string EmptyTitle => HasSearch ? "No matching applications" : "No added applications found";
    public string EmptyHint => HasSearch ? "Try another search or clear it to show the full list." : "Scan installed apps to find supported applications, or add a custom profile.";
    public void ReplaceItems(IEnumerable<IntegrationItem> items, string? preferredId = null)
    {
        var id = preferredId ?? Selected?.Id;
        var snapshot = items.OrderByDescending(i => i.IsAdded).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        allItems.Clear();
        allItems.AddRange(snapshot);
        ApplyFilter(id);
    }
    private void ApplyFilter(string? preferredId)
    {
        var terms = SearchText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = allItems.Where(item => terms.All(term => item.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                     || (SearchIncludesLocations && item.Locations.Contains(term, StringComparison.OrdinalIgnoreCase)))).ToArray();
        // Keep matching rows in place so typing does not reset an inspector's unsaved edits.
        for (var i = Items.Count - 1; i >= 0; i--)
            if (!matches.Contains(Items[i]))
                Items.RemoveAt(i);
        for (var i = 0; i < matches.Length; i++)
        {
            var existing = Items.IndexOf(matches[i]);
            if (existing < 0)
                Items.Insert(i, matches[i]);
            else if (existing != i)
                Items.Move(existing, i);
        }
        if (!IsCustomEditor)
            Selected = Items.FirstOrDefault(i => i.Id == preferredId) ?? Items.FirstOrDefault();
        Raise(nameof(Summary));
        Raise(nameof(IsEmpty));
        Raise(nameof(EmptyTitle));
        Raise(nameof(EmptyHint));
        RaiseInspector();
    }
    private void RaiseInspector()
    {
        Raise(nameof(HasInspector));
        Raise(nameof(CanEdit));
        Raise(nameof(InspectorTitle));
        Raise(nameof(InspectorSubtitle));
    }
    private sealed class ClearSearchAction(IntegrationBrowser owner) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => owner.SearchText = "";
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}

