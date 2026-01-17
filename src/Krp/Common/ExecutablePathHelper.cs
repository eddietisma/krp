using System;
using System.Diagnostics;
using System.IO;

namespace Krp.Common;

public static class ExecutablePathHelper
{
    public static string GetExecutableBaseDirectory()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                var exeInfo = new FileInfo(exePath);
                var exeTarget = exeInfo.ResolveLinkTarget(true) as FileInfo;
                return exeTarget?.DirectoryName ?? exeInfo.DirectoryName ?? AppContext.BaseDirectory;
            }

            var assemblyLocation = System.Reflection.Assembly.GetEntryAssembly()?.Location;
            if (string.IsNullOrWhiteSpace(assemblyLocation))
            {
                return AppContext.BaseDirectory;
            }

            var fileInfo = new FileInfo(assemblyLocation);
            var target = fileInfo.ResolveLinkTarget(true) as FileInfo;
            return target?.DirectoryName ?? fileInfo.DirectoryName ?? AppContext.BaseDirectory;
        }
        catch
        {
            return AppContext.BaseDirectory;
        }
    }
}
