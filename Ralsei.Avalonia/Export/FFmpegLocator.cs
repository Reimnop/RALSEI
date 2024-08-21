using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Ralsei.Avalonia.Export;

/// <summary>
/// Finds FFmpeg, which makes the videos.
/// </summary>
public static partial class FFmpegLocator
{
    private static string ExecutableName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    /// <summary>
    /// The FFmpeg to run: the one that was chosen (a file, or the folder it is in), and if there is none, the one next
    /// to the app or on the path. Null when there is no such file.
    /// </summary>
    public static string? Find(string? chosen)
    {
        if (!string.IsNullOrWhiteSpace(chosen))
        {
            var path = chosen.Trim().Trim('"');
            if (Directory.Exists(path))
                path = Path.Combine(path, ExecutableName);

            return File.Exists(path) ? Path.GetFullPath(path) : null;
        }

        if (File.Exists(Path.Combine(AppContext.BaseDirectory, ExecutableName)))
            return Path.Combine(AppContext.BaseDirectory, ExecutableName);

        var directories = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var directory in directories)
        {
            try
            {
                var candidate = Path.Combine(directory.Trim('"'), ExecutableName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // Not a path, which happens on the path
            }
        }

        return null;
    }

    /// <summary>
    /// Runs the program to see if it is FFmpeg. Gives its version, or null when it is not.
    /// </summary>
    public static async Task<string?> GetVersionAsync(string executable, CancellationToken cancellationToken = default)
    {
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-hide_banner");
            startInfo.ArgumentList.Add("-version");

            using var process = Process.Start(startInfo);
            if (process is null)
                return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);

                var match = VersionPattern().Match(output);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                return null;
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^ffmpeg version (\S+)", RegexOptions.Multiline)]
    private static partial Regex VersionPattern();
}
