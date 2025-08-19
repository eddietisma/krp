using Krp.Endpoints;
using Krp.Endpoints.PortForward;
using Krp.Tool.TerminalUi.Extensions;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Krp.Tool.TerminalUi.Tables;

public class PortForwardTable
{
    private readonly KrpTerminalState _state;
    private readonly EndpointManager _endpointManager;
    
    private readonly Regex _spectreMarkup = new(@"\[[^\]]+?]", RegexOptions.Compiled);
    private readonly Regex _spectreEmoji = new(@":[\w+\-]+?:", RegexOptions.Compiled);
    private readonly Dictionary<string, int> _measurementLookup = new();
    
    private int _handlersActiveCount;

    public int Count { get; private set; }

    public PortForwardTable(KrpTerminalState state, EndpointManager endpointManager)
    {
        _state = state;
        _endpointManager = endpointManager;

        _state.SelectedRow.Add(KrpTable.PortForwards, 0); // Initialize selected row for port forwards.
    }

    public bool DetectChanges()
    {
        var handlers = _endpointManager.GetAllHandlers().ToList();

        var newHandlersCount = handlers.Count();
        if (newHandlersCount != Count)
        {
            Count = newHandlersCount;
            _state.SelectedRow[KrpTable.PortForwards] = Math.Clamp(_state.SelectedRow[_state.SelectedTable], 0, Math.Max(0, newHandlersCount - 1));
            return true;
        }

        var newHandlersActiveCount = handlers.OfType<PortForwardEndpointHandler>().Count(x => x.IsActive);
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
        var columns = new List<(string header, int width, SortField sort, Func<PortForwardEndpointHandler, string> valueSelector, bool allowGrow)>
        {
            ("[bold]PF[/]", 0, SortField.PortForward, h => h.IsActive ? "[magenta1]⬤[/]" : "", false),
            ("[bold]RESOURCE[/]", 0, SortField.Resource, h => h.Resource, true),
            ("[bold]NAMESPACE[/]", 0, SortField.Namespace, h => h.Namespace, true),
            ("[bold]URL[/]", 0, SortField.Url, h => h.Url, true),
            ("[bold]IP[/]", 0, SortField.Ip, h => h.LocalIp.ToString(), true),
        };

        var total = columns.Count;
        var totalVisibleColumns = CalculateColumns(items, columns, out var slack);

        // Auto‑reveal hidden columns when console grows.
        if (_state.WindowGrew)
        {
            while (_state.ColumnOffset > 0)
            {
                var idx = _state.ColumnOffset - 1;
                var natural = Math.Max(4, Math.Max(VisibleLen(columns[idx].header), items.Any() ? items.Max(h => VisibleLen(columns[idx].valueSelector(h))) : 0));
                if (natural > slack)
                {
                    break;
                }

                _state.ColumnOffset--;
                columns = columns.Select(c => (c.header, 0, c.sort, c.valueSelector, c.allowGrow)).ToList();
                totalVisibleColumns = CalculateColumns(items, columns, out slack);
            }

            _state.WindowGrew = false;
        }

        _state.ColumnOffsetMax = Math.Max(0, total - totalVisibleColumns + (_state.LastColumnClipped ? 1 : 0));

        // Rows that fit.
        var fixedRows = 3 + KrpTerminalUi.HEADER_SIZE;
        var maxRows = Math.Max(1, _state.WindowHeight - fixedRows);

        var tbl = new Table().NoBorder();

        // Print columns
        var shownCols = columns.Skip(_state.ColumnOffset).Take(totalVisibleColumns).ToList();
        foreach (var col in shownCols)
        {
            var column = new TableColumn($"{col.header}{(_state.SortField == col.sort ? _state.SortAscending ? "[#00ffff]↑[/]" : "[#00ffff]↓[/]" : "")}") { Width = col.width, NoWrap = true, Padding = new Padding(0), };

            tbl.AddColumn(column);
        }

        // Print rows
        var first = Math.Clamp(_state.SelectedRow[_state.SelectedTable] - maxRows + 1, 0, Math.Max(0, items.Count - maxRows));
        for (var i = first; i < Math.Min(items.Count, first + maxRows); i++)
        {
            var isSelected = i == _state.SelectedRow[_state.SelectedTable];
            var cells = new List<Markup>();

            foreach (var col in shownCols)
            {
                // Pad selected cells to the column width so the highlight covers the entire cell.
                // This is done inside markup so Spectre doesn't trim the spaces.
                var cell = new Markup(isSelected
                    ? $"[black on #87cefa]{RightPad(col.valueSelector(items[i]), col.width)}[/]"
                    : $"[#87cefa]{col.valueSelector(items[i])}[/]") { Overflow = Overflow.Crop, };
                cells.Add(cell);
            }

            var row = new TableRow(cells.ToArray());
            tbl.AddRow(row);
        }

        if (!items.Any())
        {
            AnsiConsole.Write(new Align(
                new Text("Spectre!"),
                HorizontalAlignment.Left,
                VerticalAlignment.Bottom
            ));

            //tbl.AddRow(Text.Empty, new Text("No endpoints available", Color.Grey), Text.Empty, Text.Empty, Text.Empty);
        }

        return new Panel(tbl)
            .Header(new PanelHeader($"[##00ffff] port-forwards([magenta1]all[/])[[[white]{items.Count}[/]]] [/]", Justify.Center));
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
    private int CalculateColumns<T>(List<T> items, List<(string header, int width, SortField sort, Func<T, string> valueSelector, bool allowGrow)> columns, out int slackBeforeSpread)
    {
        var n = columns.Count;
        _state.LastColumnClipped = false; // reset every call

        // 1. Natural width per column.
        // TODO: Optimize using dictionary cache and only recompute when row changes.
        var nat = new int[n];
        for (var i = 0; i < n; i++)
        {
            var col = columns[i];
            var header = VisibleLen(col.header);
            var longest = items.Any() ? items.Max(h => VisibleLen(col.valueSelector(h))) : 0;
            nat[i] = Math.Max(KrpTerminalUi.MIN_COL_WIDTH, Math.Max(header, longest));
        }

        // 2. Build the slice that fits. Track remaining width.
        var visibleIdx = new List<int>();
        var remain = _state.WindowWidth;

        for (var i = _state.ColumnOffset; i < n && remain > 0; i++)
        {
            var shown = Math.Min(nat[i], remain);
            columns[i] = (columns[i].header, shown, columns[i].sort, columns[i].valueSelector, columns[i].allowGrow);
            visibleIdx.Add(i);
            remain -= shown;
        }

        // Ensure at least one column is shown.
        if (visibleIdx.Count == 0 && _state.ColumnOffset < n)
        {
            var shown = Math.Min(nat[_state.ColumnOffset], _state.WindowWidth);
            columns[_state.ColumnOffset] = (columns[_state.ColumnOffset].header, shown, columns[_state.ColumnOffset].sort, columns[_state.ColumnOffset].valueSelector, columns[_state.ColumnOffset].allowGrow);
            visibleIdx.Add(_state.ColumnOffset);
            remain = _state.WindowWidth - shown;
        }

        var slack = remain;   // ← FREE SPACE **before** we spread it

        // 3. Capture slack before spreading and then spread evenly.
        slackBeforeSpread = slack; // Free space before spreading.
        if (slack > 0 && visibleIdx.Count > 0)
        {
            // Only spread to columns that can grow.
            var growable = visibleIdx.Where(i => columns[i].allowGrow).ToList();
            if (growable.Count > 0)
            {
                var even = slack / growable.Count;
                var leftover = slack % growable.Count;

                foreach (var idx in growable)
                {
                    var c = columns[idx];
                    // TODO: Bug here causing rows to not be fully highlighted.
                    c.width += even + (leftover-- > 0 ? 1 : 0);
                    columns[idx] = c;
                }
            }
        }

        // 4. Flag when the right-most column is cropped or near the edge.
        var last = visibleIdx[^1];
        var cropped = columns[last].width < nat[last];
        var nearMargin = slack <= KrpTerminalUi.CROP_MARGIN;
        _state.LastColumnClipped = cropped || nearMargin;

        // 5. Clamp offset and return count.
        _state.ColumnOffset = Math.Clamp(_state.ColumnOffset, 0, n - 1);
        return visibleIdx.Count;
    }

    private int VisibleLen(string markup)
    {
        // Fast path: Plain text (no Spectre markup or emoji) uses raw length.
        if (!_spectreMarkup.IsMatch(markup) && !_spectreEmoji.IsMatch(markup))
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

    private string RightPad(string s, int w)
    {
        var len = VisibleLen(s);
        var padLength = w;

        if (s.Length > len)
        {
            padLength = w + s.Length - len;
            //padLength = w - len;
            //padLength = w;
        }
        //else
        //{
        //    padLength -= 1;
        //}

        var result = s.PadRight(padLength);
        return result;
    }
}