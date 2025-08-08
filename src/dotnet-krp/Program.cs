using k8s;
using Krp.DependencyInjection;
using Krp.Dns;
using Krp.Endpoints;
using Krp.Endpoints.PortForward;
using Krp.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Krp.Tool;

public class Program
{
    private const int HEADER_SIZE = 2;

    private enum SortField { Url, Resource, Namespace, Ip, PortForward }
    private static readonly Dictionary<string, int> _measurementLookup = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> _maxLengthLookup = new Dictionary<string, int>();
    private static SortField _sortField = SortField.Url;
    private static readonly string _version = Assembly.GetExecutingAssembly().GetName().Version!.ToString();
    private static readonly Regex _spectreMarkup = new Regex(@"\[[^\]]+?]", RegexOptions.Compiled);
    private static readonly Regex _spectreEmoji = new(@":[\w+\-]+?:", RegexOptions.Compiled);
    private static bool _sortAsc = true;
    private static int _columnOffset;
    private static int _maxColumnOffset;
    private static int _selectedRowIndex;
    private static bool _lastColumnClipped; // allows to increase offset if the rightmost colimn is partially cropped.
    private const int CROPPING_MARGIN = 3;
    private static bool _windowGrew;

    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureLogging(loggingBuilder => loggingBuilder.AddKrpLogger())
            .ConfigureServices((ctx, services) =>
            {
                services
                    .AddKubernetesForwarder(ctx.Configuration)
                     .UseDnsLookup(options => options.Nameserver = "8.8.8.8")
                     .UseRouting(DnsOptions.HostsFile)
                     .UseTcpWithHttpForwarder(o =>
                     {
                         o.ListenAddress = IPAddress.Any;
                         o.ListenPorts = new[] { 80, 443 };
                     })
                     .UseEndpointExplorer(options =>
                    {
                        //options.Filter = [
                        //    "namespace/meetings/*",
                        //    "namespace/*/service/person*",
                        //];
                        options.RefreshInterval = TimeSpan.FromHours(1);
                    });
            })
            .Build();

        var mgr = host.Services.GetRequiredService<EndpointManager>();

