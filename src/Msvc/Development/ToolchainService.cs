using DevTools.Contracts;
using DevTools.Infrastructure;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text;

namespace DevTools.Modules.Development;

public sealed record Toolchain(string Root, string CompilerVersion, string SdkVersion, string Host, string Target)
{
    public string CompilerRoot => Path.Combine(Root, @"VC\Tools\MSVC", CompilerVersion);
    public string SdkRoot => Path.Combine(Root, @"Windows Kits\10");
    public string Compiler => Path.Combine(CompilerRoot, "bin", "Host" + Host, Target, "cl.exe");
    public string Linker => Path.Combine(CompilerRoot, "bin", "Host" + Host, Target, "link.exe");
    public Dictionary<string, string> EnvironmentVariables()
    {
        var include = Path.Combine(SdkRoot, "Include", SdkVersion);
        var lib = Path.Combine(SdkRoot, "Lib", SdkVersion);
        return new()
        {
            ["VCToolsInstallDir"] = CompilerRoot + "\\",
            ["WindowsSdkDir"] = SdkRoot + "\\",
            ["WindowsSDKVersion"] = SdkVersion + "\\",
            ["PATH"] = string.Join(';', Path.GetDirectoryName(Compiler), Path.Combine(SdkRoot, "bin", SdkVersion, Host), System.Environment.GetEnvironmentVariable("PATH")),
            ["INCLUDE"] = string.Join(';', new[] { Path.Combine(CompilerRoot, "include") }.Concat(new[] { "ucrt", "shared", "um", "winrt", "cppwinrt" }.Select(s => Path.Combine(include, s)))),
            ["LIB"] = string.Join(';', Path.Combine(CompilerRoot, "lib", Target), Path.Combine(lib, "ucrt", Target), Path.Combine(lib, "um", Target))
        };
    }
    public void Validate()
    {
        if (CompilerVersion == "" || SdkVersion == "")
            throw new DirectoryNotFoundException("A compiler and Windows SDK must both be present.");
        foreach (var file in new[] { Compiler, Linker, Path.Combine(CompilerRoot, "include", "vector"), Path.Combine(SdkRoot, "Include", SdkVersion, "um", "Windows.h"), Path.Combine(SdkRoot, "Lib", SdkVersion, "um", Target, "kernel32.lib"), Path.Combine(SdkRoot, "Lib", SdkVersion, "ucrt", Target, "ucrt.lib") })
            if (!File.Exists(file))
                throw new FileNotFoundException("Required toolchain component is missing.", file);
    }
    public static Toolchain Detect(string root, string host = "x64", string target = "x64") => new(Path.GetFullPath(root), WindowsEnvironment.NewestVersion(Path.Combine(root, @"VC\Tools\MSVC")), WindowsEnvironment.NewestVersion(Path.Combine(root, @"Windows Kits\10\Include")), host, target);
}

