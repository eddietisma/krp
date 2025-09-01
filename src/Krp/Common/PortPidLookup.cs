using System;
using System.Diagnostics;

namespace Krp.Common;

public static class PortPidLookup
{
    public static bool TryGetPidForPort(int port, out int pid)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new NotImplementedException("");
        }

        pid = -1;

        var processStartInfo = new ProcessStartInfo("netstat", "-ano -p udp")
        {
            RedirectStandardOutput = true, 
            RedirectStandardError = true, 
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(processStartInfo);
        if (process == null)
        {
            return false;
        }

        var output = process.StandardOutput.ReadToEnd();
        var err = process.StandardError.ReadToEnd();

        process.WaitForExit();

        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        // Lines look like:
        // TCP    0.0.0.0:5000     0.0.0.0:0      LISTENING       1234
        // UDP    0.0.0.0:5353     *:*                           14012
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        // Match ":<port>" but avoid accidental substring hits (e.g., ":155757")
        string needle = ":" + port;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!line.Contains(needle, StringComparison.Ordinal))
            {
                continue;
            }

            // Split on whitespace; PID is last token
            var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
            {
                continue;
            }

            // For TCP we can require LISTENING rows; for UDP there’s no state column
            // Windows formats vary; to be safe, just take the last column as PID
            var pidToken = parts[^1];
            if (int.TryParse(pidToken, out var p) && p > 0)
            {
                pid = p;
                return true;
            }
        }

        return false;
    }
}