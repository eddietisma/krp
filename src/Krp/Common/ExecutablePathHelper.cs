using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Krp.Common;

public static class ExecutablePathHelper
{
    public static string GetExecutableBaseDirectory()
    {
        try
        {
            var candidates = new[]
            {
                TryResolveDirectory(AppContext.BaseDirectory),
                TryResolveFileTargetDirectory(Process.GetCurrentProcess().MainModule?.FileName),
                TryResolveDirectory(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName)),
            }
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(candidate => candidate!)
            .ToArray();

            var runtimeCandidate = candidates.FirstOrDefault(candidate => Directory.Exists(Path.Combine(candidate, "runtimes")));
            return runtimeCandidate ?? candidates.FirstOrDefault() ?? AppContext.BaseDirectory;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }

    private static string? TryResolveDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var directoryInfo = new DirectoryInfo(directory);
        var resolved = directoryInfo.ResolveLinkTarget(true) as DirectoryInfo;
        return resolved?.FullName ?? directoryInfo.FullName;
    }

    private static string? TryResolveFileTargetDirectory(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var fileInfo = new FileInfo(filePath);
        var resolved = fileInfo.ResolveLinkTarget(true) as FileInfo;
        var targetPath = resolved?.FullName ?? fileInfo.FullName;
        return TryResolveDirectory(Path.GetDirectoryName(targetPath));
    }
}