internal static class ToolchainOperations
{
    public static void OpenTerminal(Toolchain toolchain)
    {
        toolchain.Validate();
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, WorkingDirectory = toolchain.Root };
        start.ArgumentList.Add("/d");
        start.ArgumentList.Add("/k");
        foreach (var pair in toolchain.EnvironmentVariables())
            start.Environment[pair.Key] = pair.Value;
        Process.Start(start);
    }
    public static async Task<string> Diagnose(Toolchain t, IModuleContext c, CancellationToken ct)
    {
        var report = $"Toolchain: {t.Root}\nCompiler version: {t.CompilerVersion}\nWindows SDK: {t.SdkVersion}\nHost/target: {t.Host}/{t.Target}\n";
        try
        {
            t.Validate();
        }
        catch (IOException e) { return report + "\nToolchain validation failed:\n" + e.Message + (e is FileNotFoundException missing ? "\nMissing file: " + missing.FileName : "") + "\nChoose a complete installation or download MSVC + SDK."; }
        var folder = Path.Combine(c.DataDirectory, "diagnostics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var source = Path.Combine(folder, "main.cpp");
        var binary = Path.Combine(folder, "check.exe");
        await File.WriteAllTextAsync(source, "#include <iostream>\nint main(){ std::cout << \"DevTools compiler/link/runtime test passed\"; return 0; }\n", ct);
        var build = await c.RunAsync(t.Compiler, ["/nologo", "/EHsc", source, "/Fe:" + binary], ct, folder, t.EnvironmentVariables());
        report += $"Compiler: {t.Compiler}\nCompile/link exit: {build.ExitCode}\n{build.Output}\n";
        var localArch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();
        if (build.ExitCode == 0 && (t.Target == localArch || (localArch == "x64" && t.Target == "x86")))
        {
            var run = await c.RunAsync(binary, [], ct, folder, t.EnvironmentVariables());
            report += $"Runtime exit: {run.ExitCode}\n{run.Output}\n";
        }
        else if (build.ExitCode == 0)
            report += "Runtime test skipped for a cross-architecture target. Run the built program on the target device.\n";
        report += "Files retained: " + folder;
        await File.WriteAllTextAsync(Path.Combine(folder, "report.txt"), report, ct);
        return report;
    }
    public static string Batch(Toolchain t)
    {
        if (t.Root.IndexOfAny(['"', '\r', '\n', '%', '!', '&', '|', '<', '>', '^']) >= 0)
            throw new ArgumentException("Use a toolchain folder without shell-special characters when generating CMD launchers.");
        var env = t.EnvironmentVariables();
        return "@echo off\r\n" + string.Join("\r\n", env.Select(pair =>
        {
            var value = pair.Key == "PATH" ? string.Join(';', Path.GetDirectoryName(t.Compiler), Path.Combine(t.SdkRoot, "bin", t.SdkVersion, t.Host), "%PATH%") : pair.Value;
            value = value.Replace(t.Root.TrimEnd('\\') + "\\", "%~dp0", StringComparison.OrdinalIgnoreCase);
            return $"set \"{pair.Key}={value}\"";
        })) + "\r\n";
    }
    public static string PowerShell(Toolchain t)
    {
        string Literal(string text) => "'" + text.Replace("'", "''") + "'";
        var lines = new List<string> { "# Dot-source this script to configure the current PowerShell session." };
        foreach (var pair in t.EnvironmentVariables())
        {
            var values = (pair.Key == "PATH" ? string.Join(';', Path.GetDirectoryName(t.Compiler), Path.Combine(t.SdkRoot, "bin", t.SdkVersion, t.Host)) : pair.Value).Split(';');
            var expressions = values.Select(value => value.StartsWith(t.Root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ? "(Join-Path $PSScriptRoot " + Literal(value[(t.Root.TrimEnd('\\').Length + 1)..]) + ")" : Literal(value)).ToList();
            if (pair.Key == "PATH")
                expressions.Add("$env:PATH");
            lines.Add("$env:" + pair.Key + " = @(" + string.Join(", ", expressions) + ") -join ';'");
        }
        return string.Join("\r\n", lines) + "\r\n";
    }
    public static void ApplyEnvironment(Toolchain t, IModuleContext c, bool remove)
    {
        if (!remove)
            t.Validate();
        using var key = Registry.CurrentUser.CreateSubKey("Environment");
        var env = t.EnvironmentVariables();
        var paths = new[] { Path.GetDirectoryName(t.Compiler)!, Path.Combine(t.SdkRoot, "bin", t.SdkVersion, t.Host) };
        if (remove)
        {
            var ownedPaths = c.Get("environment.ownedPaths");
            if (ownedPaths != "")
                key.SetValue("Path", WindowsEnvironment.MergePath(key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "", ownedPaths.Split(';'), false), RegistryValueKind.ExpandString);
            foreach (var name in new[] { "VCToolsInstallDir", "INCLUDE", "LIB" })
                if (c.Get("environment.owned." + name) is { Length: > 0 } owned && key.GetValue(name)?.ToString() == owned)
                {
                    var backup = c.Get("environment.backup." + name);
                    if (c.Get("environment.existed." + name) == "true")
                        key.SetValue(name, backup, RegistryValueKind.ExpandString);
                    else
                        key.DeleteValue(name, false);
                    c.Save("environment.owned." + name, "");
                }
            c.Save("environment.ownedPaths", "");
        }
        else
        {
            var currentPath = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
            if (c.Get("environment.ownedPaths") is { Length: > 0 } previous)
                currentPath = WindowsEnvironment.MergePath(currentPath, previous.Split(';'), false);
            var existing = currentPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var newlyAdded = paths.Where(path => !existing.Any(item => item.TrimEnd('\\').Equals(path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))).ToArray();
            key.SetValue("Path", WindowsEnvironment.MergePath(currentPath, paths, true), RegistryValueKind.ExpandString);
            c.Save("environment.ownedPaths", string.Join(';', newlyAdded));
            foreach (var name in new[] { "VCToolsInstallDir", "INCLUDE", "LIB" })
            {
                var currentValue = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
                if (c.Get("environment.owned." + name) == "" || currentValue != c.Get("environment.owned." + name))
                {
                    c.Save("environment.existed." + name, key.GetValue(name) is null ? "false" : "true");
                    c.Save("environment.backup." + name, currentValue ?? "");
                }
                key.SetValue(name, env[name]);
                c.Save("environment.owned." + name, env[name]);
            }
        }
        WindowsEnvironment.Broadcast();
    }
}

