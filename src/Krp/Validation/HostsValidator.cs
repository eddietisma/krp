using Krp.Common;
using System;
using System.IO;

namespace Krp.Validation;

public static class HostsValidator
{
    public static bool Validate(string hostsPath)
    {
        var fileExists = File.Exists(hostsPath);
        if (!fileExists)
        {
            Console.Error.WriteLine($"\u001b[31m • Hosts file not found at '{hostsPath}'\u001b[0m");
        }
        else
        {
            Console.WriteLine($" • Found hosts file at '{hostsPath}'");
        }
        
        var hasAccess = FileHelper.HasWriteAccess(hostsPath);
        if (!hasAccess)
        {
            Console.Error.WriteLine($"\u001b[31m • Access to the path '{hostsPath}' is denied\u001b[0m");
        }
        else
        {
            Console.WriteLine(" • Permission to hosts file OK");
        }

        return fileExists && hasAccess;
    }
}