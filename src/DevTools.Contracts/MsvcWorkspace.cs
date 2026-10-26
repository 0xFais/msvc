using System.Collections.ObjectModel;

namespace DevTools.Contracts;

/// <summary>Presentation state for the status-first, compact compiler workspace.</summary>
public sealed class MsvcWorkspace : ObservableObject
{
    private string heading = "Checking your compiler…";
    private string description = "";
    private string pathStatus = "Checking";
    private string versions = "No toolchain selected";
    private bool onPath;
    public string Heading
    {
        get => heading; set => Set(ref heading, value);
    }
    public string Description
    {
        get => description; set => Set(ref description, value);
    }
    public string PathStatus
    {
        get => pathStatus; set => Set(ref pathStatus, value);
    }
    public string Versions
    {
        get => versions; set => Set(ref versions, value);
    }
    public bool IsOnPath
    {
        get => onPath; set => Set(ref onPath, value);
    }
    public ModuleAction? SetupAction
    {
        get; set;
    }
    public ModuleAction? DiagnoseAction
    {
        get; set;
    }
    public ModuleAction? RefreshAction
    {
        get; set;
    }
    public ObservableCollection<ModuleAction> ExistingActions { get; } = [];
    public ObservableCollection<ModuleAction> DownloadActions { get; } = [];
    public ObservableCollection<ModuleAction> ToolActions { get; } = [];
    public ObservableCollection<ModuleField> AdvancedFields { get; } = [];
}

