using System.Collections.ObjectModel;

namespace DevTools.Contracts;

public sealed record FolderItem(string Id, string Name, string CurrentPath, string DefaultPath, string Symbol, bool IsCustom = false, bool CloudManaged = false)
{
    public string Kind => IsCustom ? "Custom folder" : "Windows folder";
    public string Status => CloudManaged ? "Cloud-managed" : IsCustom ? "Custom folder" : CurrentPath.TrimEnd('\\').Equals(DefaultPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ? "Default location" : "Custom location";
}
public sealed class FolderWorkspace : ObservableObject
{
    private FolderItem? selected;
    private string search = "", target = "", customName = "", customSource = "", summary = "", notice = "Select a folder to manage its location.";
    private bool customEditor, move;
    public ObservableCollection<FolderItem> Items { get; } = [];
    public Dictionary<string, ModuleAction> Commands { get; } = [];
    public FolderItem? Selected { get => selected; set { if (Set(ref selected, value)) { RaiseInspector(); SelectionChanged?.Invoke(); } } }
    public string Search { get => search; set { if (Set(ref search, value)) SearchChanged?.Invoke(); } }
    public string Target { get => target; set => Set(ref target, value); }
    public string CustomName { get => customName; set => Set(ref customName, value); }
    public string CustomSource { get => customSource; set => Set(ref customSource, value); }
    public string Summary { get => summary; set => Set(ref summary, value); }
    public string Notice { get => notice; set => Set(ref notice, value); }
    public bool IsCustomEditor { get => customEditor; set { if (Set(ref customEditor, value)) RaiseInspector(); } }
    public bool MoveFiles { get => move; set => Set(ref move, value); }
    public bool HasInspector => Selected is not null || IsCustomEditor;
    public bool IsWindowsFolder => Selected is { IsCustom: false } && !IsCustomEditor;
    public bool IsCustomFolder => Selected is { IsCustom: true } && !IsCustomEditor;
    public bool CanChange => Selected is { CloudManaged: false } && !IsCustomEditor;
    public string InspectorTitle => IsCustomEditor ? "Add custom folder" : Selected?.Name ?? "Choose a folder";
    public string CurrentPath => Selected?.CurrentPath ?? "";
    public string Kind => IsCustomEditor ? "CUSTOM FOLDER" : Selected?.Kind ?? "";
    public event Action? SelectionChanged;
    public event Action? SearchChanged;
    private void RaiseInspector() { foreach (var property in new[] { nameof(HasInspector), nameof(IsWindowsFolder), nameof(IsCustomFolder), nameof(CanChange), nameof(InspectorTitle), nameof(CurrentPath), nameof(Kind) }) Raise(property); }
}

