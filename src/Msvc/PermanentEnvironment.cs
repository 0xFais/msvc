using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using DevTools.Infrastructure;
using DevTools.Modules.Development;

namespace DevTools.PortableMsvc;

public sealed record SavedValue(string Text, bool Expand = false);
public sealed record OwnedValue(SavedValue? Previous, SavedValue Written);
public sealed record InstalledToolchain(string Id, string Root, string Host, string Target);
public sealed class EnvironmentState
{
    public int Format { get; set; } = 1;
    public string? Active { get; set; }
    public Dictionary<string, InstalledToolchain> Installed { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> OwnedPaths { get; set; } = [];
    public bool PathWasMissing { get; set; }
    public Dictionary<string, OwnedValue> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
public interface IUserEnvironment
{
    SavedValue? Read(string name);
    void Write(string name, SavedValue? value);
    void Broadcast();
}
public interface IEnvironmentStateStore
{
    EnvironmentState Load();
    void Save(EnvironmentState state);
}

public sealed class RegistryUserEnvironment : IUserEnvironment
{
    public SavedValue? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey("Environment");
        return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string text
            ? new(text, key.GetValueKind(name) == RegistryValueKind.ExpandString) : null;
    }
    public void Write(string name, SavedValue? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey("Environment");
        if (value is null) key.DeleteValue(name, false);
        else key.SetValue(name, value.Text, value.Expand ? RegistryValueKind.ExpandString : RegistryValueKind.String);
    }
    public void Broadcast() => WindowsEnvironment.Broadcast();
}

public sealed class EnvironmentStateFile(string file) : IEnvironmentStateStore
{
    public EnvironmentState Load()
    {
        if (!File.Exists(file)) return new();
        var state = JsonSerializer.Deserialize<EnvironmentState>(File.ReadAllText(file)) ?? throw new InvalidDataException("Empty MSVC environment backup.");
        if (state.Format != 1) throw new InvalidDataException("Unsupported MSVC environment backup version.");
        state.Installed = new(state.Installed, StringComparer.OrdinalIgnoreCase);
        state.Variables = new(state.Variables, StringComparer.OrdinalIgnoreCase);
        return state;
    }
    public void Save(EnvironmentState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var staging = file + ".tmp";
        using (var stream = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(true);
        }
        File.Move(staging, file, true);
    }
}

/// <summary>Tracks one active compiler and restores only settings still owned by it.</summary>
public sealed class PermanentEnvironment(IUserEnvironment environment, IEnvironmentStateStore store, Func<InstalledToolchain, Toolchain>? resolve = null)
{
    private static readonly string[] Names = ["VCToolsInstallDir", "WindowsSdkDir", "WindowsSDKVersion", "INCLUDE", "LIB"];
    private Toolchain ToolchainFor(InstalledToolchain item)
    {
        if (item.Root.IndexOfAny([';', '\r', '\n']) >= 0) throw new ArgumentException("Choose an MSVC directory without PATH separators or line breaks.");
        var toolchain = resolve?.Invoke(item) ?? Toolchain.Detect(item.Root, item.Host, item.Target);
        toolchain.Validate();
        return toolchain;
    }
    public EnvironmentState Read() => store.Load();
    public void Register(InstalledToolchain item)
    {
        _ = ToolchainFor(item);
        Change(state =>
        {
            state.Installed[item.Id] = item;
            if (state.Active is null || state.Active.Equals(item.Id, StringComparison.OrdinalIgnoreCase)) Apply(state, item);
        });
    }
    public void Use(string id, string? target = null)
    {
        Change(state =>
        {
            var item = state.Installed.Values.FirstOrDefault(v => v.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("Version is not registered. Run msvc list.");
            if (target is not null) { item = item with { Target = target }; state.Installed[item.Id] = item; }
            Apply(state, item);
        });
    }
    public void Suspend(string id) => Change(state =>
    {
        if (string.Equals(state.Active, id, StringComparison.OrdinalIgnoreCase)) Restore(state);
    });
    public void Unregister(string id) => Change(state =>
    {
        state.Installed.Remove(id);
        if (string.Equals(state.Active, id, StringComparison.OrdinalIgnoreCase)) { Restore(state); state.Active = null; }
    });
    private void Apply(EnvironmentState state, InstalledToolchain item)
    {
        var toolchain = ToolchainFor(item);
        var desired = toolchain.EnvironmentVariables();
        var paths = new[] { Path.GetDirectoryName(toolchain.Compiler)!, Path.Combine(toolchain.SdkRoot, "bin", toolchain.SdkVersion, toolchain.Host) };
        var currentPath = environment.Read("Path");
        if (state.Variables.Count == 0 && state.OwnedPaths.Count == 0) state.PathWasMissing = currentPath is null;
        var kept = RemovePaths(currentPath?.Text ?? "", state.OwnedPaths);
        var entries = kept.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        state.OwnedPaths = paths.Where(path => !entries.Any(entry => SamePath(entry, path))).ToList();
        var nextPath = string.Join(';', state.OwnedPaths.Concat(entries));
        foreach (var name in Names)
        {
            var previous = state.Variables.TryGetValue(name, out var owned) && environment.Read(name) == owned.Written ? owned.Previous : environment.Read(name);
            state.Variables[name] = new(previous, new(desired[name]));
        }
        state.Active = item.Id;
        store.Save(state); // durable original values before any environment writes
        environment.Write("Path", new(nextPath, currentPath?.Expand ?? true));
        foreach (var name in Names) environment.Write(name, state.Variables[name].Written);
    }
    private void Restore(EnvironmentState state)
    {
        var path = environment.Read("Path");
        if (state.OwnedPaths.Count > 0 && path is not null)
        {
            var restoredPath = RemovePaths(path.Text, state.OwnedPaths);
            environment.Write("Path", state.PathWasMissing && restoredPath.Length == 0 ? null : new(restoredPath, path.Expand));
        }
        foreach (var (name, owned) in state.Variables)
            if (Names.Contains(name) && environment.Read(name) == owned.Written) environment.Write(name, owned.Previous);
        state.OwnedPaths.Clear();
        state.Variables.Clear();
    }
    private void Change(Action<EnvironmentState> action)
    {
        var state = store.Load();
        var previousState = JsonSerializer.Deserialize<EnvironmentState>(JsonSerializer.Serialize(state))!;
        var before = Names.Append("Path").ToDictionary(name => name, environment.Read);
        try { action(state); store.Save(state); }
        catch
        {
            // The durable backup is left intact if rollback itself cannot complete.
            foreach (var (name, value) in before) environment.Write(name, value);
            store.Save(previousState);
            environment.Broadcast();
            throw;
        }
        environment.Broadcast();
    }
    private static string RemovePaths(string path, IEnumerable<string> paths) => string.Join(';', path.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(entry => !paths.Any(owned => SamePath(entry, owned))));
    private static bool SamePath(string a, string b) => a.Trim().Trim('"').TrimEnd('\\', '/').Equals(b.Trim().Trim('"').TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    public static string DefaultStateFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DevToolsStudio", "PortableMsvc", "environment.json");
    public static IDisposable Lock()
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DefaultStateFile.ToUpperInvariant())));
        var mutex = new Mutex(false, "Local\\DevTools-PortableMsvc-" + name);
        try { if (!mutex.WaitOne(TimeSpan.FromSeconds(30))) throw new IOException("Another MSVC environment operation is running."); }
        catch (AbandonedMutexException) { }
        catch { mutex.Dispose(); throw; }
        return new MutexLease(mutex);
    }
    private sealed class MutexLease(Mutex mutex) : IDisposable { public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); } }
}


