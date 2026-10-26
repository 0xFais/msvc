namespace DevTools.Contracts;

public sealed class StartSearchWorkspace : ObservableObject
{
    private bool bing, recommendations, initialBing, initialRecommendations, initialized, canBing, canRecommendations, hasBackup;
    private string platform = "Check current settings to read this PC.", bingState = "Not checked", recommendationsState = "Not checked", bingHint = "", recommendationsHint = "", result = "";
    public event Action? SelectionChanged;
    public bool DisableBing { get => bing; set { if (Set(ref bing, value)) Changed(); } }
    public bool DisableRecommendations { get => recommendations; set { if (Set(ref recommendations, value)) Changed(); } }
    public bool BingChanged => initialized && canBing && bing != initialBing;
    public bool RecommendationsChanged => initialized && canRecommendations && recommendations != initialRecommendations;
    public bool HasChanges => BingChanged || RecommendationsChanged;
    public string Pending => HasChanges ? $"{(BingChanged ? 1 : 0) + (RecommendationsChanged ? 1 : 0)} pending change(s)" : initialized ? "No pending changes" : "Settings have not been checked";
    public bool IsInitialized { get => initialized; set => Set(ref initialized, value); }
    public bool CanChangeBing { get => canBing; set => Set(ref canBing, value); }
    public bool CanChangeRecommendations { get => canRecommendations; set => Set(ref canRecommendations, value); }
    public bool HasBackup { get => hasBackup; set => Set(ref hasBackup, value); }
    public string Platform { get => platform; set => Set(ref platform, value); }
    public string BingState { get => bingState; set => Set(ref bingState, value); }
    public string RecommendationsState { get => recommendationsState; set => Set(ref recommendationsState, value); }
    public string BingHint { get => bingHint; set => Set(ref bingHint, value); }
    public string RecommendationsHint { get => recommendationsHint; set => Set(ref recommendationsHint, value); }
    public string Result { get => result; set => Set(ref result, value); }
    public ModuleAction? CheckAction { get; set; }
    public ModuleAction? ApplyAction { get; set; }
    public ModuleAction? RestoreAction { get; set; }
    public ModuleAction? SettingsAction { get; set; }
    public ModuleAction? RestartAction { get; set; }
    public void SetCurrent(bool bingDisabled, bool recommendationsDisabled)
    {
        initialBing = bingDisabled; initialRecommendations = recommendationsDisabled;
        DisableBing = bingDisabled; DisableRecommendations = recommendationsDisabled;
        IsInitialized = true;
        Changed();
    }
    private void Changed() { Raise(nameof(BingChanged)); Raise(nameof(RecommendationsChanged)); Raise(nameof(HasChanges)); Raise(nameof(Pending)); SelectionChanged?.Invoke(); }
}

