using System.Collections.ObjectModel;
using System.Windows.Input;

namespace DevTools.Contracts;

public sealed record ScoopPackage(string Name, string Version, string Bucket, bool Held, string Status,
    string Directory, string ManifestPath, string Description = "", string Homepage = "", string License = "")
{
    public string Identity => Bucket + "/" + Name;
    public string HeldLabel => Held ? "Held" : "";
}
public sealed record ScoopBucket(string Name, int Manifests);
public enum ScoopSetupState { Checking, Missing, PathMissing, Ready, Broken, Installing }
public sealed class ScoopWorkspace : ObservableObject
{
    private ScoopSetupState setupState = ScoopSetupState.Checking;
    private string setupMessage = "Checking Scoop installation…";
    private string installLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop");
    public ScoopSetupState SetupState { get => setupState; set { if (Set(ref setupState, value)) { Raise(nameof(HasInstallation)); Raise(nameof(ShowInstall)); Raise(nameof(NeedsPath)); Raise(nameof(SetupTitle)); } } }
    public string SetupMessage { get => setupMessage; set => Set(ref setupMessage, value); }
    public string InstallLocation { get => installLocation; set => Set(ref installLocation, value); }
    public bool HasInstallation => SetupState is ScoopSetupState.Ready or ScoopSetupState.PathMissing;
    public bool ShowInstall => SetupState == ScoopSetupState.Missing;
    public bool NeedsPath => SetupState == ScoopSetupState.PathMissing;
    public string SetupTitle => SetupState switch { ScoopSetupState.Ready => "✓ Scoop is ready", ScoopSetupState.PathMissing => "Scoop found — PATH setup needed", ScoopSetupState.Missing => "Scoop is not installed", ScoopSetupState.Broken => "Scoop installation needs attention", ScoopSetupState.Installing => "Installing Scoop…", _ => "Checking Scoop…" };
    private string root = "", filter = "", query = "", summary = "Choose Refresh to detect Scoop.", details = "Select a package to inspect its details.", pathEntries = "", version = "";
    private ScoopPackage? selected;
    private int tab;
    private bool running;
    private string operationStatus = "Ready";
    public bool IsRunning { get => running; set => Set(ref running, value); }
    public string OperationStatus { get => operationStatus; set => Set(ref operationStatus, value); }
    public string Root { get => root; set => Set(ref root, value); }
    public string Filter { get => filter; set { if (Set(ref filter, value)) FilterChanged?.Invoke(); } }
    public string Query { get => query; set => Set(ref query, value); }
    public string Summary { get => summary; set => Set(ref summary, value); }
    public string Details { get => details; set => Set(ref details, value); }
    public string PathEntries { get => pathEntries; set => Set(ref pathEntries, value); }
    public string Version { get => version; set => Set(ref version, value); }
    public int Tab { get => tab; set => Set(ref tab, value); }
    public ScoopPackage? Selected { get => selected; set { if (Set(ref selected, value)) SelectionChanged?.Invoke(); } }
    public List<ScoopPackage> Selection { get; } = [];
    public List<ScoopPackage> SearchSelection { get; } = [];
    public List<ScoopBucket> BucketSelection { get; } = [];
    public ObservableCollection<ScoopPackage> Installed { get; } = [];
    public ObservableCollection<ScoopPackage> Results { get; } = [];
    public ObservableCollection<ScoopBucket> Buckets { get; } = [];
    public ObservableCollection<string> Versions { get; } = [];
    public ObservableCollection<ModuleField> CustomFields { get; } = [];
    public Dictionary<string, ICommand> Commands { get; } = [];
    public string BucketName { get; set; } = "";
    public string BucketUrl { get; set; } = "";
    public event Action? FilterChanged;
    public event Action? SelectionChanged;
    public ModuleField Field(string key) => CustomFields.Single(f => f.Key == key);
}

