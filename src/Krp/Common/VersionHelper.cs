using System.Diagnostics;

namespace Krp.Common;

public static class VersionHelper
{
    public static string GetProductVersion()
    {
        var filePath = typeof(Program).Assembly.Location;
        var fvi = FileVersionInfo.GetVersionInfo(filePath);
        var infoVersion = fvi.ProductVersion; // e.g. "1.0.0+abc123"

        return infoVersion;
    }
}