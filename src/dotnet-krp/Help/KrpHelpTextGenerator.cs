using McMaster.Extensions.CommandLineUtils;
using McMaster.Extensions.CommandLineUtils.HelpText;
using System;
using System.Collections.Generic;
using System.IO;

namespace Krp.Tool.Help;

/// <summary>
/// Moves the "Run 'krp [command] -?|-h|--help'..." help hint to the final line for cleaner output.
/// </summary>
public sealed class KrpHelpTextGenerator : IHelpTextGenerator
{
    private const string MoreInfoPrefix = "Run '";
    private const string MoreInfoSuffix = " for more information about a command.";

    private readonly IHelpTextGenerator _inner = new DefaultHelpTextGenerator();

    public void Generate(CommandLineApplication application, TextWriter output)
    {
        using var buffer = new StringWriter();
        _inner.Generate(application, buffer);

        var text = buffer.ToString();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var normalized = text.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');

        string moreInfoLine = null;
        var filtered = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            if (moreInfoLine == null &&
                line.StartsWith(MoreInfoPrefix, StringComparison.Ordinal) &&
                line.EndsWith(MoreInfoSuffix, StringComparison.Ordinal))
            {
                moreInfoLine = line;
                continue;
            }

            filtered.Add(line);
        }

        if (moreInfoLine != null)
        {
            while (filtered.Count > 0 && string.IsNullOrEmpty(filtered[^1]))
            {
                filtered.RemoveAt(filtered.Count - 1);
            }

            filtered.Add(string.Empty);
            filtered.Add(moreInfoLine);
        }

        output.Write(string.Join(Environment.NewLine, filtered));
    }
}
