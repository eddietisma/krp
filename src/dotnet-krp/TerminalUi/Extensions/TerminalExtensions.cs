using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace Krp.Tool.TerminalUi.Extensions;


public static class TerminalExtensions
{
    public static Text GetLevelText(this LogLevel level) => level switch
    {
        LogLevel.Trace => new Text("TRA", Color.Grey),
        LogLevel.Debug => new ("DBG", Color.Grey),
        LogLevel.Information => new ("INF", Color.Green),
        LogLevel.Warning => new ("WRN", Color.Yellow),
        LogLevel.Error => new ("ERR", Color.Red),
        LogLevel.Critical => new ("CRI", Color.Red),
        _ => new (level.ToString().ToUpperInvariant()),
    };
}