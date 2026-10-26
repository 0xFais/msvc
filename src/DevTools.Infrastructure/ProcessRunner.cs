using System.Diagnostics;
using DevTools.Contracts;

namespace DevTools.Infrastructure;

public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token, string? directory = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments)
            start.ArgumentList.Add(arg);
        if (directory is not null)
            start.WorkingDirectory = directory;
        if (environment is not null)
            foreach (var pair in environment)
                start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new IOException("Could not start " + executable);
        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token);
        }
        catch (OperationCanceledException) { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
        var output = await stdout;
        var error = await stderr;
        return new(process.ExitCode, (output + Environment.NewLine + error).Trim()) { StandardOutput = output, StandardError = error };
    }
}

