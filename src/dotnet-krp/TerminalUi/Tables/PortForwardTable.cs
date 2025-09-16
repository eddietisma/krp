using Krp.Endpoints;
using Krp.Endpoints.PortForward;
using Krp.Tool.TerminalUi.Extensions;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Krp.Tool.TerminalUi.Tables;

public class PortForwardTable
{
    private readonly KrpTerminalState _state;
    private readonly EndpointManager _endpointManager;
    private readonly Dictionary<string, int> _measurementLookup = new();
    private readonly List<ColumnDefinition<PortForwardEndpointHandler>> _columnDefinitions;

    private int _handlersActiveCount;
    public int Count { get; private set; }

    public PortForwardTable(KrpTerminalState state, EndpointManager endpointManager)
    {
        _state = state;
        _endpointManager = endpointManager;

        // Initialize selected row for port forwards.
        _state.SelectedRow.Add(KrpTable.PortForwards, 0);

        // Initialize column definitions.
        _columnDefinitions =
        [
            new ColumnDefinition<PortForwardEndpointHandler>("[bold]PF[/]", 0, SortField.PortForward, h => h.IsActive ? "[magenta1]⬤[/]" : "", false),
            new ColumnDefinition<PortForwardEndpointHandler>("[bold]RESOURCE[/]", 0, SortField.Resource, h => h.Resource, true),
            new ColumnDefinition<PortForwardEndpointHandler>("[bold]NAMESPACE[/]", 0, SortField.Namespace, h => h.Namespace, true),
            new ColumnDefinition<PortForwardEndpointHandler>("[bold]URL[/]", 0, SortField.Url, h => h.Url, true),
            new ColumnDefinition<PortForwardEndpointHandler>("[bold]IP[/]", 0, SortField.Ip, h => h.LocalIp.ToString(), true),
        ];
    }

    public bool DetectChanges()
    {
        var handlers = _endpointManager.GetAllHandlers().OfType<PortForwardEndpointHandler>().ToList();

        var newHandlersCount = handlers.Count;
        if (newHandlersCount != Count)
        {
            Count = newHandlersCount;
            _state.SelectedRow[KrpTable.PortForwards] = Math.Clamp(_state.SelectedRow[_state.SelectedTable], 0, Math.Max(0, newHandlersCount - 1));
            return true;
        }

        var newHandlersActiveCount = handlers.Count(x => x.IsActive);
        if (newHandlersActiveCount != _handlersActiveCount)
        {
            _handlersActiveCount = newHandlersActiveCount;
            return true;
        }

        return false;
    }

