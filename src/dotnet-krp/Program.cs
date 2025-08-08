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
    private const int CROPPING_MARGIN = 3;          // Space (chars) treated as "near edge".
    private const int MIN_COL_WIDTH = 4;            // Space (chars) for minimum column width.

    private enum SortField { Url, Resource, Namespace, Ip, PortForward }
    private static SortField _sortField = SortField.Url;
    private static readonly Dictionary<string, int> _measurementLookup = new();
    private static readonly Regex _spectreMarkup = new(@"\[[^\]]+?]", RegexOptions.Compiled);
    private static readonly Regex _spectreEmoji = new(@":[\w+\-]+?:", RegexOptions.Compiled);
    private static readonly string _version = Assembly.GetExecutingAssembly().GetName().Version!.ToString();

    private static int _columnOffset = 0;           // Index of first visible column.
    private static int _maxColumnOffset = 0;        // Right‑most allowed offset.
    private static int _selectedRowIndex = 0;       // Row cursor.
    private static bool _sortAsc = true;            // Current sort direction.
    private static bool _lastColumnClipped;         // True when right‑most col is trimmed.
    private static bool _windowGrew;                // True when console width increased.

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
                while (true)
                {
                    try
                    {
                        var newW = Console.WindowWidth;
                        var newH = Console.WindowHeight;

                        var handlers = endpointManager.GetAllHandlers().ToList();
                        var redraw = false;
                        var redrawInfo = false;

                        // Keyboard handling.
                        if (Console.KeyAvailable)
                        {
                            var key = Console.ReadKey(true);
                            var shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

                            switch (key.Key)
                            {
                                case ConsoleKey.LeftArrow: _columnOffset = Math.Max(0, _columnOffset - 1); redraw = true;break;
                                case ConsoleKey.RightArrow: _columnOffset = Math.Min(_columnOffset + 1, _maxColumnOffset); redraw = true; break;
                                case ConsoleKey.UpArrow: _selectedRowIndex = Math.Max(0, _selectedRowIndex - 1); redraw = true; break;
                                case ConsoleKey.DownArrow: _selectedRowIndex = Math.Min(endpointManager.GetAllHandlers().Count() - 1, _selectedRowIndex + 1); redraw = true; break;
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
                            if (cfg.CurrentContext != kubeContextName)
                            {
                                kubeContextName = cfg.CurrentContext ?? "unknown";
                                redrawInfo = true;
                            }

                            lastCtx = DateTime.UtcNow;
                        }

                        // Handler list size.
                        var handlersCount = handlers.Count();
                        if (!redraw && handlersCount != handlerCount)
                        {
                            handlerCount = handlersCount;
                            _selectedRowIndex = Math.Clamp(_selectedRowIndex, 0, Math.Max(0, handlersCount - 1));
                            redraw = true;
                        }

                        // Active handlers.
                        var handlersActiveCount = handlers.OfType<PortForwardEndpointHandler>().Count(x => x.IsActive);
                        if (!redraw && handlersActiveCount != handlerActiveCount)
                        {
                            handlerActiveCount = handlersActiveCount;
                            redraw = true;
                        }

                        // Window resizing.
                        if (!redraw && (newW != baseW || newH != baseH))
                        {
                            try
                            {
#pragma warning disable CA1416
                                Console.SetBufferSize(Console.WindowLeft + newW, Console.WindowTop + newH);
#pragma warning restore CA1416
                            }
                            catch
                            {
                                // ignored
                            }

                            _windowGrew = newW > baseW;
                            baseW = newW;
                            baseH = newH;
                            redraw = true;
                        }

                        // Redraw panels.
                        if (redraw)
                        {
                            layout["main"].Update(BuildTablePanel(endpointManager));
                            layout["main"].Visible();
                            ctx.Refresh();
                        }

                        if (redrawInfo)
                        {
                            layout["info"].Update(BuildInfoPanel(kubeContextName));
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

    private static Layout BuildLayout(string ctxName, EndpointManager endpointManager)
    {
        var info = BuildInfoPanel(ctxName);
        var cmd = BuildCommandPanel();
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
        root["info"].Update(info);
        root["commands1"].Update(cmd);
        root["commands2"].Update(cmd);
        root["commands3"].Update(cmd);
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

    private static Panel BuildTablePanel(EndpointManager mgr)
    {
        var items = SortHandlers(mgr.GetAllHandlers().OfType<PortForwardEndpointHandler>());
        var columns = new List<(string header, int width, SortField sort, Func<PortForwardEndpointHandler, string> valueSelector)>
        {
            ("[bold]URL[/]",       0, SortField.Url,        h => h.Url),
            ("[bold]PF[/]",        0, SortField.PortForward,h => h.IsActive ? ":black_circle:" : ":white_circle:"),
            ("[bold]RESOURCE[/]",  0, SortField.Resource,   h => h.Resource),
            ("[bold]NAMESPACE[/]", 0, SortField.Namespace,  h => h.Namespace),
            ("[bold]IP[/]",        0, SortField.Ip,         h => h.LocalIp.ToString()),
        };

        var total = columns.Count;
        var totalVisibleColumns = CalculateColumns(items, columns, out int slack);

        // Auto‑reveal hidden columns when console grows.
        if (_windowGrew)
        {
            while (_columnOffset > 0)
            {
                int idx = _columnOffset - 1;
                int natural = Math.Max(4, Math.Max(VisibleLen(columns[idx].header), items.Any() ? items.Max(h => VisibleLen(columns[idx].valueSelector(h))) : 0));
                if (natural > slack)
                {
                    break;
                }

                _columnOffset--;
                columns = columns.Select(c => (c.header, 0, c.sort, c.valueSelector)).ToList();
                totalVisibleColumns = CalculateColumns(items, columns, out slack);
            }
            _windowGrew = false;
        }

        _maxColumnOffset = Math.Max(0, total - totalVisibleColumns + (_lastColumnClipped ? 1 : 0));

        // Rows that fit.
        var fixedRows = 3 + HEADER_SIZE;
        var maxRows = Math.Max(1, Console.WindowHeight - fixedRows);

        var tbl = new Table().NoBorder();

        // Print columns
        var shownCols = columns.Skip(_columnOffset).Take(totalVisibleColumns).ToList();
        foreach (var col in shownCols)
        {
            tbl.AddColumn(CreateColumn(col.header, col.width, col.sort));
        }
        
        // Print rows
        var first = Math.Clamp(_selectedRowIndex - maxRows + 1, 0, Math.Max(0, items.Count - maxRows));
        for (int i = first; i < Math.Min(items.Count, first + maxRows); i++)
        {
            var isSelected = i == _selectedRowIndex;
            var cells = new List<Markup>();

            foreach (var col in shownCols)
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
    /// Calculates column widths and returns how many columns are visible starting at <see cref="_columnOffset"/>.
    /// - Columns are never removed.
    /// - Columns never shrink below natural width.
    /// - Leftover space is shared evenly across visible columns (adaptive).
    /// - Always returns ≥ 1 so something is shown in a narrow window.
    /// - Sets <see cref="_lastColumnClipped"/> if the right-most column is cropped or near the margin.
    /// - Outputs <paramref name="slackBeforeSpread"/> as free space before spreading.
    /// </summary>
    private static int CalculateColumns(List<PortForwardEndpointHandler> items, List<(string header, int width, SortField sort, Func<PortForwardEndpointHandler, string> value)> columns, out int slackBeforeSpread)
    {
        var width = Console.WindowWidth;
        var n = columns.Count;
        _lastColumnClipped = false; // reset every call

        // 1. Natural width per column.
        var nat = new int[n];
        for (var i = 0; i < n; i++)
        {
            var header = VisibleLen(columns[i].header);
            var longest = items.Any() ? items.Max(h => VisibleLen(columns[i].value(h))) : 0;
            nat[i] = Math.Max(MIN_COL_WIDTH, Math.Max(header, longest));
        }

        // 2. Build the slice that fits. Track remaining width.
        var visibleIdx = new List<int>();
        var remain = width;

        for (var i = _columnOffset; i < n && remain > 0; i++)
        {
            var shown = Math.Min(nat[i], remain);
            columns[i] = (columns[i].header, shown, columns[i].sort, columns[i].value);
            visibleIdx.Add(i);
            remain -= shown;
        }

        // Ensure at least one column is shown.
        if (visibleIdx.Count == 0 && _columnOffset < n)
        {
            var shown = Math.Min(nat[_columnOffset], width);
            columns[_columnOffset] = (columns[_columnOffset].header, shown, columns[_columnOffset].sort, columns[_columnOffset].value);
            visibleIdx.Add(_columnOffset);
            remain = width - shown;
        }

        var slack = remain;   // ← FREE SPACE **before** we spread it

        // 3. Capture slack before spreading and then spread evenly.
        slackBeforeSpread = slack; // Free space before spreading.
        if (slack > 0 && visibleIdx.Count > 0)
        {
            var even = slack / visibleIdx.Count;
            var leftover = slack % visibleIdx.Count;

            foreach (var idx in visibleIdx)
            {
                var c = columns[idx];
                c.width += even + (leftover-- > 0 ? 1 : 0);
                columns[idx] = c;
            }
        }

        // 4. Flag when the right-most column is cropped or near the edge.
        var last = visibleIdx[^1];
        var cropped = columns[last].width < nat[last];
        var nearMargin = slack <= CROPPING_MARGIN;
        _lastColumnClipped = cropped || nearMargin;

        // 5. Clamp offset and return count.
        _columnOffset = Math.Clamp(_columnOffset, 0, n - 1);
        return visibleIdx.Count;
    }

    private static void ToggleSort(SortField field, ref bool redraw)
    {
        _sortAsc = _sortField != field || !_sortAsc;
        _sortField = field;
        _selectedRowIndex = 0; // Reset cursor to top.
        redraw = true;
    }

    private static List<PortForwardEndpointHandler> SortHandlers(IEnumerable<PortForwardEndpointHandler> list)
    {
        var ordered = _sortField switch
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
            _ => throw new ArgumentOutOfRangeException(),
        };

        return ordered.ToList();
    }

    static uint ToUInt32(string? ip)
    {
        if (string.IsNullOrEmpty(ip))
        {
            return uint.MaxValue;
        }

        var bytes = IPAddress.Parse(ip).GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | (uint)bytes[3]; // bytes are in network-order (big-endian) → pack manually
    }

    private static TableColumn CreateColumn(string header, int width, SortField sortField)
    {
        if (_sortField == sortField)
        {
            header += _sortAsc ? "[cyan]↑[/]" : "[cyan]↓[/]";
        }

        return new TableColumn(header)
        {
            Width = width, 
            NoWrap = true, 
            Padding = new Padding(0)
        };
    }

    private static Markup CreateCell(string text, int width, bool isSelected)
    {
        // Pad selected cells to the column width so the highlight covers the entire cell.
        // This is done inside markup so Spectre doesn't trim the spaces.
        return new Markup(isSelected ? $"[black on #87cefa]{RightPad(text, width)}[/]" : $"[#87cefa]{text}[/]") { Overflow = Overflow.Ellipsis };
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
        return result;
    }

    private static int VisibleLen(string markup)
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
