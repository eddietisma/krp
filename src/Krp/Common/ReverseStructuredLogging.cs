using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Krp.Common;

public static class ReverseStructuredLogging
{
    private static readonly ConcurrentDictionary<string, Regex> _regexCache = new();

    public static bool TryParse(string template, string input, out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>();
        
        var regex = GetOrAddRegex(template);

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

    private static Regex GetOrAddRegex(string template)
    {
        return _regexCache.GetOrAdd(template, t =>
        {
            // Build regex from template.
            var regexPattern = Regex.Escape(t)
                .Replace(@"\{", "{")
                .Replace(@"\}", "}")
                .Replace("{", "(?<")
                .Replace("}", @">.+?)");

            return new Regex($"^{regexPattern}$", RegexOptions.Compiled);
        });
    }
}
