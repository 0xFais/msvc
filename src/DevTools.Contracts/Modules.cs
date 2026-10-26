using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace DevTools.Contracts;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected bool Set<T>(ref T storage, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;
        storage = value;
        Raise(name);
        return true;
    }
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public enum FieldKind
{
    Text, Secret, Choice, Toggle, ReadOnly
}
public sealed class ModuleField : ObservableObject
{
    private string value;
    private bool visible = true;
    public bool IsVisible
    {
        get => visible; set => Set(ref visible, value);
    }
    private bool readOnly;
    public bool IsReadOnly
    {
        get => readOnly; set => Set(ref readOnly, value);
    }
    public string Key
    {
        get;
    }
    public string Label
    {
        get;
    }
    public string Hint { get; init; } = "";
    public FieldKind Kind
    {
        get;
    }
    public ObservableCollection<string> Choices { get; } = [];
    public string Value
    {
        get => value; set
        {
            if (Set(ref this.value, value))
                Raise(nameof(IsChecked));
        }
    }
    public bool IsChecked
    {
        get => Value == "true"; set => Value = value ? "true" : "false";
    }
    public ModuleField(string key, string label, string value = "", FieldKind kind = FieldKind.Text)
        => (Key, Label, this.value, Kind) = (key, label, value, kind);
}

public sealed class ModulePage : ObservableObject
{
    private string status = "Ready";
    private bool busy;
    private double progress;
    public string Id
    {
        get;
    }
    public string Title
    {
        get;
    }
    public string Description
    {
        get;
    }
    public string Group
    {
        get;
    }
    public string Icon
    {
        get;
    }
    public string Status
    {
        get => status; set => Set(ref status, value);
    }
    public bool IsBusy
    {
        get => busy; private set
        {
            if (Set(ref busy, value))
                foreach (var a in Actions)
                    a.Notify();
        }
    }
    public double Progress
    {
        get => progress; set => Set(ref progress, value);
    }
    public ObservableCollection<ModuleField> Fields { get; } = [];
    public ObservableCollection<ModuleAction> Actions { get; } = [];
    public IntegrationBrowser? Browser
    {
        get; init;
    }
    public bool HasIntegrationBrowser => Browser is not null;
    public MsvcWorkspace? CompilerWorkspace
    {
        get; init;
    }
    public bool HasCompilerWorkspace => CompilerWorkspace is not null;
    public WindowsWorkspace? WindowsWorkspace { get; init; }
    public bool HasWindowsWorkspace => WindowsWorkspace is not null;
    public ScoopWorkspace? ScoopWorkspace { get; init; }
    public bool HasScoopWorkspace => ScoopWorkspace is not null;
    public FolderWorkspace? FolderWorkspace { get; init; }
    public bool HasFolderWorkspace => FolderWorkspace is not null;
    public StartSearchWorkspace? StartSearchWorkspace { get; init; }
    public bool HasStartSearchWorkspace => StartSearchWorkspace is not null;
    public WindowsPreferencesWorkspace? PreferencesWorkspace { get; init; }
    public bool HasPreferencesWorkspace => PreferencesWorkspace is not null;
    public ModuleField this[string key] => Fields.Single(f => f.Key == key);
    public ModulePage(string id, string title, string description, string group, string icon)
        => (Id, Title, Description, Group, Icon) = (id, title, description, group, icon);
    public ModuleField Add(string key, string label, string value = "", FieldKind kind = FieldKind.Text, string hint = "", params string[] choices)
    {
        var f = new ModuleField(key, label, value, kind) { Hint = hint };
        foreach (var choice in choices)
            f.Choices.Add(choice);
        Fields.Add(f);
        return f;
    }
    public void Action(string title, Func<CancellationToken, Task> execute, IModuleContext context, bool primary = false)
        => Actions.Add(new ModuleAction(title, this, execute, context, primary));
    internal void Busy(bool value) => IsBusy = value;
}

public sealed class ModuleAction(string title, ModulePage page, Func<CancellationToken, Task> execute, IModuleContext context, bool primary) : ObservableObject, ICommand
{
    private string titleValue = title;
    public string Title
    {
        get => titleValue; set => Set(ref titleValue, value);
    }
    private bool visible = true;
    public bool IsVisible
    {
        get => visible; set => Set(ref visible, value);
    }
    public bool IsPrimary { get; } = primary;
    private bool enabled = true;
    public bool IsEnabled
    {
        get => enabled;
        set
        {
            if (Set(ref enabled, value))
                Notify();
        }
    }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => IsEnabled && !page.IsBusy && !context.IsOperationRunning;
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter) || !context.TryBeginOperation())
            return;
        page.Busy(true);
        page.Progress = 0;
        page.Status = Title + "…";
        try
        {
            await execute(context.OperationToken);
            page.Progress = 100;
            page.Status = "Completed · " + Title;
            context.Log(page.Title, Title + " completed.");
        }
        catch (ActionDeclinedException) { page.Status = "Declined · " + Title; context.Log(page.Title, page.Status); }
        catch (OperationCanceledException) { page.Status = "Cancelled · inspect the activity log"; context.Log(page.Title, page.Status); }
        catch (Exception e) { page.Status = "Failed · " + e.Message; context.Log(page.Title, e.Message); await context.ShowReportAsync(Title + " failed", e.ToString()); }
        finally { page.Busy(false); context.EndOperation(); }
    }
    public void Notify() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public interface IDevToolsModule
{
    string Id
    {
        get;
    }
    ModulePage CreatePage(IModuleContext context);
}

public sealed class ActionDeclinedException : OperationCanceledException
{
    public ActionDeclinedException() : base("Action declined by the user.") { }
}

public record ProcessResult(int ExitCode, string Output)
{
    public string? StandardOutput { get; init; }
    public string? StandardError { get; init; }
    public string EnsureSuccess() => ExitCode == 0 ? Output : throw new InvalidOperationException($"Process exited {ExitCode}: {Output}");
    public string EnsureStandardOutputSuccess()
    {
        EnsureSuccess();
        return StandardOutput ?? Output;
    }
}

public interface IAppDataLocations
{
    string SettingsDirectory { get; }
    string LogsDirectory { get; }
}

public interface IModuleContext
{
    string DataDirectory
    {
        get;
    }
    string Get(string key, string fallback = "");
    void Save(string key, string value);
    void Log(string module, string message);
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowReportAsync(string title, string content);
    Task<string?> PickFolderAsync(string title);
    Task<string?> PickFileAsync(string title, string filter);
    Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null);
    void Open(string path);
    bool IsOperationRunning
    {
        get;
    }
    CancellationToken OperationToken
    {
        get;
    }
    bool TryBeginOperation();
    void EndOperation();
}

