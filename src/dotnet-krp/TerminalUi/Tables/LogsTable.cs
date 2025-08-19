using Krp.Tool.TerminalUi.Extensions;
using Krp.Tool.TerminalUi.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Linq;

namespace Krp.Tool.TerminalUi.Tables;

public class LogsTable 
{
    private readonly KrpTerminalState _state;
    private readonly InMemoryLoggingProvider _logProvider;

    public int Count { get; private set; }
    
    public LogsTable(KrpTerminalState state, InMemoryLoggingProvider logProvider)
    {
        _state = state;
        _logProvider = logProvider;

        _state.SelectedRow.Add(KrpTable.Logs, 0); // Initialize selected row for logs table
    }

    public bool DetectChanges()
    {
        var newLogsCount = _logProvider.CountLogs();
        if (newLogsCount != Count)
        {
            Count = newLogsCount;
            _state.SelectedRow[KrpTable.Logs] = 0; // Forces tail-like behavior when new logs arrive.
            return true;
        }
        
        return false;
    }

    public Panel BuildPanel()
    {
        var fixedRows = 2 + KrpTerminalUi.HEADER_SIZE;
        var rowsVis = Math.Max(1, _state.WindowHeight - fixedRows);

        var total = _logProvider.CountLogs();
        var maxStart = Math.Max(0, total - rowsVis);

        // _selectedRowIndex is "start row from the top"
        // Down (index++)  => start increases => scrolls down (newer)
        // Up   (index--)  => start decreases => scrolls up   (older)
        _state.SelectedRow[KrpTable.Logs] = Math.Clamp(_state.SelectedRow[KrpTable.Logs], 0, maxStart);

        if (_state.SelectedRow[KrpTable.Logs] == 0) // first-time / follow-tail state
        {
            _state.SelectedRow[KrpTable.Logs] = maxStart;  // start at the tail
        }

        var start = _state.SelectedRow[KrpTable.Logs];
        var slice = _logProvider.ReadLogs(start, rowsVis).ToList();

        var tbl = new Table().NoBorder().Expand().HideHeaders();
        tbl.AddColumn(new TableColumn("[bold]TIME[/]") { NoWrap = true, Width = 7, Padding = new Padding(1) });
        tbl.AddColumn(new TableColumn("[bold]LVL[/]") { NoWrap = true, Width = 2, Padding = new Padding(1) });
        tbl.AddColumn(new TableColumn("[bold]MESSAGE[/]") { NoWrap = false, Width = Console.WindowWidth - 30, Padding = new Padding(0) });

        // Render from the slice; the newest entry appears at the bottom of the table
        foreach (var log in slice)
        {
            var msgText = new Text(log.Message ?? string.Empty);
            IRenderable msgCell = log.Exception == null
                ? msgText
                : new Rows(msgText, log.Exception.GetRenderable(new ExceptionSettings { Format = ExceptionFormats.ShortenEverything }));

            tbl.AddRow(
                new Markup(log.Timestamp.ToString("HH:mm:ss")),
                new Markup(log.Level.GetLevelMarkup()),
                msgCell);
        }

        if (slice.Count == 0)
        {
            tbl.AddRow(Text.Empty, new Text("No logs available", Color.Grey), Text.Empty, Text.Empty);
        }

        return new Panel(tbl)
            .Header(new PanelHeader($"[##00ffff] logs([magenta1]all[/])[[[white]{_logProvider!.CountLogs()}[/]]] [/]", Justify.Center));
    }

}