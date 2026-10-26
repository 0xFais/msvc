using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DevTools.Infrastructure;

public static partial class WindowsEnvironment
{
    public static string NewestVersion(string directory) => !Directory.Exists(directory) ? "" : Directory.GetDirectories(directory).Select(Path.GetFileName).Where(n => Version.TryParse(n, out _)).OrderByDescending(n => Version.Parse(n!)).FirstOrDefault() ?? "";
    public static string FindExecutable(string executable, string root = "")
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + executable);
            var path = (key?.GetValue("") as string)?.Trim('"');
            if (File.Exists(path))
                return path!;
        }
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
        {
            if (string.IsNullOrWhiteSpace(folder))
                continue;
            try
            {
                var path = Path.Combine(Environment.ExpandEnvironmentVariables(folder.Trim().Trim('"')), executable);
                if (File.Exists(path))
                    return path;
            }
            catch (ArgumentException) { }
        }
        var bases = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JetBrains", "Toolbox", "apps"), Path.Combine(root, "apps") };
        foreach (var folder in bases.Where(Directory.Exists).Distinct())
        {
            var found = FindWithin(folder, executable, 5);
            if (found != "")
                return found;
        }
        return "";
    }
    private static string FindWithin(string directory, string name, int depth)
    {
        try
        {
            if (File.Exists(Path.Combine(directory, name)))
                return Path.Combine(directory, name);
            if (depth <= 0)
                return "";
            foreach (var sub in Directory.EnumerateDirectories(directory))
            {
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                    continue;
                var found = FindWithin(sub, name, depth - 1);
                if (found != "")
                    return found;
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
        return "";
    }
    public static string ContextCommand(string id, string executable, string scope)
    {
        if (executable.Contains('"'))
            throw new ArgumentException("Invalid executable path.");
        var arg = scope == "background" ? "%V" : "%1";
        var prefix = id switch
        {
            "terminal" => " -d",
            "pwsh" => " -NoExit -WorkingDirectory",
            "gitbash" => " --cd=",
            "wezterm" => " start --cwd",
            "alacritty" => " --working-directory",
            _ => ""
        };
        return prefix.EndsWith('=') ? $"\"{executable}\"{prefix}\"{arg}\"" : $"\"{executable}\"{prefix} \"{arg}\"";
    }
    public static string MergePath(string current, IEnumerable<string> entries, bool add)
    {
        var additions = entries.ToArray();
        var keep = current.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(s => !additions.Any(a => a.TrimEnd('\\').Equals(s.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)));
        return string.Join(';', (add ? additions.Concat(keep) : keep).Distinct(StringComparer.OrdinalIgnoreCase));
    }
    public static void Broadcast() => SendMessageTimeout(new nint(0xffff), 0x1a, 0, "Environment", 2, 5000, out _);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, string lParam, uint flags, uint timeout, out nint result);
}

