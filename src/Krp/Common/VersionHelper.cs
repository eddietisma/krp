using System.Diagnostics;
using System.Reflection;

namespace Krp.Common;

public static class VersionHelper
{
    public static string GetProductVersion()
    {
        var assembly = typeof(Program).Assembly;
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            return infoVersion;
        }

        var fallbackVersion = assembly.GetName().Version?.ToString() ?? "unknown";
        // Assembly location might be empty in some contexts (e.g., single-file publish).
        var assemblyLocation = assembly.Location;
        if (string.IsNullOrWhiteSpace(assemblyLocation))
        {
            return fallbackVersion;
        }

        var fvi = FileVersionInfo.GetVersionInfo(assemblyLocation);
        return !string.IsNullOrWhiteSpace(fvi.ProductVersion) ? fvi.ProductVersion : fallbackVersion;
    }
}
