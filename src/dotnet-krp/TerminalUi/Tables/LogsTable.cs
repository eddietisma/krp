using Krp.Tool.TerminalUi.Extensions;
using Krp.Tool.TerminalUi.Logging;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Krp.Tool.TerminalUi.Tables;

public class LogsTable 
{
    public const int HEADER_SIZE = 2;
    public int Count { get; private set; }

    private readonly KrpTerminalState _state;
    private readonly InMemoryLoggingProvider _logProvider;
    private readonly List<ColumnDefinition<LogEntry>> _columnDefinitions;
    
    public LogsTable(KrpTerminalState state, InMemoryLoggingProvider logProvider)
    {
        _state = state;
        _logProvider = logProvider;

        // Initialize rows for logs table.
        _state.SelectedRow.Add(KrpTable.Logs, 0);
        _state.AnchorRowIndex.Add(KrpTable.Logs, 0);

        // Initialize column definitions.
        _columnDefinitions =
        [
            new ColumnDefinition<LogEntry>("", 8, SortField.None, _ => string.Empty, true),
            new ColumnDefinition<LogEntry>("", 3, SortField.None, _ => string.Empty, true),
            new ColumnDefinition<LogEntry>("", 0, SortField.None, _ => string.Empty, true),
        ];
    }

    public bool DetectChanges()
    {
        var newLogsCount = _logProvider.CountLogs();
        if (newLogsCount == Count)
        {
            return false;
        }

        Count = newLogsCount;

        var fixedRows = 2 + KrpTerminalUi.HEADER_SIZE;
        var rowsVis = Math.Max(1, _state.WindowHeight - fixedRows);

        var total = _logProvider.CountLogs();
        var maxStart = Math.Max(0, total - rowsVis);

        _state.SelectedRow[KrpTable.Logs] = maxStart; // Forces tail-like behavior when new logs arrive.
        return true;
    }

    public Panel BuildMainPanel()
    {
        // Rows that fit.
        var fixedRows = HEADER_SIZE + KrpTerminalUi.HEADER_SIZE;
        var maxRows = Math.Max(1, _state.WindowHeight - fixedRows);

        var total = _logProvider.CountLogs();
        var maxStart = Math.Max(0, total - maxRows);

        // _selectedRowIndex is "start row from the top"
        //  • Down (index++)  => start increases => scrolls down (newer)
        //  • Up   (index--)  => start decreases => scrolls up   (older)
        _state.SelectedRow[KrpTable.Logs] = Math.Clamp(_state.SelectedRow[KrpTable.Logs], 0, maxStart);

        var start = _state.SelectedRow[KrpTable.Logs];
        var slice = _logProvider.ReadLogs(start, maxRows).ToList();

        var tbl = new Table().NoBorder().Expand().HideHeaders().Width(Console.WindowWidth).ShowRowSeparators(); ;

        // Print columns.
        foreach (var col in _columnDefinitions)
        {
            tbl.AddColumn(new TableColumn("")
            {
                Width = col.Width == 0 ? Console.WindowWidth - 10 : col.Width,
                NoWrap = false,
                Padding = new Padding(1),
            });
        }

        // Print rows.
        foreach (var log in slice)
        {
            var msgText = new Text(log.Message ?? string.Empty);
            IRenderable msgCell = log.Exception == null
                ? msgText
                : new Rows(msgText, log.Exception.GetRenderable(new ExceptionSettings { Format = ExceptionFormats.ShortenEverything }));

            tbl.AddRow(
                new Text(log.Timestamp.ToString("HH:mm:ss")),
                new Text(GetLogLevelText(log.Level), GetLogLevelColor(log.Level)),
                msgCell);
        }

        if (total == 0)
        {
            tbl.AddRow(Text.Empty, new Text("No logs available", Color.Grey), Text.Empty, Text.Empty);
        }

        return new Panel(tbl)
            .Header(new PanelHeader($"[##00ffff] logs([magenta1]all[/])[[[white]{_logProvider!.CountLogs()}[/]]] [/]", Justify.Center));
    }

    public Panel BuildContextMenuPanel()
    {
        return new Panel(new Text(""));
    }

    private static string GetLogLevelText(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => level.ToString().ToUpperInvariant(),
    };

    private static Color GetLogLevelColor(LogLevel level) => level switch
    {
        LogLevel.Trace => Color.Grey,
        LogLevel.Debug => Color.Grey,
        LogLevel.Information => Color.Green,
        LogLevel.Warning => Color.Yellow,
        LogLevel.Error => Color.Red,
        LogLevel.Critical => Color.Red,
        _ => Color.Grey,
    };
}