    public Panel BuildPanel()
    {
        var items = _endpointManager.GetAllHandlers().OfType<PortForwardEndpointHandler>().ToList().Sort(_state.SortField, _state.SortAscending);
        var totalVisibleColumns = CalculateColumns(items, out var slack);

        // Auto‑reveal hidden columns when console grows.
        if (_state.WindowGrew)
        {
            while (_state.ColumnOffset > 0)
            {
                var idx = _state.ColumnOffset - 1;
                var natural = Math.Max(4, Math.Max(VisibleLen(_columnDefinitions[idx].Header), items.Any() ? items.Max(h => VisibleLen(_columnDefinitions[idx].ValueSelector(h))) : 0));
                if (natural > slack)
                {
                    break;
                }

                _state.ColumnOffset--;
                _columnDefinitions[idx].Width = 0;
                totalVisibleColumns = CalculateColumns(items, out slack);
            }

            _state.WindowGrew = false;
        }

        _state.ColumnOffsetMax = Math.Max(0, _columnDefinitions.Count - totalVisibleColumns + (_state.LastColumnClipped ? 1 : 0));

        // Rows that fit.
        var fixedRows = 3 + KrpTerminalUi.HEADER_SIZE;
        var maxRows = Math.Max(1, _state.WindowHeight - fixedRows);

        var tbl = new Table().NoBorder();

        // Print columns
        var shownCols = _columnDefinitions.Skip(_state.ColumnOffset).Take(totalVisibleColumns).ToList();

        foreach (var col in shownCols)
        {
            var column = new TableColumn($"{col.Header}{(_state.SortField == col.Sort ? _state.SortAscending ? "[#00ffff]↑[/]" : "[#00ffff]↓[/]" : "")}")
            {
                Width = col.Width,
                NoWrap = true,
                Padding = new Padding(0),
            };

            tbl.AddColumn(column);
        }

        // Print rows
        var first = Math.Clamp(_state.SelectedRow[_state.SelectedTable] - maxRows + 1, 0, Math.Max(0, items.Count - maxRows));
        for (var i = first; i < Math.Min(items.Count, first + maxRows); i++)
        {
            var isSelected = i == _state.SelectedRow[_state.SelectedTable];
            var cells = new List<IRenderable>();

            foreach (var col in shownCols)
            {
                // Pad selected cells with NBSP so Spectre preserves trailing fill and the
                // background highlight covers the entire cell width.
                var text = col.ValueSelector(items[i]);
                var cell = isSelected
                    ? new Markup(text.PadRight(col.Width), new Style(Color.Black, new Color(135, 206, 250))) { Overflow = Overflow.Crop }
                    : new Markup(text, new Style(foreground: new Color(135, 206, 250))) { Overflow = Overflow.Crop };
                cells.Add(cell);
            }

            var row = new TableRow(cells.ToArray());
            tbl.AddRow(row);
        }

        if (!items.Any())
        {
            tbl.AddRow(Text.Empty, new Text("No endpoints available", Color.Grey), Text.Empty, Text.Empty, Text.Empty);
        }

        return new Panel(tbl)
            .Header(new PanelHeader($"[##00ffff] port-forwards([magenta1]all[/])[[[white]{items.Count}[/]]] [/]", Justify.Center));
    }

    public Panel BuildContextMenuPanel()
    {
        var panel = new Panel(new Table()
            .NoBorder().HideHeaders()
            .AddColumn("")
            .AddColumn("")
            .AddRow(new Text("<enter>", "#1E90FF") { Overflow = Overflow.Ellipsis }, new Text("force start", Color.White) { Overflow = Overflow.Ellipsis, Justification = Justify.Left })
            .AddRow(new Text("<ctrl+d>", "#1E90FF") { Overflow = Overflow.Ellipsis }, new Text("force stop", Color.White) { Overflow = Overflow.Ellipsis, Justification = Justify.Left }));
        return panel;
    }

    public async Task ForceStart()
    {
        var handlers = _endpointManager.GetAllHandlers().OfType<PortForwardEndpointHandler>().ToList().Sort(_state.SortField, _state.SortAscending);
        var selected = _state.SelectedRow[KrpTable.PortForwards];

        if (selected < 0 || selected >= handlers.Count)
        {
            return; // Invalid index
        }

        var handler = handlers[selected];
        if (!handler.IsActive)
        {
            await handler.EnsureRunningAsync();
        }
    }

    public void ForceStop()
    {
        var handlers = _endpointManager.GetAllHandlers().OfType<PortForwardEndpointHandler>().ToList().Sort(_state.SortField, _state.SortAscending);
        var selected = _state.SelectedRow[KrpTable.PortForwards];

        if (selected < 0 || selected >= handlers.Count)
        {
            return; // Invalid index
        }

        var handler = handlers[selected];
        if (handler.IsActive)
        {
            handler.Dispose();
        }
    }

    /// <summary>
    /// Calculates column widths and returns how many columns are visible starting at <see cref="KrpTerminalState.ColumnOffset"/>.
    /// <para>
    /// - Columns are never removed.<br/>
    /// - Columns never shrink below natural width.<br/>
    /// - Leftover space is shared evenly across visible columns (adaptive).<br/>
    /// - Always returns ≥ 1 so something is shown in a narrow window.<br/>
    /// - Sets <see cref="KrpTerminalState.LastColumnClipped"/> if the right-most column is cropped or near the margin.<br/>
    /// - Outputs <paramref name="slackBeforeSpread"/> as free space before spreading.<br/>
    /// </para>
    /// </summary>
    private int CalculateColumns(List<PortForwardEndpointHandler> items, out int slackBeforeSpread)
    {
        var n = _columnDefinitions.Count;
        _state.LastColumnClipped = false; // reset every call

        // 1. Natural width per column.
        // TODO: Optimize using dictionary cache and only recompute when row changes.
        var nat = new int[n];
        for (var i = 0; i < n; i++)
        {
            var col = _columnDefinitions[i];
            int header = VisibleLen(col.Header);
            var longest = items.Any() ? items.Max(h => VisibleLen(col.ValueSelector(h))) : 0;
            nat[i] = Math.Max(KrpTerminalUi.MIN_COL_WIDTH, Math.Max(header, longest));
        }

        // 2. Build the slice that fits. Track remaining width.
        var visibleIdx = new List<int>();
        var remain = _state.WindowWidth - KrpTerminalUi.TBL_SPACING;

        for (var i = _state.ColumnOffset; i < n && remain > 0; i++)
        {
            var shown = Math.Min(nat[i], remain);
            _columnDefinitions[i].Width = shown;
            visibleIdx.Add(i);
            remain -= shown;
        }

        // Ensure at least one column is shown.
        if (visibleIdx.Count == 0 && _state.ColumnOffset < n)
        {
            var shown = Math.Min(nat[_state.ColumnOffset], _state.WindowWidth);

            _columnDefinitions[_state.ColumnOffset].Width = shown;

            visibleIdx.Add(_state.ColumnOffset);
            remain = _state.WindowWidth - shown;
        }

        var slack = remain;   // ← FREE SPACE **before** we spread it

        // 3. Capture slack before spreading and then spread evenly.
        slackBeforeSpread = slack; // Free space before spreading.
        if (slack > 0 && visibleIdx.Count > 0)
        {
            // Only spread to columns that can grow.
            var growable = visibleIdx.Where(i => _columnDefinitions[i].AllowGrow).ToList();
            if (growable.Count > 0)
            {
                var even = slack / growable.Count;
                var leftover = slack % growable.Count;

                foreach (var idx in growable)
                {
                    _columnDefinitions[idx].Width += even + (leftover-- > 0 ? 1 : 0);
                }
            }
        }

        // 4. Flag when the right-most column is cropped or near the edge.
        var last = visibleIdx[^1];
        var cropped = _columnDefinitions[last].Width < nat[last];
        var nearMargin = slack <= KrpTerminalUi.CROP_MARGIN;
        _state.LastColumnClipped = cropped || nearMargin;

        // 5. Clamp offset and return count.
        _state.ColumnOffset = Math.Clamp(_state.ColumnOffset, 0, n - 1);
        return visibleIdx.Count;
    }

    private int VisibleLen(string markup)
    {
        // Fast path: Plain text (no Spectre markup or emoji) uses raw length.
        if (markup.IndexOf('[') == -1 && markup.IndexOf(':') == -1)
        {
            return markup.Length;
        }

        // Cache hit: Reuse measured visual width.
        if (_measurementLookup.TryGetValue(markup, out var len))
        {
            return len;
        }

        // Slow path: Let Spectre compute rendered cell width (markup affects visual length).
        IRenderable renderable = new Markup(markup);
        var result = renderable
            .Measure(RenderOptions.Create(AnsiConsole.Console), int.MaxValue)
            .Max;

        _measurementLookup.TryAdd(markup, result); // Memoize for next time.
        return result;
    }
}
