using System.Diagnostics;
using System.Text.Json;
using DevTools.Contracts;
using DevTools.Infrastructure;
using DevTools.Modules.Development;

namespace DevTools.PortableMsvc;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return await Execute(args, cancel.Token); }
        catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled. Partial downloads are retained; retry the same destination."); return 130; }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            for (var cause = e.InnerException; cause is not null; cause = cause.InnerException) Console.Error.WriteLine(cause.Message);
            return 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    public static async Task<int> Execute(string[] args, CancellationToken ct)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine("""
                MSVC 1.1.0 — native C# compiler/SDK downloader
                msvc list
                msvc use VERSION [--target ARCH]
                msvc versions [--preview] [--catalog-url HTTPS_URL]
                msvc install --accept-license [--root PATH] [--host x64|x86|arm64]
                    [--targets x64,x86,arm64,arm] [--compiler Latest|VERSION] [--sdk Latest|BUILD]
                    [--preview] [--clean] [--update] [--catalog-url HTTPS_URL]
                msvc info [--root PATH] [--host ARCH] [--target ARCH]
                msvc shell [--root PATH] [--host ARCH] [--target ARCH]
                msvc run [--root PATH] [--host ARCH] [--target ARCH] -- COMMAND [ARGS...]
                Microsoft terms: https://visualstudio.microsoft.com/license-terms/
                Scoop installs register a permanent user build environment. Reopen terminals after changes.
                """);
            return 0;
        }
        if (args[0] == "--version") { if (args.Length != 1) throw new ArgumentException("--version takes no arguments."); Console.WriteLine("1.1.0"); return 0; }
        var options = Options.Parse(args);
        var manager = new PermanentEnvironment(new RegistryUserEnvironment(), new EnvironmentStateFile(PermanentEnvironment.DefaultStateFile));
        if (args[0] is "list" or "use" or "register" or "unregister" or "suspend")
        {
            using var lease = PermanentEnvironment.Lock();
            switch (args[0])
            {
                case "list":
                    var listing = manager.Read();
                    foreach (var item in listing.Installed.Values)
                        Console.WriteLine($"{(listing.Active == item.Id ? "*" : " ")} {item.Id} · {item.Host}/{item.Target} · {item.Root}");
                    if (listing.Installed.Count == 0) Console.WriteLine("No registered MSVC toolchains.");
                    break;
                case "use":
                    manager.Use(options.Value("--id", ""), options.Has("--target") ? options.Value("--target", "x64") : null);
                    Console.WriteLine("Active compiler changed. Reopen terminals to use the permanent environment.");
                    break;
                case "register":
                    manager.Register(new(options.Value("--id", ""), Path.GetFullPath(options.Value("--root", Path.Combine(AppContext.BaseDirectory, "toolchain"))), options.Value("--host", "x64"), options.Value("--target", "x64")));
                    Console.WriteLine("Toolchain registered. Permanent environment configured for the active version; reopen terminals.");
                    break;
                case "suspend": manager.Suspend(options.Value("--id", "")); break;
                case "unregister": manager.Unregister(options.Value("--id", "")); Console.WriteLine("Registration removed; owned environment settings restored where unchanged."); break;
            }
            return 0;
        }
        InstalledToolchain? active = null;
        if (args[0] is ("info" or "shell" or "run") && !options.Has("--root"))
        {
            var state = manager.Read();
            if (state.Active is { } id) state.Installed.TryGetValue(id, out active);
        }
        var root = Path.GetFullPath(options.Value("--root", active?.Root ?? Path.Combine(AppContext.BaseDirectory, "toolchain")));
        var host = options.Value("--host", active?.Host ?? "x64");
        var target = options.Value("--target", active?.Target ?? "x64");
        var context = new ConsoleContext();
        var page = new ModulePage("msvc", "MSVC", "", "", "");
        foreach (var pair in new Dictionary<string, string>
        {
            ["root"] = root, ["host"] = host, ["targets"] = options.Value("--targets", "x64"),
            ["compiler"] = options.Value("--compiler", "Latest"), ["sdk"] = options.Value("--sdk", "Latest"),
            ["preview"] = options.Has("--preview") ? "true" : "false", ["clean"] = options.Has("--clean") ? "true" : "false"
        }) page.Add(pair.Key, pair.Key, pair.Value);
        var installer = new MsvcInstaller(context, page, catalogUrl: options.Has("--catalog-url") ? options.Value("--catalog-url", "") : null, catalogSha256: options.Has("--catalog-sha256") ? options.Value("--catalog-sha256", "") : null);
        switch (args[0])
        {
            case "versions":
                Console.WriteLine(JsonSerializer.Serialize(await installer.VersionsAsync(options.Has("--preview"), ct), new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            case "install":
                if (!options.Has("--accept-license")) throw new ArgumentException("Read https://visualstudio.microsoft.com/license-terms/ and pass --accept-license to download Microsoft components.");
                if (!options.Has("--update") && Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
                    throw new IOException("Destination is not empty. Use a new dedicated folder, or --update to resume/update this toolchain.");
                Console.WriteLine("Downloading Microsoft compiler and Windows SDK packages. This can use several GB.");
                await installer.InstallAsync(ct);
                foreach (var arch in page["targets"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    Toolchain.Detect(root, host, arch).Validate();
                Console.WriteLine("Installed and validated: " + root);
                return 0;
            case "info":
                var info = Toolchain.Detect(root, host, target);
                info.Validate();
                Console.WriteLine(JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            case "shell":
            case "run":
                var toolchain = Toolchain.Detect(root, host, target);
                toolchain.Validate();
                var command = args[0] == "shell" ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe") : options.Command[0];
                var process = new ProcessStartInfo(command) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
                foreach (var pair in toolchain.EnvironmentVariables()) process.Environment[pair.Key] = pair.Value;
                foreach (var arg in args[0] == "shell" ? new[] { "/d", "/k" } : options.Command.Skip(1)) process.ArgumentList.Add(arg);
                using (var child = Process.Start(process) ?? throw new IOException("Could not start developer command."))
                {
                    try { await child.WaitForExitAsync(ct); }
                    catch (OperationCanceledException) { try { child.Kill(true); } catch (InvalidOperationException) { } throw; }
                    return child.ExitCode;
                }
            default: throw new ArgumentException("Unknown command. Run msvc --help.");
        }
    }
}

public sealed class Options
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    public string[] Command { get; private set; } = [];
    public bool Has(string key) => values.ContainsKey(key);
    public string Value(string key, string fallback) => values.GetValueOrDefault(key, fallback);
    public static Options Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("versions" or "install" or "info" or "shell" or "run" or "list" or "use" or "register" or "unregister" or "suspend")) throw new ArgumentException("Unknown command.");
        var result = new Options();
        var allowed = args[0] switch
        {
            "versions" => new[] { "--preview", "--catalog-url", "--catalog-sha256" },
            "list" => [],
            "use" => ["--target"],
            "register" => ["--id", "--root", "--host", "--target"],
            "unregister" or "suspend" => ["--id"],
            "install" => ["--root", "--host", "--targets", "--compiler", "--sdk", "--preview", "--clean", "--update", "--accept-license", "--catalog-url", "--catalog-sha256"],
            _ => ["--root", "--host", "--target"]
        };
        for (var i = 1; i < args.Length; i++)
        {
            var key = args[i];
            if (args[0] == "use" && i == 1 && !key.StartsWith("--"))
            {
                result.values["--id"] = key == "latest" ? "msvc" : key.StartsWith("msvc", StringComparison.Ordinal) ? key : "msvc-" + key;
                continue;
            }
            if (key == "--" && args[0] == "run") { result.Command = args[(i + 1)..]; break; }
            if (!allowed.Contains(key) || result.Has(key)) throw new ArgumentException("Unknown or repeated option: " + key);
            if (key is "--preview" or "--clean" or "--update" or "--accept-license") result.values.Add(key, "true");
            else
            {
                if (++i >= args.Length || args[i].StartsWith("--") || string.IsNullOrWhiteSpace(args[i])) throw new ArgumentException("Missing value for " + key);
                result.values.Add(key, args[i]);
            }
        }
        if (!new[] { "x64", "x86", "arm64" }.Contains(result.Value("--host", "x64"))) throw new ArgumentException("Unsupported host architecture.");
        if (!new[] { "x64", "x86", "arm64", "arm" }.Contains(result.Value("--target", "x64"))) throw new ArgumentException("Unsupported target architecture.");
        var targets = result.Value("--targets", "x64").Split(',', StringSplitOptions.TrimEntries);
        if (targets.Any(t => !new[] { "x64", "x86", "arm64", "arm" }.Contains(t))) throw new ArgumentException("Unsupported target list.");
        if (args[0] is "use" or "register" or "unregister" or "suspend")
            if (!System.Text.RegularExpressions.Regex.IsMatch(result.Value("--id", ""), @"^msvc(?:-\d+\.\d+)?$")) throw new ArgumentException("Choose an installed package ID or compiler family, for example: use 14.44.");
        if (result.Has("--catalog-url") && (!Uri.TryCreate(result.Value("--catalog-url", ""), UriKind.Absolute, out var uri) || uri.Scheme != "https")) throw new ArgumentException("Catalog URL must use HTTPS.");
        if (result.Has("--catalog-sha256") && (!result.Has("--catalog-url") || !System.Text.RegularExpressions.Regex.IsMatch(result.Value("--catalog-sha256", ""), "^[0-9a-fA-F]{64}$"))) throw new ArgumentException("A catalog SHA256 requires an HTTPS catalog URL and 64 hexadecimal characters.");
        if (args[0] == "run" && result.Command.Length == 0) throw new ArgumentException("Use run -- COMMAND [ARGS...].");
        return result;
    }
}

internal sealed class ConsoleContext : IModuleContext
{
    public string DataDirectory => AppContext.BaseDirectory;
    public bool IsOperationRunning => false;
    public CancellationToken OperationToken => CancellationToken.None;
    public string Get(string key, string fallback = "") => fallback;
    public void Save(string key, string value) { }
    public void Log(string module, string message) => Console.WriteLine(message);
    public Task<bool> ConfirmAsync(string title, string message) => throw new NotSupportedException();
    public Task ShowReportAsync(string title, string content) => throw new NotSupportedException();
    public Task<string?> PickFolderAsync(string title) => throw new NotSupportedException();
    public Task<string?> PickFileAsync(string title, string filter) => throw new NotSupportedException();
    public Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken, string? workingDirectory = null, IReadOnlyDictionary<string, string>? environment = null) => ProcessRunner.RunAsync(executable, arguments, cancellationToken, workingDirectory, environment);
    public void Open(string path) => throw new NotSupportedException();
    public bool TryBeginOperation() => true;
    public void EndOperation() { }
}


