using DevTools.Contracts;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevTools.Modules.Development;

public sealed record AvailableVersions(Dictionary<string, string> Compiler, Dictionary<string, string> Sdk);
public sealed class MsvcInstaller(IModuleContext context, ModulePage page, string destinationKey = "root", string? catalogUrl = null, string? catalogSha256 = null)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromHours(1) };
    private Dictionary<string, List<JsonElement>> packages = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> downloaded = [];
    private string Cache => Path.Combine(Path.GetFullPath(page[destinationKey].Value), ".devtools-cache");
    public static AvailableVersions CollectVersions(IEnumerable<string> ids)
    {
        var compiler = new Dictionary<string, string>();
        var sdk = new Dictionary<string, string>();
        foreach (var id in ids)
        {
            var m = Regex.Match(id, @"^Microsoft\.VisualStudio\.Component\.VC\.(\d+(?:\.\d+)+)\.x86\.x64$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var full = m.Groups[1].Value;
                var parts = full.Split('.');
                var key = string.Join('.', parts.Take(2));
                if (!compiler.TryGetValue(key, out var previous) || Version.Parse(full) > Version.Parse(previous))
                    compiler[key] = full;
            }
            var s = Regex.Match(id, @"^Microsoft\.VisualStudio\.Component\.Windows1[01]SDK\.(\d+)$", RegexOptions.IgnoreCase);
            if (s.Success)
                sdk[s.Groups[1].Value] = id;
        }
        return new(compiler, sdk);
    }
    private static IEnumerable<JsonElement> Objects(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("id", out _))
                yield return element;
            foreach (var property in element.EnumerateObject())
                foreach (var item in Objects(property.Value))
                    yield return item;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
                foreach (var item in Objects(child))
                    yield return item;
    }
    private static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var value) ? value.ToString() : "";
    private static IEnumerable<JsonElement> Payloads(JsonElement e) => e.TryGetProperty("payloads", out var payloads) ? payloads.EnumerateArray() : [];
    private JsonElement Package(string id) => packages.TryGetValue(id, out var values) ? values.FirstOrDefault(e => Text(e, "language") is "" or "en-US" or "neutral") : default;
    public async Task<AvailableVersions> VersionsAsync(bool preview, CancellationToken ct)
    {
        page.Status = "Downloading Microsoft package catalog…";
        string url;
        if (catalogUrl is not null) url = catalogUrl;
        else
        {
            var channel = await Http.GetStringAsync(preview ? "https://aka.ms/vs/insiders/channel" : "https://aka.ms/vs/stable/channel", ct);
            using var doc = JsonDocument.Parse(channel);
            var manifest = Objects(doc.RootElement).FirstOrDefault(e => Text(e, "id").Equals(preview ? "Microsoft.VisualStudio.Manifests.VisualStudioPreview" : "Microsoft.VisualStudio.Manifests.VisualStudio", StringComparison.OrdinalIgnoreCase));
            if (manifest.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("The Microsoft channel has no supported package manifest.");
            url = Text(Payloads(manifest).First(), "url");
        }
        RequireHttps(url);
        var catalogBytes = await Http.GetByteArrayAsync(url, ct);
        var json = DecodeCatalog(catalogBytes, catalogSha256);
        using var catalog = JsonDocument.Parse(json);
        packages = Objects(catalog.RootElement).Where(e => Text(e, "id") != "").GroupBy(e => Text(e, "id"), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Select(e => e.Clone()).ToList(), StringComparer.OrdinalIgnoreCase);
        var versions = CollectVersions(packages.Keys);
        if (versions.Compiler.Count == 0 || versions.Sdk.Count == 0)
            throw new InvalidDataException("No supported compiler or SDK versions found. Microsoft may have changed the catalog format.");
        return versions;
    }
    public static string DecodeCatalog(byte[] bytes, string? expectedSha256)
    {
        if (expectedSha256 is not null && (!Regex.IsMatch(expectedSha256, "^[0-9a-fA-F]{64}$") || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Microsoft catalog SHA256 mismatch. No compiler payloads were downloaded.");
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
    private static void RequireHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidDataException("Only HTTPS package URLs are accepted.");
    }
    public static IEnumerable<string> CompilerPackages(string version, string host, IEnumerable<string> targets)
    {
        var v = "microsoft.vc." + version;
        foreach (var s in new[] { "microsoft.visualcpp.dia.sdk", v + ".crt.headers.base", v + ".crt.source.base", v + ".asan.headers.base", v + ".premium.tools." + host + ".base.base", v + ".pgo.headers.base" })
            yield return s;
        foreach (var t in targets)
        {
            foreach (var s in new[] { ".tools.host" + host + ".target" + t + ".base", ".tools.host" + host + ".target" + t + ".res.base", ".crt." + t + ".desktop.base", ".crt." + t + ".store.base", ".crt.redist." + t + ".base", ".premium.tools.host" + host + ".target" + t + ".base", ".pgo." + t + ".base" })
                yield return v + s;
            if (t is "x86" or "x64")
                yield return v + ".asan." + t + ".base";
        }
    }
    private async Task<string> Download(JsonElement payload, string name, CancellationToken ct)
    {
        var hash = Text(payload, "sha256");
        if (!Regex.IsMatch(hash, "^[0-9a-fA-F]{64}$"))
            throw new InvalidDataException("Missing SHA256 for " + name);
        RequireHttps(Text(payload, "url"));
        Directory.CreateDirectory(Cache);
        var file = Path.Combine(Cache, hash[..12] + "_" + Path.GetFileName(name.Replace('\\', '/')));
        downloaded.Add(file);
        if (File.Exists(file) && await Hash(file, ct) == hash.ToLowerInvariant())
            return file;
        var partial = file + ".partial";
        page.Status = "Downloading " + name;
        context.Log(page.Title, page.Status);
        using (var response = await Http.GetAsync(Text(payload, "url"), HttpCompletionOption.ResponseHeadersRead, ct))
        {
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            await using var output = File.Create(partial);
            await stream.CopyToAsync(output, ct);
        }
        if (await Hash(partial, ct) != hash.ToLowerInvariant())
        {
            File.Delete(partial);
            throw new InvalidDataException("SHA256 mismatch for " + name);
        }
        File.Move(partial, file, true);
        return file;
    }
    private static async Task<string> Hash(string file, CancellationToken ct)
    {
        await using var input = File.OpenRead(file);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant();
    }
    public static string SafeArchivePath(string destination, string entry)
    {
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, entry.Replace('/', Path.DirectorySeparatorChar)));
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? path : throw new InvalidDataException("Archive entry escapes the destination.");
    }
    private static void Extract(string file, string destination, CancellationToken ct)
    {
        using var zip = ZipFile.OpenRead(file);
        foreach (var e in zip.Entries)
        {
            ct.ThrowIfCancellationRequested();
            var name = e.FullName.Replace('\\', '/');
            if (!name.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase))
                continue;
            var relative = Uri.UnescapeDataString(name[9..]);
            if (relative == "" || name.EndsWith('/'))
                continue;
            var target = SafeArchivePath(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            e.ExtractToFile(target, true);
        }
    }
    public static string[] SdkInstallers(IEnumerable<string> targets, IEnumerable<string>? available = null)
    {
        var architectures = targets.Prepend("x86").Distinct().ToArray();
        var required = new[] { "Windows SDK for Windows Store Apps Tools-x86_en-us.msi", "Windows SDK for Windows Store Apps Headers-x86_en-us.msi", "Windows SDK for Windows Store Apps Libs-x86_en-us.msi", "Universal CRT Headers Libraries and Sources-x86_en-us.msi" }
            .Concat(architectures.Select(t => "Windows SDK Desktop Headers " + t + "-x86_en-us.msi"))
            .Concat(targets.Select(t => "Windows SDK Desktop Libs " + t + "-x86_en-us.msi"));
        // Onecore headers were split into separate MSIs in newer SDKs.
        var optional = new[] { "Windows SDK for Windows Store Apps Headers OnecoreUap-x86_en-us.msi" }
            .Concat(architectures.Select(t => "Windows SDK OnecoreUap Headers " + t + "-x86_en-us.msi"));
        if (available is not null) { var names = available.ToHashSet(StringComparer.OrdinalIgnoreCase); optional = optional.Where(names.Contains); }
        return required.Concat(optional).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async Task InstallAsync(CancellationToken ct)
    {
        var host = page["host"].Value;
        var targets = page["targets"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
        if (!new[] { "x64", "x86", "arm64" }.Contains(host) || targets.Length == 0 || targets.Any(t => !new[] { "x64", "x86", "arm64", "arm" }.Contains(t)))
            throw new ArgumentException("Unsupported host or target architecture.");
        var root = Path.GetFullPath(page[destinationKey].Value);
        if (Path.GetPathRoot(root) == root)
            throw new ArgumentException("Choose a dedicated toolchain subfolder.");
        Directory.CreateDirectory(root);
        var versions = await VersionsAsync(page["preview"].IsChecked, ct);
        var compiler = page["compiler"].Value == "Latest" ? versions.Compiler.Keys.MaxBy(Version.Parse)! : page["compiler"].Value;
        var sdk = page["sdk"].Value == "Latest" ? versions.Sdk.Keys.MaxBy(int.Parse)! : page["sdk"].Value;
        if (!versions.Compiler.TryGetValue(compiler, out var version) || !versions.Sdk.TryGetValue(sdk, out var sdkId))
            throw new InvalidDataException("Selected version is not in this catalog. Fetch versions again.");
        var jobs = new List<(string Id, JsonElement Payload)>();
        foreach (var id in CompilerPackages(version, host, targets))
        {
            var pkg = Package(id);
            if (pkg.ValueKind == JsonValueKind.Undefined)
            {
                context.Log(page.Title, "Optional package unavailable: " + id);
                continue;
            }
            jobs.AddRange(Payloads(pkg).Select(pl => (id, pl)));
        }
        if (jobs.Count == 0)
            throw new InvalidDataException("No compiler payloads resolved.");
        for (var i = 0; i < jobs.Count; i++)
        {
            var job = jobs[i];
            var file = await Download(job.Payload, Text(job.Payload, "fileName"), ct);
            page.Status = "Extracting " + job.Id;
            await Task.Run(() => Extract(file, root, ct), ct);
            page.Progress = 5 + 45.0 * (i + 1) / jobs.Count;
        }
        var component = Package(sdkId);
        var candidates = new List<JsonElement>();
        if (component.TryGetProperty("dependencies", out var dependencies))
            foreach (var dep in dependencies.EnumerateObject())
            {
                var pkg = Package(dep.Name);
                if (pkg.ValueKind != JsonValueKind.Undefined)
                    candidates.AddRange(Payloads(pkg));
            }
        var payloads = candidates.GroupBy(pl => Text(pl, "fileName").Replace('\\', '/'), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        JsonElement Payload(string name) => payloads.TryGetValue("Installers/" + name, out var pl) ? pl : throw new InvalidDataException("Required SDK payload missing: " + name);
        var sdkFolder = Path.Combine(Cache, "sdk-" + sdk);
        Directory.CreateDirectory(sdkFolder);
        var installers = SdkInstallers(targets, payloads.Keys.Select(name => name.StartsWith("Installers/", StringComparison.OrdinalIgnoreCase) ? name[11..] : name));
        var cabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in installers)
        {
            var file = await Download(Payload(name), name, ct);
            var target = Path.Combine(sdkFolder, name);
            File.Copy(file, target, true);
            downloaded.Add(target);
            var bytes = await File.ReadAllBytesAsync(file, ct);
            var text = Encoding.Latin1.GetString(bytes);
            foreach (Match m in Regex.Matches(text, @"[A-Za-z0-9_.-]{32}\.cab", RegexOptions.IgnoreCase))
                cabs.Add(m.Value);
        }
        if (cabs.Count == 0)
            throw new InvalidDataException("SDK cabinet dependencies could not be identified.");
        foreach (var name in cabs)
        {
            var file = await Download(Payload(name), name, ct);
            var target = Path.Combine(sdkFolder, name);
            File.Copy(file, target, true);
            downloaded.Add(target);
        }
        for (var i = 0; i < installers.Length; i++)
        {
            var file = Path.Combine(sdkFolder, installers[i]);
            var log = Path.Combine(sdkFolder, "extract-" + i + ".log");
            page.Status = "Extracting SDK · " + installers[i];
            var result = await context.RunAsync("msiexec.exe", ["/a", file, "/qn", "/norestart", "/L*v", log, "TARGETDIR=" + root], ct);
            if (result.ExitCode is not (0 or 3010))
                throw new IOException($"SDK extraction failed ({result.ExitCode}). See {log}");
            page.Progress = 55 + 35.0 * (i + 1) / installers.Length;
        }
        foreach (var target in targets)
        {
            var t = Toolchain.Detect(root, host, target);
            t.Validate();
            // Copy debug CRT runtime DLLs without removing any existing toolchain content.
            var redist = Path.Combine(root, @"VC\Redist\MSVC");
            if (Directory.Exists(redist))
                foreach (var dll in Directory.EnumerateFiles(redist, "*.dll", SearchOption.AllDirectories).Where(f => f.Contains(Path.DirectorySeparatorChar + target + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    File.Copy(dll, Path.Combine(Path.GetDirectoryName(t.Compiler)!, Path.GetFileName(dll)), true);
            await File.WriteAllTextAsync(Path.Combine(root, "setup_" + target + ".bat"), ToolchainOperations.Batch(t), ct);
            await File.WriteAllTextAsync(Path.Combine(root, "setup_" + target + ".ps1"), ToolchainOperations.PowerShell(t), ct);
        }
        if (page["clean"].IsChecked)
            foreach (var file in downloaded.Distinct())
            {
                ct.ThrowIfCancellationRequested();
                if (File.Exists(file))
                    File.Delete(file);
            }
        context.Log(page.Title, "Compiler and SDK extracted; run diagnostics to validate compile/link/runtime on this PC.");
    }
}

