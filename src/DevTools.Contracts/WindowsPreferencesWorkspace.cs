using System.Collections.ObjectModel;

namespace DevTools.Contracts;

public sealed class WindowsPreferenceRow : ObservableObject
{
    private bool enabled, initial, available;
    private string state = "Not checked", availability = "Check current settings first.";
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Scope { get; init; } = "";
    public IReadOnlyList<string> Choices { get; init; } = [];
    public bool IsChoice => Choices.Count == 2;
    public bool IsToggle => !IsChoice;
    public event Action? Changed;
    public bool Enabled { get => enabled; set { if (Set(ref enabled, value)) { Raise(nameof(SelectedChoice)); Raise(nameof(ToggleLabel)); Raise(nameof(IsChanged)); Changed?.Invoke(); } } }
    public string ToggleLabel => Enabled ? "On" : "Off";
    public string SelectedChoice { get => IsChoice ? Choices[Enabled ? 1 : 0] : ""; set { if (IsChoice && Choices.Contains(value)) Enabled = value == Choices[1]; } }
    public bool Available { get => available; set => Set(ref available, value); }
    public bool IsChanged => Available && Enabled != initial;
    public string State { get => state; set => Set(ref state, value); }
    public string Availability { get => availability; set => Set(ref availability, value); }
    public void Load(bool value) { initial = value; Enabled = value; Raise(nameof(IsChanged)); Changed?.Invoke(); }
}

public sealed class WindowsPreferencesWorkspace : ObservableObject
{
    private string platform = "Checking this PC...", result = "";
    private bool initialized, hasBackup;
    public ObservableCollection<WindowsPreferenceRow> Rows { get; } = [];
    public bool HasChanges => Rows.Any(r => r.IsChanged);
    public string Pending => HasChanges ? $"{Rows.Count(r => r.IsChanged)} pending change(s)" : IsInitialized ? "No pending changes" : "Settings have not been checked";
    public bool IsInitialized { get => initialized; set => Set(ref initialized, value); }
    public bool HasBackup { get => hasBackup; set => Set(ref hasBackup, value); }
    public string Platform { get => platform; set => Set(ref platform, value); }
    public string Result { get => result; set => Set(ref result, value); }
    public ModuleAction? CheckAction { get; set; }
    public ModuleAction? ApplyAction { get; set; }
    public ModuleAction? RestoreAction { get; set; }
    public ModuleAction? RestartAction { get; set; }
    public ModuleAction? SettingsAction { get; set; }
    public void NotifySelection() { Raise(nameof(HasChanges)); Raise(nameof(Pending)); if (ApplyAction is { } action) action.IsEnabled = IsInitialized && HasChanges; }
}

