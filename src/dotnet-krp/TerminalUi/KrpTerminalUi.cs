using k8s;
using Krp.Endpoints;
using Krp.Endpoints.PortForward;
using Krp.Tool.TerminalUi.Extensions;
using Krp.Tool.TerminalUi.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Krp.Tool.TerminalUi;

public enum WindowSize { XS, SM, MD, LG }
public enum KrpTable { PortForwards, Logs }

public class KrpTerminalUi
{
    private const int HEADER_SIZE = 8;
    private const int CROP_MARGIN = 4;      // Space (chars) treated as "near edge".
    private const int MIN_COL_WIDTH = 5;    // Space (chars) for minimum column width.

    private readonly Dictionary<string, int> _measurementLookup = new();
    private readonly Regex _spectreMarkup = new(@"\[[^\]]+?]", RegexOptions.Compiled);
    private readonly Regex _spectreEmoji = new(@":[\w+\-]+?:", RegexOptions.Compiled);
    private readonly string _version = Assembly.GetExecutingAssembly().GetName().Version!.ToString();
    private readonly EndpointManager _endpointManager;
    private readonly InMemoryLoggingProvider _logProvider;
    private readonly FigletFont _logoFont;

    // Sorting
    private SortField _sortField;
    private bool _sortAsc;

    // Table state
    private KrpTable _selectedTableIndex;
    private readonly Dictionary<KrpTable, int> _selectedRowIndex;
    private int _columnOffset;              // Index of first visible column.
    private int _columnOffsetMax;           // Right‑most allowed offset.
    private bool _lastColumnClipped;        // True when right‑most col is trimmed.

    // Window state
    private bool _windowGrew;               // True when console width increased.
    private int _windowWidth;               // Current Console.WindowWidth to prevent interops.           
    private int _windowHeight;              // Current Console.WindowHeight to prevent interops.
    private WindowSize _windowSize;         // Current window size category (XS, SM, MD, LG), used for responsive 

    private string _kubeCurrentContext;

    public KrpTerminalUi(EndpointManager endpointManager, InMemoryLoggingProvider inMemoryLoggingProvider)
    {
        _endpointManager = endpointManager;
        _logProvider = inMemoryLoggingProvider;
        _logoFont = FigletFont.Load(Assembly.GetExecutingAssembly().GetManifestResourceStream($"{typeof(KrpTerminalUi).Namespace}.Fonts.3D.flf") ?? throw new InvalidOperationException("Embedded font '3D.flf' not found."));
        _sortField = SortField.PortForward;
        _selectedRowIndex = new Dictionary<KrpTable, int>
        {
            { KrpTable.Logs, 0 },
            { KrpTable.PortForwards, 0 },
        };
    }

