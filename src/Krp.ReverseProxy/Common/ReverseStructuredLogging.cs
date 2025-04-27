using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Krp.ReverseProxy.Common;

public static class ReverseStructuredLogging
{
    public static bool TryParse(string template, string input, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>();

        // Build regex from template
        var regexPattern = Regex.Escape(template)
            .Replace(@"\{", "{")
            .Replace(@"\}", "}")
            .Replace("{", "(?<")
            .Replace("}", @">.+?)");

        var regex = new Regex($"^{regexPattern}$");

        var match = regex.Match(input);
        if (!match.Success)
        {
            return false;
        }

        foreach (var groupName in regex.GetGroupNames())
        {
            if (int.TryParse(groupName, out _))
            {
                continue; // Skip numbered groups
            }

            values[groupName] = match.Groups[groupName].Value;
        }

        return true;
    }
}