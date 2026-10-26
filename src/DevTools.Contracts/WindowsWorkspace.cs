namespace DevTools.Contracts;

public sealed class WindowsWorkspace : ObservableObject
{
    public string Kind { get; init; } = "Updates";
    private string heading = "Reading Windows status…", description = "", badge = "Checking", serviceState = "Unknown", startup = "Unknown", health = "Not checked", resume = "No automatic resume", edition = "Unknown edition", publicKey = "", keyNote = "", license = "Not checked";
    private bool positive, initialized;
    public string Heading { get => heading; set => Set(ref heading, value); }
    public string Description { get => description; set => Set(ref description, value); }
    public string Badge { get => badge; set => Set(ref badge, value); }
    public string ServiceState { get => serviceState; set => Set(ref serviceState, value); }
    public string Startup { get => startup; set => Set(ref startup, value); }
    public string Health { get => health; set => Set(ref health, value); }
    public string Resume { get => resume; set => Set(ref resume, value); }
    public string Edition { get => edition; set => Set(ref edition, value); }
    public string PublicKey { get => publicKey; set { if (Set(ref publicKey, value)) Raise(nameof(HasPublicKey)); } }
    public bool HasPublicKey => PublicKey.Length > 0;
    public string KeyNote { get => keyNote; set => Set(ref keyNote, value); }
    public string License { get => license; set => Set(ref license, value); }
    public bool IsPositive { get => positive; set => Set(ref positive, value); }
    public bool IsInitialized { get => initialized; set => Set(ref initialized, value); }
    public ModuleAction? RefreshAction { get; set; }
    public ModuleAction? ResumeAction { get; set; }
    public ModuleAction? HealthAction { get; set; }
    public ModuleAction? PauseAction { get; set; }
    public ModuleAction? DisableAction { get; set; }
    public ModuleAction? DetailsAction { get; set; }
    public ModuleAction? SettingsAction { get; set; }
    public ModuleAction? ActivateAction { get; set; }
}