    public async Task RunUiAsync()
    {
        var kubeCfg = await KubernetesClientConfiguration.LoadKubeConfigAsync();
        _kubeCurrentContext = kubeCfg.CurrentContext ?? "unknown";
        var handlerCount = _endpointManager.GetAllHandlers().Count();
        var handlerActiveCount = 0;
        var logsCount = _logProvider.CountLogs();
        var baseW = Console.WindowWidth;
        var baseH = Console.WindowHeight;

        while (true)
        {
            var init = true;
            try
            {
                _windowWidth = Console.WindowWidth;
                _windowHeight = Console.WindowHeight;

                var layout = BuildLayout();
                await AnsiConsole.Live(layout).StartAsync(async ctx =>
                {
                    var lastCtx = DateTime.MinValue;
                    while (true)
                    {
                        try
                        {
                            _windowWidth = Console.WindowWidth;
                            _windowHeight = Console.WindowHeight;

                            var count = _selectedTableIndex switch
                            {
                                KrpTable.PortForwards => handlerCount,
                                KrpTable.Logs => logsCount,
                                _ => 0,
                            };

                            var redraw = init;
                            var redrawInfo = false;

                            // Keyboard handling.
                            if (Console.KeyAvailable)
                            {
                                var key = Console.ReadKey(true);
                                var shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

                                switch (key.Key)
                                {
                                    case ConsoleKey.Home: _selectedRowIndex[_selectedTableIndex] = 0; redraw = true; break;
                                    case ConsoleKey.End: _selectedRowIndex[_selectedTableIndex] = count - 1; redraw = true; break;
                                    case ConsoleKey.LeftArrow: _columnOffset = Math.Max(0, _columnOffset - 1); redraw = true; break;
                                    case ConsoleKey.RightArrow: _columnOffset = Math.Min(_columnOffset + 1, _columnOffsetMax); redraw = true; break;
                                    case ConsoleKey.UpArrow: _selectedRowIndex[_selectedTableIndex] = Math.Max(0, _selectedRowIndex[_selectedTableIndex] - 1); redraw = true; break;
                                    case ConsoleKey.DownArrow: _selectedRowIndex[_selectedTableIndex] = Math.Min(Math.Max(0, count - 1), _selectedRowIndex[_selectedTableIndex] + 1); redraw = true; break;
                                    case ConsoleKey.D1: _selectedTableIndex = KrpTable.PortForwards; redraw = true; break;
                                    case ConsoleKey.D2: _selectedTableIndex = KrpTable.Logs; redraw = true; break;
                                    case ConsoleKey.I when shift: ToggleSort(SortField.Ip, ref redraw); break;
                                    case ConsoleKey.N when shift: ToggleSort(SortField.Namespace, ref redraw); break;
                                    case ConsoleKey.P when shift: ToggleSort(SortField.PortForward, ref redraw); break;
                                    case ConsoleKey.R when shift: ToggleSort(SortField.Resource, ref redraw); break;
                                    case ConsoleKey.U when shift: ToggleSort(SortField.Url, ref redraw); break;
                                    case ConsoleKey.F5: return; // Abort inner loop to force a new AnsiConsole.Live instance, forcing a refresh.
                                }
                            }

                            // Context change check (1s).
                            if (!redraw && DateTime.UtcNow - lastCtx >= TimeSpan.FromSeconds(1))
                            {
                                var cfg = await KubernetesClientConfiguration.LoadKubeConfigAsync();
                                if (cfg.CurrentContext != _kubeCurrentContext)
                                {
                                    _kubeCurrentContext = cfg.CurrentContext ?? "unknown";
                                    redrawInfo = true;
                                }

                                lastCtx = DateTime.UtcNow;
                            }

                            // Handlers count.
                            var newHandlersCount = _endpointManager.GetAllHandlers().Count();
                            if (!redraw && newHandlersCount != handlerCount)
                            {
                                handlerCount = newHandlersCount;
                                _selectedRowIndex[KrpTable.PortForwards] = Math.Clamp(_selectedRowIndex[_selectedTableIndex], 0, Math.Max(0, newHandlersCount - 1));
                                redraw = true;
                            }

                            // Active handlers.
                            var newHandlersActiveCount = _endpointManager.GetAllHandlers().OfType<PortForwardEndpointHandler>().Count(x => x.IsActive);
                            if (!redraw && newHandlersActiveCount != handlerActiveCount)
                            {
                                handlerActiveCount = newHandlersActiveCount;
                                redraw = true;
                            }

                            // Logs count.
                            var newLogsCount = _logProvider.CountLogs();
                            if (!redraw && newLogsCount != logsCount)
                            {
                                logsCount = newLogsCount;
                                _selectedRowIndex[KrpTable.Logs] = 0;
                                redraw = true;
                            }

                            // Window resizing.
                            if (init || (!redraw && (_windowWidth != baseW || _windowHeight != baseH)))
                            {
                                try
                                {
#pragma warning disable CA1416
                                    Console.SetBufferSize(Console.WindowLeft + _windowWidth, Console.WindowTop + _windowHeight);
#pragma warning restore CA1416
                                }
                                catch
                                {
                                    // ignored
                                }
                                                         
                                var previousWindowSize = _windowSize;

                                _windowGrew = _windowWidth > baseW;
                                _windowSize = _windowWidth switch
                                {
                                    < 75 => WindowSize.XS,
                                    < 100 => WindowSize.SM,
                                    < 130 => WindowSize.MD,
                                    _ => WindowSize.LG,
                                };

                                baseW = _windowWidth;
                                baseH = _windowHeight;
                                
                                if (_windowSize != previousWindowSize)
                                {
                                    return;
                                }

                                redraw = true;
                            }

                            // Redraw panels.
                            if (redraw)
                            {
                                layout["main"].Update(BuildMainPanel());
                                ctx.Refresh();
                                init = false;
                            }

                            if (redrawInfo)
                            {
                                layout["info"].Update(BuildInfoPanel());
                                ctx.Refresh();
                            }

                            await Task.Delay(1);
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            }
        }
    }

    private Layout BuildLayout()
    {
        var root = new Layout("root");

        root.SplitRows(
                new Layout("header") { Size = HEADER_SIZE },
                new Layout("main"));
    
        root["header"].SplitColumns(
            new Layout("logo") { Ratio = 5 },
            new Layout("info") { Ratio = 3, IsVisible = _windowSize != WindowSize.XS },
            new Layout("menu") { Ratio = 2, IsVisible = _windowSize > WindowSize.SM },
            new Layout("contextmenu") { Ratio = 3, IsVisible = _windowSize > WindowSize.MD });

        root["logo"].Update(BuildLogoPanel());
        root["info"].Update(BuildInfoPanel());
        root["menu"].Update(BuildMenuPanel());
        root["contextmenu"].Update(BuildContextMenuPanel());
        root["main"].Update(BuildMainPanel());
        return root;
    }

    private Panel BuildMainPanel()
    {
        var panel = _selectedTableIndex switch
        {
            KrpTable.PortForwards => BuildTablePanel(),
            KrpTable.Logs => BuildLogsPanel(),
            _ => throw new ArgumentOutOfRangeException(nameof(_selectedTableIndex), _selectedTableIndex, "Invalid selected table index."),
        };
        return panel.NoBorder().BorderColor(new Color(136, 206, 250)).Border(BoxBorder.Square).Padding(1, 0, 0, 1).Expand();
    }

    private Panel BuildInfoPanel()
    {
        var panel = new Panel(new Table()
            .NoBorder().HideHeaders()
            .AddColumn("")
            .AddColumn("")
            .AddRow(new Text("context:", Color.Orange1) { Overflow = Overflow.Ellipsis }, new Text(_kubeCurrentContext, Color.White) { Overflow = Overflow.Ellipsis })
            .AddRow(new Text("version:", Color.Orange1) { Overflow = Overflow.Ellipsis }, new Text(_version, Color.White) { Overflow = Overflow.Ellipsis })
            .AddRow(new Text("last refresh:", Color.Orange1) { Overflow = Overflow.Ellipsis }, new Text(DateTime.Now.ToString("HH:mm:ss"), Color.White) { Overflow = Overflow.Ellipsis }));
        return panel.NoBorder().Padding(0, 0, 0, 0).HeaderAlignment(Justify.Left);
    }

    private Panel BuildMenuPanel()
    {
        var panel = new Panel(new Table()
            .NoBorder().HideHeaders()
            .AddColumn("")
            .AddColumn("")
            .AddRow(new Text("<1>", Color.Magenta1) { Overflow = Overflow.Ellipsis }, new Text("port-forwards", Color.White) { Overflow = Overflow.Ellipsis })
            .AddRow(new Text("<2>", Color.Magenta1) { Overflow = Overflow.Ellipsis }, new Text("logs", Color.White) { Overflow = Overflow.Ellipsis })
            .AddRow(new Text("<?>", Color.Magenta1) { Overflow = Overflow.Ellipsis }, new Text("help", Color.White) { Overflow = Overflow.Ellipsis }));
        return panel.NoBorder().Padding(0, 0, 0, 0).HeaderAlignment(Justify.Left);
    }

    private Panel BuildContextMenuPanel()
    {
        var panel = new Panel(new Table()
            .NoBorder().HideHeaders()
            .AddColumn("")
            .AddColumn("")
            .AddRow(new Text("<ctrl+enter>", "#1E90FF") { Overflow = Overflow.Ellipsis }, new Text("force start", Color.White) { Overflow = Overflow.Ellipsis, Justification = Justify.Left })
            .AddRow(new Text("<ctrl+del>", "#1E90FF") { Overflow = Overflow.Ellipsis }, new Text("force stop", Color.White) { Overflow = Overflow.Ellipsis, Justification = Justify.Left }));
        return panel.NoBorder().Padding(0, 0, 0, 0).HeaderAlignment(Justify.Left);
    }

    private Panel BuildLogoPanel()
    {
        var figlet = new FigletText(_logoFont, "KRP").Color(Color.Orange1);
        var panel = new Panel(figlet);

        if (_windowSize == WindowSize.XS)
        {
            figlet.Centered();
        }

        return panel.NoBorder().Padding(1, 0, 0, 0);
    }

    private Panel BuildLogsPanel()
    {
        var fixedRows = 2 + HEADER_SIZE;
        var rowsVis = Math.Max(1, _windowHeight - fixedRows);

        var all = _logProvider!.ReadLogs(0, int.MaxValue).ToList();
        var total = all.Count;

        // max starting index so the window stays within bounds
        var maxStart = Math.Max(0, total - rowsVis);

        // _selectedRowIndex is "start row from the top"
        // Down (index++)  => start increases => scrolls down (newer)
        // Up   (index--)  => start decreases => scrolls up   (older)
        _selectedRowIndex[KrpTable.Logs] = Math.Clamp(_selectedRowIndex[KrpTable.Logs], 0, maxStart);

        if (_selectedRowIndex[KrpTable.Logs] == 0) // first-time / follow-tail state
        {
            _selectedRowIndex[KrpTable.Logs] = maxStart;  // start at the tail
        }

        var start = _selectedRowIndex[_selectedTableIndex];
        var slice = all.Skip(start).Take(rowsVis).ToList();

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
    
    private Panel BuildTablePanel()
    {
        var items = _endpointManager.GetAllHandlers().OfType<PortForwardEndpointHandler>().ToList().Sort(_sortField, _sortAsc);
        var columns = new List<(string header, int width, SortField sort, Func<PortForwardEndpointHandler, string> valueSelector, bool allowGrow)>
        {
            ("[bold]PF[/]",        0, SortField.PortForward,h => h.IsActive ? "[magenta1]⬤[/]" : "", false),
            ("[bold]RESOURCE[/]",  0, SortField.Resource,   h => h.Resource, true),
            ("[bold]NAMESPACE[/]", 0, SortField.Namespace,  h => h.Namespace, true),
            ("[bold]URL[/]",       0, SortField.Url,        h => h.Url , true),
            ("[bold]IP[/]",        0, SortField.Ip,         h => h.LocalIp.ToString(), true),
        };

        var total = columns.Count;
        var totalVisibleColumns = CalculateColumns(items, columns, out var slack);

        // Auto‑reveal hidden columns when console grows.
        if (_windowGrew)
        {
            while (_columnOffset > 0)
            {
                var idx = _columnOffset - 1;
                var natural = Math.Max(4, Math.Max(VisibleLen(columns[idx].header), items.Any() ? items.Max(h => VisibleLen(columns[idx].valueSelector(h))) : 0));
                if (natural > slack)
                {
                    break;
                }

                _columnOffset--;
                columns = columns.Select(c => (c.header, 0, c.sort, c.valueSelector, c.allowGrow)).ToList();
                totalVisibleColumns = CalculateColumns(items, columns, out slack);
            }
            _windowGrew = false;
        }

        _columnOffsetMax = Math.Max(0, total - totalVisibleColumns + (_lastColumnClipped ? 1 : 0));

        // Rows that fit.
        var fixedRows = 3 + HEADER_SIZE;
        var maxRows = Math.Max(1, _windowHeight - fixedRows);

        var tbl = new Table().NoBorder();

        // Print columns
        var shownCols = columns.Skip(_columnOffset).Take(totalVisibleColumns).ToList();
        foreach (var col in shownCols)
        {
            var column = new TableColumn($"{col.header}{(_sortField == col.sort ? _sortAsc ? "[#00ffff]↑[/]" : "[#00ffff]↓[/]" : "")}")
            {
                Width = col.width,
                NoWrap = true,
                Padding = new Padding(0),
            };

            tbl.AddColumn(column);
        }
        
        // Print rows
        var first = Math.Clamp(_selectedRowIndex[_selectedTableIndex] - maxRows + 1, 0, Math.Max(0, items.Count - maxRows));
        for (var i = first; i < Math.Min(items.Count, first + maxRows); i++)
        {
            var isSelected = i == _selectedRowIndex[_selectedTableIndex];
            var cells = new List<Markup>();

            foreach (var col in shownCols)
            {
                // Pad selected cells to the column width so the highlight covers the entire cell.
                // This is done inside markup so Spectre doesn't trim the spaces.
                var cell = new Markup(isSelected 
                    ? $"[black on #87cefa]{RightPad(col.valueSelector(items[i]), col.width)}[/]" 
                    : $"[#87cefa]{col.valueSelector(items[i])}[/]")
                {
                    Overflow = Overflow.Crop,
                };
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
    /// Calculates column widths and returns how many columns are visible starting at <see cref="_columnOffset"/>.
    /// - Columns are never removed.
    /// - Columns never shrink below natural width.
    /// - Leftover space is shared evenly across visible columns (adaptive).
    /// - Always returns ≥ 1 so something is shown in a narrow window.
    /// - Sets <see cref="_lastColumnClipped"/> if the right-most column is cropped or near the margin.
    /// - Outputs <paramref name="slackBeforeSpread"/> as free space before spreading.
    /// </summary>
    private int CalculateColumns<T>(List<T> items, List<(string header, int width, SortField sort, Func<T, string> valueSelector, bool allowGrow)> columns, out int slackBeforeSpread)
    {
        var n = columns.Count;
        _lastColumnClipped = false; // reset every call

        // 1. Natural width per column.
        // TODO: Optimize using dictionary cache and only recompute when row changes.
        var nat = new int[n];
        for (var i = 0; i < n; i++)
        {
            var col = columns[i];
            var header = VisibleLen(col.header);
            var longest = items.Any() ? items.Max(h => VisibleLen(col.valueSelector(h))) : 0;
            nat[i] = Math.Max(MIN_COL_WIDTH, Math.Max(header, longest));
        }

        // 2. Build the slice that fits. Track remaining width.
        var visibleIdx = new List<int>();
        var remain = _windowWidth;

        for (var i = _columnOffset; i < n && remain > 0; i++)
        {
            var shown = Math.Min(nat[i], remain);
            columns[i] = (columns[i].header, shown, columns[i].sort, columns[i].valueSelector, columns[i].allowGrow);
            visibleIdx.Add(i);
            remain -= shown;
        }

        // Ensure at least one column is shown.
        if (visibleIdx.Count == 0 && _columnOffset < n)
        {
            var shown = Math.Min(nat[_columnOffset], _windowWidth);
            columns[_columnOffset] = (columns[_columnOffset].header, shown, columns[_columnOffset].sort, columns[_columnOffset].valueSelector, columns[_columnOffset].allowGrow);
            visibleIdx.Add(_columnOffset);
            remain = _windowWidth - shown;
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
        var nearMargin = slack <= CROP_MARGIN;
        _lastColumnClipped = cropped || nearMargin;

        // 5. Clamp offset and return count.
        _columnOffset = Math.Clamp(_columnOffset, 0, n - 1);
        return visibleIdx.Count;
    }

    private void ToggleSort(SortField field, ref bool redraw)
    {
        _sortAsc = _sortField != field || !_sortAsc;
        _sortField = field;
        _selectedRowIndex[_selectedTableIndex] = 0; // Reset cursor to top.
        redraw = true;
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
}
