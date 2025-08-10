using Microsoft.Extensions.Logging;

namespace Krp.Tool.TerminalUi.Extensions;


public static class TerminalExtensions
{
    public static string GetLevelMarkup(this LogLevel level) => level switch
    {
        LogLevel.Trace => "[grey]TRA[/]",
        LogLevel.Debug => "[grey]DBG[/]",
        LogLevel.Information => "[green]INF[/]",
        LogLevel.Warning => "[yellow]WRN[/]",
        LogLevel.Error => "[red]ERR[/]",
        LogLevel.Critical => "[red]CRI[/]",
        _ => level.ToString().ToUpperInvariant()
    };
}