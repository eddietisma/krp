using k8s;
using Krp.DependencyInjection;
using Krp.Dns;
using Krp.Endpoints;
using Krp.Endpoints.PortForward;
using Krp.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;
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

    private enum SortField { Url, Resource, Namespace, Ip, Active }
    private static SortField _sortField = SortField.Url;
    private static readonly string _version = Assembly.GetExecutingAssembly().GetName().Version!.ToString();
    private static readonly Regex _spectreMarkup = new Regex(@"\[[^\]]+?]", RegexOptions.Compiled);
    private static bool _sortAsc = true;

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
                        options.Filter = [
                            "namespace/meetings/*",
                            "namespace/*/service/person*",
                        ];
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

        var handlerSeen = endpointManager.GetAllHandlers().Count();
        var handlerActive = 0;
        var baseW = Console.WindowWidth;
        var baseH = Console.WindowHeight;
        var selectedIndex = 0;
        var selectedColumn = 0;

        while (true)
        {
            var layout = BuildLayout(kubeContextName, endpointManager, selectedIndex);

            await AnsiConsole.Live(layout).StartAsync(async ctx =>
            {
                DateTime lastCtx = DateTime.MinValue;

                while (true) // inner update loop
                {
                    var redraw = false;

                    // ── keyboard handling ────────────────────────────────
                    if (Console.KeyAvailable)
                    {
                        var keyInfo = Console.ReadKey(true);
                        var shift = (keyInfo.Modifiers & ConsoleModifiers.Shift) != 0;
                        var ctrl = (keyInfo.Modifiers & ConsoleModifiers.Control) != 0;

                        switch (keyInfo.Key)
                        {
                            case ConsoleKey.LeftArrow:
                                selectedColumn = Math.Max(0, selectedColumn - 1);
                                break;
                            case ConsoleKey.RightArrow:
                                selectedColumn = Math.Min(4, selectedIndex + 1);
                                break;
                            case ConsoleKey.UpArrow:
                                if (endpointManager.GetAllHandlers().Any())
                                {
                                    selectedIndex = Math.Max(0, selectedIndex - 1);
                                }

                                redraw = true;
                                break;
                            case ConsoleKey.DownArrow:
                                if (endpointManager.GetAllHandlers().Any())
                                {
                                    selectedIndex = Math.Min(endpointManager.GetAllHandlers().Count() - 1, selectedIndex + 1);
                                }

                                redraw = true;
                                break;

                            case ConsoleKey.R when ctrl: return;
                            case ConsoleKey.A when shift: ToggleSort(SortField.Active, ref selectedIndex, ref redraw); break;
                            case ConsoleKey.R when shift: ToggleSort(SortField.Resource, ref selectedIndex, ref redraw); break;
                            case ConsoleKey.U when shift: ToggleSort(SortField.Url, ref selectedIndex, ref redraw); break;
                            case ConsoleKey.N when shift: ToggleSort(SortField.Namespace, ref selectedIndex, ref redraw); break;
                            case ConsoleKey.I when shift: ToggleSort(SortField.Ip, ref selectedIndex, ref redraw); break;
                        }
                    }

                    // kube context every 3 s
                    if (DateTime.UtcNow - lastCtx > TimeSpan.FromSeconds(3))
                    {
                        var cfg = await KubernetesClientConfiguration.LoadKubeConfigAsync();
                        if (cfg.CurrentContext != kubeContextName)
                        {
                            kubeContextName = cfg.CurrentContext ?? "unknown";
                            redraw = true;
                        }
                        lastCtx = DateTime.UtcNow;
                    }


                    // handler list changed?
                    var handlers = endpointManager.GetAllHandlers().ToList();
                    int handlersCount = handlers.Count();
                    if (handlersCount != handlerSeen)
                    {
                        handlerSeen = handlersCount;
                        selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, handlersCount - 1));
                        redraw = true;
                    }

                    // handlers become active?
                    int activeCount = handlers.OfType<PortForwardEndpointHandler>().Count(x => x.IsActive);
                    if (activeCount != handlerSeen)
                    {
                        handlerActive = activeCount;
                        redraw = true;
                    }

                    // window resized?
                    if (Console.WindowWidth != baseW || Console.WindowHeight != baseH)
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

                        redraw = true;
                    }

                    if (redraw)
                    {
                        var newTable = BuildTablePanel(endpointManager, selectedIndex);
                        layout["main"].Update(newTable);
                        ctx.Refresh();
                    }

                    await Task.Delay(1);
                }
            });
        }
    }

    // ───────────────────────────────────────────────────
    //  Layout builders
    // ───────────────────────────────────────────────────
    private static Layout BuildLayout(string ctxName, EndpointManager endpointManager, int selectedIndex)
    {
        var infoPanel = BuildInfoPanel(ctxName);
        var commandPanel = BuildCommandPanel();
        var table = BuildTablePanel(endpointManager, selectedIndex);

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
    private static Panel BuildTablePanel(EndpointManager mgr, int sel)
    {
        var fixedRows = 3 + HEADER_SIZE;
        var (wActive, wUrl, wRs, wNs, wTgt) = Split(Console.WindowWidth - fixedRows);

        var tbl = new Table().NoBorder();
        var thUrl = CreateColumn("[bold]URL[/]", wUrl, SortField.Url);
        var thResource = CreateColumn("[bold]RESOURCE[/]", wRs, SortField.Resource);
        var thNamespace = CreateColumn("[bold]NAMESPACE[/]", wNs, SortField.Namespace);
        var thLocalIp = CreateColumn("[bold]IP[/]", wTgt, SortField.Ip);
        var thActive = CreateColumn("[bold]ACTIVE[/]", wActive, SortField.Active);

        tbl.AddColumn(thUrl);
        tbl.AddColumn(thResource);
        tbl.AddColumn(thNamespace);
        tbl.AddColumn(thLocalIp);
        tbl.AddColumn(thActive);

        var items = SortHandlers(mgr.GetAllHandlers().ToList());
        var rowsVis = Math.Max(1, Console.WindowHeight - fixedRows);
        var first = Math.Clamp(sel - rowsVis + 1, 0, Math.Max(0, items.Count - rowsVis));

        for (int i = first; i < Math.Min(items.Count, first + rowsVis); i++)
        {
            string url, rs, ns, tgt;
            bool active = false;
            var h = items[i];

            if (h is PortForwardEndpointHandler pf)
            {
                active = pf.IsActive;
                url = pf.Url; 
                rs = pf.Resource; 
                ns = pf.Namespace;
                tgt = $"{pf.LocalIp}";
            }
            else
            {
                url = h.Url; 
                rs = ""; 
                ns = "";
                tgt = $"{h.LocalIp}";
            }

            var isSelected = i == sel;
            var tdUrl = CreateCell($"{url}", wUrl, isSelected);
            var tdResource = CreateCell(rs, wRs, isSelected);
            var tdNamespace = CreateCell(ns, wNs, isSelected);
            var tdLocalIp = CreateCell(tgt, wTgt, isSelected);
            var tdActive = CreateCell(active ? "[green]•[/]" : "", wActive, isSelected);

            tbl.AddRow(tdUrl, tdResource, tdNamespace, tdLocalIp, tdActive);
        }

        if (!items.Any())
        {
            tbl.AddRow(new Text("No endpoints available", Color.Grey), Text.Empty, Text.Empty, Text.Empty, Text.Empty);
        }

        return new Panel(tbl)
            .Header($"endpoints [[{items.Count}]] ", Justify.Center)
            .NoBorder()
            .BorderColor(Color.Cyan1)
            .Border(BoxBorder.Square)
            .Padding(1, 0, 0, 0)
            .Expand();
    }

    private static (int active, int n, int ns, int rs, int tgt) Split(int usable)
    {
        const int minEach = 3;
        const int widthActiveCol = 8;
        const int widthResourceCol = 30;
        const int withLocalIpCol = 30;

        var minTotal = +widthActiveCol + widthResourceCol + withLocalIpCol + (2 * minEach);
        usable = Math.Max(minTotal, usable);

        var flexible = usable - (widthActiveCol + widthResourceCol + withLocalIpCol);
        var baseFlex = flexible / 2;
        var extra = flexible % 2;

        var nWidth = Math.Max(minEach, baseFlex + (extra > 0 ? 1 : 0));
        var nsWidth = Math.Max(minEach, baseFlex);
        
        return (widthActiveCol, nWidth, nsWidth, widthResourceCol, withLocalIpCol);
    }

    private static void ToggleSort(SortField field, ref int sel, ref bool needTable)
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

        sel = 0; // reset cursor to top
        needTable = true;
    }

    private static List<IEndpointHandler> SortHandlers(List<IEndpointHandler> list)
    {
        IOrderedEnumerable<IEndpointHandler> ordered = _sortField switch
        {
            SortField.Active => _sortAsc
                ? list.OrderBy(h => (h as PortForwardEndpointHandler)?.IsActive)
                : list.OrderByDescending(h => (h as PortForwardEndpointHandler)?.IsActive),
            SortField.Resource => _sortAsc
                ? list.OrderBy(h => (h as PortForwardEndpointHandler)?.Resource ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(h => (h as PortForwardEndpointHandler)?.Resource ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            SortField.Namespace => _sortAsc
                ? list.OrderBy(h => (h as PortForwardEndpointHandler)?.Namespace ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                : list.OrderByDescending(h => (h as PortForwardEndpointHandler)?.Namespace ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            SortField.Ip => _sortAsc
                ? list.OrderBy(h => ToUInt32((h as PortForwardEndpointHandler)?.LocalIp?.ToString() ?? string.Empty))
                : list.OrderByDescending(h => ToUInt32((h as PortForwardEndpointHandler)?.LocalIp?.ToString() ?? string.Empty)),
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
            Padding = new Padding(0, 0),
        };
    }

    private static Markup CreateCell(string text, int width, bool isSelected)
    {
        return isSelected
            ? new Markup($"{RightPad(text, width)}", new Style(Color.Black, Color.LightSkyBlue1)) { Overflow = Overflow.Ellipsis, }
            : new Markup($"{RightPad(text, width)}", new Style(Color.LightSkyBlue1, Color.Black)) { Overflow = Overflow.Ellipsis, };
    }

    private static string RightPad(string s, int w)
    {
        var plain = _spectreMarkup.Replace(s, ""); // remove Spectre markup
        return plain.Length > w ? plain[..w] : plain.PadRight(w);
    }
}