        await Task.WhenAll(host.RunAsync(), RunUiAsync(mgr));
    }

    // UI loop that recreates Live on every resize --------------------
    private static async Task RunUiAsync(EndpointManager endpointManager)
    {
        var kubeCfg = await KubernetesClientConfiguration.LoadKubeConfigAsync();
        var kubeContextName = kubeCfg.CurrentContext ?? "unknown";

        var handlerCount = endpointManager.GetAllHandlers().Count();
        var handlerActiveCount = 0;
        var baseW = Console.WindowWidth;
        var baseH = Console.WindowHeight;

        while (true)
        {
            var layout = BuildLayout(kubeContextName, endpointManager);

            await AnsiConsole.Live(layout).StartAsync(async ctx =>
            {
                DateTime lastCtx = DateTime.MinValue;

                while (true) // inner update loop
                {
                    try
                    {
                        var handlers = endpointManager.GetAllHandlers().ToList();
                        var redraw = false;
                        var redrawInfo = false;

                        // ── keyboard handling ────────────────────────────────
                        if (Console.KeyAvailable)
                        {
                            var keyInfo = Console.ReadKey(true);
                            var shift = (keyInfo.Modifiers & ConsoleModifiers.Shift) != 0;
                            var ctrl = (keyInfo.Modifiers & ConsoleModifiers.Control) != 0;

                            switch (keyInfo.Key)
                            {
                                case ConsoleKey.LeftArrow:
                                    _columnOffset = Math.Max(0, _columnOffset - 1);
                                    redraw = true;
                                    break;
                                case ConsoleKey.RightArrow:
                                    //_columnOffset = Math.Min(5, _columnOffset + 1);
                                    _columnOffset = Math.Min(_maxColumnOffset, _columnOffset + 1);
                                    //_columnOffset = Math.Min(5, _columnOffset + 1);
                                    redraw = true;
                                    break;
                                case ConsoleKey.UpArrow:
                                    _selectedRowIndex = Math.Max(0, _selectedRowIndex - 1);
                                    redraw = true;
                                    break;
                                case ConsoleKey.DownArrow:
                                    _selectedRowIndex = Math.Min(endpointManager.GetAllHandlers().Count() - 1, _selectedRowIndex + 1);
                                    redraw = true;
                                    break;

                                case ConsoleKey.I when shift: ToggleSort(SortField.Ip, ref redraw); break;
                                case ConsoleKey.N when shift: ToggleSort(SortField.Namespace, ref redraw); break;
                                case ConsoleKey.P when shift: ToggleSort(SortField.PortForward, ref redraw); break;
                                case ConsoleKey.R when ctrl: return;
                                case ConsoleKey.R when shift: ToggleSort(SortField.Resource, ref redraw); break;
                                case ConsoleKey.U when shift: ToggleSort(SortField.Url, ref redraw); break;
                            }
                        }

                        // kube context every 3 s
                        if (!redraw && DateTime.UtcNow - lastCtx >= TimeSpan.FromSeconds(1))
                        {
                            var cfg = await KubernetesClientConfiguration.LoadKubeConfigAsync();
                            if (cfg.CurrentContext != kubeContextName)
                            {
                                kubeContextName = cfg.CurrentContext ?? "unknown";
                                redrawInfo = true;
                            }

                            lastCtx = DateTime.UtcNow;
                        }

                        // handler list changed?
                        var handlersCount = handlers.Count();
                        if (!redraw && handlersCount != handlerCount)
                        {
                            handlerCount = handlersCount;
                            _selectedRowIndex = Math.Clamp(_selectedRowIndex, 0, Math.Max(0, handlersCount - 1));
                            redraw = true;
                        }

                        // handlers become active?
                        var handlersActiveCount = handlers.OfType<PortForwardEndpointHandler>().Count(x => x.IsActive);
                        if (!redraw && handlersActiveCount != handlerActiveCount)
                        {
                            handlerActiveCount = handlersActiveCount;
                            redraw = true;
                        }

                        // window resized?
                        if ((!redraw && (Console.WindowWidth != baseW || Console.WindowHeight != baseH)))
                        {
                            try
                            {
#pragma warning disable CA1416
                                Console.SetBufferSize(Console.WindowLeft + Console.WindowWidth, Console.WindowTop + Console.WindowHeight);
#pragma warning restore CA1416
                            }
                            catch
                            {
                                // ignored
                            }

                            _windowGrew = Console.WindowWidth > baseW; // used to reset column offset to show now-visible columns after resizing

                            baseW = Console.WindowWidth;
                            baseH = Console.WindowHeight;

                            //// ─── reset horizontal view state ───────────────────────────────
                            //_columnOffset = 0;     // jump back to the first column
                            //_lastColumnClipped = false; // (optional) flag will be recomputed
                            redraw = true;
                        }

                        if (redraw)
                        {
                            var newTable = BuildTablePanel(endpointManager);
                            layout["main"].Update(newTable);
                            layout["main"].Visible();
                            ctx.Refresh();
                        }

                        if (redrawInfo)
                        {
                            var newTable = BuildInfoPanel(kubeContextName);
                            layout["info"].Update(newTable);
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
    }

    // ───────────────────────────────────────────────────
    //  Layout builders
    // ───────────────────────────────────────────────────
    private static Layout BuildLayout(string ctxName, EndpointManager endpointManager)
    {
        var infoPanel = BuildInfoPanel(ctxName);
        var commandPanel = BuildCommandPanel();
        var table = BuildTablePanel(endpointManager);

        var root = new Layout("root");
        root.SplitRows(
            new Layout { Size = HEADER_SIZE }
                .SplitColumns(
                    new Layout("info"),
                    new Layout("commands1"), 
                    new Layout("commands2"), 
                    new Layout("commands3")), 
        new Layout("main"));
        root["info"].Update(infoPanel);
        root["commands1"].Update(commandPanel);
        root["commands2"].Update(commandPanel);
        root["commands3"].Update(commandPanel);
        root["main"].Update(table);
        return root;
    }

    private static Panel BuildInfoPanel(string ctx)
    {
        return new Panel(new Table()
            .NoBorder().HideHeaders()
            .AddColumn("")
            .AddColumn("")
            .AddRow(new Text("context:", new Style(Color.Orange1, Color.Black)), new Text(ctx, Color.White))
            .AddRow(new Text("version:", Color.Orange1), new Text(_version, Color.White))
        ).NoBorder().Padding(1, 0, 0, 0);
    }

    private static Panel BuildCommandPanel()
    {
        return new Panel(new Table()
            .NoBorder().HideHeaders()
            .AddColumn("")
            .AddColumn("")
            .AddRow(new Text("<ctrl+l>", Color.Blue), new Text("logs", Color.White))
            .AddRow(new Text("<ctrl+e>", Color.Blue), new Text("endpoints", Color.White)))
        .NoBorder().Padding(1, 0, 0, 0);
    }

    // ───────────────────────────────────────────────────
    //  Endpoint table (full-row highlighting)
    // ───────────────────────────────────────────────────
    private static Panel BuildTablePanel(EndpointManager mgr)
    {
        // ── 1. data & metadata ───────────────────────────────────────────
        var items = SortHandlers(mgr.GetAllHandlers().OfType<PortForwardEndpointHandler>().ToList());

        var columns = new List<(string header, int width, SortField sort, Func<PortForwardEndpointHandler, string> valueSelector)>
        {
            ("[bold]URL[/]", 0, SortField.Url, h => h.Url),
            ("[bold]PF[/]", 0, SortField.PortForward, h => h.IsActive ? ":black_circle:" : ":white_circle:"),
            ("[bold]RESOURCE[/]", 0, SortField.Resource, h => h.Resource ),
            ("[bold]NAMESPACE[/]", 0, SortField.Namespace, h => h.Namespace),
            ("[bold]IP[/]", 0, SortField.Ip, h => h.LocalIp.ToString()),
        };

        // ── 2. beräkna bredd + slack ─────────────────────────────────────
        int totalColumnCount = columns.Count;
        int visibleColsActive = CalculateColumns(items, columns, out int slack);

        //── 3.flytta vyn vänster så långt slack tillåter ────────────────
        if (_windowGrew)
        {
            while (_columnOffset > 0)
            {
                int prevIdx = _columnOffset - 1;

                // naturlig bredd för kolumnen vi vill återställa
                int needed = Math.Max(
                    4,
                    Math.Max(
                        VisibleLen(columns[prevIdx].header),
                        items.Any()
                            ? items.Max(h => VisibleLen(columns[prevIdx].valueSelector(h)))
                            : 0));

                if (needed > slack)           // får inte plats → bryt
                    break;

                // den ryms → backa en position
                _columnOffset--;

                // nollställ bara breddfältet innan vi räknar om
                columns = columns.Select(c => (c.header, 0, c.sort, c.valueSelector))
                    .ToList();

                visibleColsActive = CalculateColumns(items, columns, out slack);   // slack uppdateras
            }

            _windowGrew = false;   // klart – körs inte igen förrän nästa resize
        }

        _maxColumnOffset = Math.Max(0, totalColumnCount - visibleColsActive + (_lastColumnClipped ? 1 : 0));

        // ── 4. räkna rader som får plats ─────────────────────────────────
        var fixedRows = 3 + HEADER_SIZE;
        var visibleRowsTotal = Math.Max(1, Console.WindowHeight - fixedRows);

        // ── 5. bygg Spectre-tabellen ─────────────────────────────────────
        var tbl = new Table().NoBorder();

        // Print columns
        var cols = columns.Skip(_columnOffset).Take(visibleColsActive).ToList();
        foreach (var col in cols)
        {
            tbl.AddColumn(CreateColumn(col.header, col.width, col.sort));
        }
        
        // Print rows
        var first = Math.Clamp(_selectedRowIndex - visibleRowsTotal + 1, 0, Math.Max(0, items.Count - visibleRowsTotal));
        for (int i = first; i < Math.Min(items.Count, first + visibleRowsTotal); i++)
        {
            var isSelected = i == _selectedRowIndex;
            var cells = new List<Markup>();

            foreach (var col in cols)
            {
                cells.Add(CreateCell(col.valueSelector(items[i]), col.width, isSelected));
            }

            var row = new TableRow(cells.ToArray());
            tbl.AddRow(row);
        }
        
        if (!items.Any())
        {
            tbl.AddRow(new Text("No endpoints available", Color.Grey), Text.Empty, Text.Empty, Text.Empty, Text.Empty);
        }

        return new Panel(tbl)
            .Header($"endpoints [[{items.Count}]] ", Justify.Center)
            .NoBorder()
            .BorderColor(new Color(136, 206, 250))
            .Border(BoxBorder.Square)
            .Padding(1, 0, 0, 1)
            .Expand();
    }

    /// <summary>
    /// Computes the width of every column and returns how many columns are  visible starting at <see cref="_columnOffset"/>.  It never removes
    /// columns – it only tells the caller which slice of the original list fits on screen – and it guarantees to return at least 1.
    /// </summary>
    /// <summary>
    /// Calculates column widths and returns how many columns are visible starting at <see cref="_columnOffset"/>.  
    /// * Columns are never removed.
    /// * A column never shrinks below its natural width.
    /// * Any left-over space is shared evenly across the visible columns
    ///   (adaptive layout).
    /// * Always returns ≥ 1 so something is shown even in a very narrow window.
    /// </summary>
    /// <summary>
    /// Calculates widths, returns how many columns are at least partially
    /// visible starting at <see cref="_columnOffset"/>, and sets
    /// <see cref="_lastColumnClipped"/> when the right-most column is cropped.
    /// </summary>
    private static int CalculateColumns(List<PortForwardEndpointHandler> items, List<(string header, int width, SortField sort, Func<PortForwardEndpointHandler, string> value)> columns, out int slackBeforeSpread)
    {
        int win = Console.WindowWidth;
        int n = columns.Count;
        _lastColumnClipped = false;          // reset every call

        // 1 ─ natural width for each column
        int[] nat = new int[n];
        for (int i = 0; i < n; i++)
        {
            int header = VisibleLen(columns[i].header);
            int longest = items.Any()
                        ? items.Max(h => VisibleLen(columns[i].value(h)))
                        : 0;
            nat[i] = Math.Max(4, Math.Max(header, longest));
        }

        // 2 ─ build the slice that fits, remember slack BEFORE spreading
        var visibleIdx = new List<int>();
        int used = 0, remain = win;

        for (int i = _columnOffset; i < n && remain > 0; i++)
        {
            int shown = Math.Min(nat[i], remain);
            columns[i] = (columns[i].header, shown,
                          columns[i].sort, columns[i].value);

            visibleIdx.Add(i);
            used += shown;
            remain -= shown;
        }

        // force a column if nothing fitted
        if (visibleIdx.Count == 0 && _columnOffset < n)
        {
            int shown = Math.Min(nat[_columnOffset], win);
            columns[_columnOffset] = (columns[_columnOffset].header, shown,
                                      columns[_columnOffset].sort,
                                      columns[_columnOffset].value);
            visibleIdx.Add(_columnOffset);
            used = shown;
            remain = win - shown;
        }

        int slack = remain;   // ← FREE SPACE **before** we spread it

        // 3 ─ spread slack evenly so the row fills the window
        if (slack > 0 && visibleIdx.Count > 0)
        {
            int even = slack / visibleIdx.Count;
            int leftover = slack % visibleIdx.Count;

            foreach (int idx in visibleIdx)
            {
                var c = columns[idx];
                c.width += even + (leftover-- > 0 ? 1 : 0);
                columns[idx] = c;
            }
        }
        
        // 4 ─ allow one more → only when   (cropped)  OR  (slack ≤ margin)
        int last = visibleIdx[^1];
        bool cropped = columns[last].width < nat[last];
        bool nearMargin = slack <= CROPPING_MARGIN;

        _lastColumnClipped = cropped || nearMargin;
        _columnOffset = Math.Clamp(_columnOffset, 0, n - 1);

        slackBeforeSpread = slack;

        return visibleIdx.Count;
    }



    private static void ToggleSort(SortField field, ref bool redraw)
    {
        if (_sortField == field)
        {
            // same column → flip direction
            _sortAsc = !_sortAsc; 
        }
        else
        {
            // new column → ascending first
            _sortField = field; 
            _sortAsc = true;
        }

        _selectedRowIndex = 0; // reset cursor to top
        redraw = true;
    }

    private static List<PortForwardEndpointHandler> SortHandlers(List<PortForwardEndpointHandler> list)
    {
        IOrderedEnumerable<PortForwardEndpointHandler> ordered = _sortField switch
        {
            SortField.PortForward => _sortAsc
                ? list.OrderBy(h => h.IsActive)
                : list.OrderByDescending(h => h.IsActive),
            SortField.Resource => _sortAsc
                ? list.OrderBy(h => h.Resource ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(h => h.Resource ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            SortField.Namespace => _sortAsc
                ? list.OrderBy(h => h.Namespace ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(h => h.Namespace ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            SortField.Ip => _sortAsc
                ? list.OrderBy(h => ToUInt32(h.LocalIp?.ToString() ?? string.Empty))
                : list.OrderByDescending(h => ToUInt32(h.LocalIp?.ToString() ?? string.Empty)),
            SortField.Url => _sortAsc
                ? list.OrderBy(h => h.Url, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(h => h.Url, StringComparer.OrdinalIgnoreCase),
            _ => throw new ArgumentOutOfRangeException()
        };

        return ordered.ToList();
    }

    static uint ToUInt32(string? ip)
    {
        if (string.IsNullOrEmpty(ip))
        {
            return UInt32.MaxValue;
        }

        var bytes = IPAddress.Parse(ip).GetAddressBytes();
        // bytes are in network-order (big-endian) → pack manually
        return ((uint)bytes[0] << 24) |
               ((uint)bytes[1] << 16) |
               ((uint)bytes[2] << 8) |
               (uint)bytes[3];
    }

    private static TableColumn CreateColumn(string header, int width, SortField sortField)
    {
        var sort = _sortField == sortField;
        if (sort)
        {
            header += _sortAsc ? "[cyan]↑[/]" : "[cyan]↓[/]";
        }

        return new TableColumn(header)
        {
            Width = width,
            NoWrap = true,
            Padding = new Padding(0),
        };
    }

    private static Markup CreateCell(string text, int width, bool isSelected)
    {
        // note that we need color in markup to preserve spaces, used to color background when selecting
        return isSelected
            ? new Markup($"[black on #87cefa]{RightPad(text, width)}[/]"){ Overflow = Overflow.Ellipsis }
            : new Markup($"[#87cefa]{text}[/]") { Overflow = Overflow.Ellipsis };
    }

    private static string RightPad(string s, int w)
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
        var y = result.Length;
        return result;
    }

    private static int VisibleLen(string markup)
    {
        // no markup / no emoji → plain ASCII length is correct
        if (!_spectreMarkup.IsMatch(markup) && !_spectreEmoji.IsMatch(markup))
        {
            return markup.Length;
        }

        if (_measurementLookup.TryGetValue(markup, out var len))
        {
            return len;
        }

        // otherwise let Spectre do the heavy lifting
        IRenderable renderable = new Markup(markup);
        var result = renderable
            .Measure(RenderOptions.Create(AnsiConsole.Console), int.MaxValue)
            .Max; // cell width on screen

        _measurementLookup.TryAdd(markup, result);

        return result;
    }
}
