using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using GalaxyBudsClient.Generated.I18N;
using Serilog;

namespace GalaxyBudsClient.Platform;

/// <summary>
/// Toggles the system's default input device between muted (input volume 0) and its previous level.
/// Currently implemented for macOS only, via AppleScript.
/// </summary>
public static class MicrophoneMute
{
    private const int FallbackVolume = 75;
    private static readonly object Lock = new();
    private static int _lastVolume = FallbackVolume;

    public static bool IsSupported => PlatformUtils.IsOSX;

    public static void Toggle()
    {
        if (!IsSupported)
            return;

        // Strings must be resolved on the calling (UI) thread
        var mutedText = Strings.MicMuted;
        var unmutedText = Strings.MicUnmuted;
        Task.Run(() => ToggleOsx(mutedText, unmutedText));
    }

    private static void ToggleOsx(string mutedText, string unmutedText)
    {
        lock (Lock)
        {
            try
            {
                var output = RunAppleScript("input volume of (get volume settings)");
                if (!int.TryParse(output, NumberStyles.Integer, CultureInfo.InvariantCulture, out var current))
                {
                    Log.Warning("MicrophoneMute: Default input device has no adjustable volume ({Output})", output);
                    return;
                }

                if (current > 0)
                {
                    _lastVolume = current;
                    RunAppleScript($"set volume input volume 0\n{Notification(mutedText)}");
                    Log.Debug("MicrophoneMute: Muted (previous volume {Volume})", current);
                }
                else
                {
                    var restore = _lastVolume > 0 ? _lastVolume : FallbackVolume;
                    RunAppleScript($"set volume input volume {restore}\n{Notification(unmutedText)}");
                    Log.Debug("MicrophoneMute: Unmuted (volume {Volume})", restore);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "MicrophoneMute: Failed to toggle microphone");
            }
        }
    }

    private static string Notification(string text)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $"display notification \"{escaped}\" with title \"Galaxy Buds Manager\"";
    }

    private static string RunAppleScript(string script)
    {
        var psi = new ProcessStartInfo("/usr/bin/osascript")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start osascript");
        process.StandardInput.Write(script);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd().Trim();
        process.WaitForExit(5000);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"osascript exited with {process.ExitCode}: {error}");

        return output;
    }
}
