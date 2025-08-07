//// Program.cs — full redraw only on resize, fresh Live each time
//using k8s;
//using k8s.Models;
//using Krp.DependencyInjection;
//using Krp.Dns;
//using Krp.Endpoints;
//using Krp.Endpoints.PortForward;
//using Krp.Logging;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using Spectre.Console;
//using System;
//using System.Linq;
//using System.Net;
//using System.Threading.Tasks;

//namespace Krp.Tool;

//public class Program
//{
//    public static async Task Main(string[] args)
//    {
//        Console.OutputEncoding = System.Text.Encoding.UTF8;
//        Console.BackgroundColor = ConsoleColor.Black;

//        var host = Host.CreateDefaultBuilder(args)
//            .ConfigureLogging(l => l.AddKrpLogger())
//            .ConfigureServices((ctx, s) =>
//            {
//                s.AddKubernetesForwarder(ctx.Configuration)
//                 .UseDnsLookup(o => o.Nameserver = "8.8.8.8")
//                 .UseRouting(DnsOptions.HostsFile)
//                 .UseTcpWithHttpForwarder(o =>
//                 {
//                     o.ListenAddress = IPAddress.Any;
//                     o.ListenPorts = new[] { 80, 443 };
//                 })
//                 .UseEndpointExplorer(o => o.RefreshInterval = TimeSpan.FromHours(1));
//            })
//            .Build();

//        var mgr = host.Services.GetRequiredService<EndpointManager>();

//        await Task.WhenAll(host.RunAsync(), RunUiAsync(mgr));
//    }

//    // UI loop that recreates Live on every resize --------------------
//    private static async Task RunUiAsync(EndpointManager mgr)
//    {
//        int sel = 0;
//        string ctxName = "unknown";
//        int handlerSeen = mgr.GetAllHandlers().Count();

//        while (true) // outer loop: restarts after each resize
//        {
//            int baseW = Console.WindowWidth;
//            int baseH = Console.WindowHeight;

//            // full layout for current size
//            var layout = BuildLayout(ctxName, mgr, sel, baseW, baseH);

//            await AnsiConsole.Live(layout).StartAsync(async ctx =>
//            {
//                DateTime lastCtx = DateTime.MinValue;

//                while (true) // inner update loop
//                {
//                    bool needTable = false;

//                    // navigation
//                    if (Console.KeyAvailable && mgr.GetAllHandlers().Any())
//                    {
//                        var key = Console.ReadKey(true).Key;
//                        int cnt = mgr.GetAllHandlers().Count();
//                        if (key == ConsoleKey.UpArrow) { sel = Math.Max(0, sel - 1); needTable = true; }
//                        if (key == ConsoleKey.DownArrow) { sel = Math.Min(cnt - 1, sel + 1); needTable = true; }
//                    }

//                    // kube context every 3 s
//                    if (DateTime.UtcNow - lastCtx > TimeSpan.FromSeconds(3))
//                    {
//                        var cfg = await KubernetesClientConfiguration.LoadKubeConfigAsync();
//                        if (cfg.CurrentContext != ctxName)
//                        {
//                            ctxName = cfg.CurrentContext ?? "unknown";
//                            // full rebuild next outer loop
//                            break;
//                        }
//                        lastCtx = DateTime.UtcNow;
//                    }

//                    // handler list changed?
//                    int nowCount = mgr.GetAllHandlers().Count();
//                    if (nowCount != handlerSeen)
//                    {
//                        handlerSeen = nowCount;
//                        sel = Math.Clamp(sel, 0, Math.Max(0, nowCount - 1));
//                        break; // full rebuild
//                    }

//                    // window resized?
//                    if (Console.WindowWidth != baseW || Console.WindowHeight != baseH)
//                    {
//                        // shrink buffer to new viewable area
//                        try
//                        {
//#pragma warning disable CA1416
//                            Console.SetBufferSize(Console.WindowLeft + Console.WindowWidth,
//                                                  Console.WindowTop + Console.WindowHeight);
//#pragma warning restore CA1416
//                        }
//                        catch { }

//                        Console.Clear();
//                        break; // leave Live, outer loop restarts
//                    }

//                    // only need table refresh?
//                    if (needTable)
//                    {
//                        var newTable = BuildTablePanel(mgr, sel, baseW, baseH);
//                        layout["main"].Update(newTable);
//                        ctx.Refresh();
//                    }

//                    await Task.Delay(1);
//                }
//            });
//        }
//    }

//    // Build complete screen ------------------------------------------
//    private static Layout BuildLayout(string ctxName, EndpointManager mgr, int sel, int winW, int winH)
//    {
//        var infoPanel = BuildInfoPanel(ctxName, mgr, out int infoRows);
//        var table = BuildTablePanel(mgr, sel, winW, winH);

//        var root = new Layout("root");
//        root.SplitRows(new Layout("info") { Size = infoRows }, new Layout("main"));
//        root["info"].Update(infoPanel);
//        root["main"].Update(table);
//        return root;
//    }

//    private static Panel BuildInfoPanel(string ctx, EndpointManager mgr, out int infoRows)
//    {
//        var t = new Table()
//            .NoBorder()
//            .HideHeaders()
//            .AddColumn("")
//            .AddColumn("")
//            .AddRow(new Text("Context:", Color.Orange1), new Text(ctx, Color.White))
//            .AddRow(new Text("Cluster:", Color.Orange1), new Text(ctx, Color.White))
//            //.AddRow(new Text("Active PF:", Color.Orange1), new Text(active.ToString(), Color.White));
//            .AddRow(new Text("krp Rev:", Color.Orange1), new Text("1.0.0-beta.78", Color.White));

//        infoRows = t.Rows.Count;
//        return new Panel(t).NoBorder().Padding(1, 0, 0, 0);
//    }

//    private static Panel BuildTablePanel(EndpointManager mgr, int sel, int winW, int winH)
//    {
//        var items = mgr.GetAllHandlers().ToList();

//        // ── sizes ─────────────────────────────────────────────
//        int fixedRows = 3 + 3;
//        int rowsVis = Math.Max(1, (winH == 0 ? Console.WindowHeight : winH) - fixedRows);
//        int first = Math.Clamp(sel - rowsVis + 1, 0, Math.Max(0, items.Count - rowsVis));

//        var (wUrl, wRs, wNs, wTgt) = Split((winW == 0 ? Console.WindowWidth : winW) - fixedRows);

//        // ── table skeleton ───────────────────────────────────
//        var tbl = new Table().NoBorder().Expand();
//        tbl.ColumnSpacing = 0;                       // <── important!
//        tbl.AddColumn(new TableColumn("[bold]URL[/]").Width(wUrl).NoWrap());
//        tbl.AddColumn(new TableColumn("[bold]RESOURCE[/]").Width(wRs).NoWrap());
//        tbl.AddColumn(new TableColumn("[bold]NAMESPACE[/]").Width(wNs).NoWrap());
//        tbl.AddColumn(new TableColumn("[bold]TARGET[/]").Width(wTgt).NoWrap());

//        // ── styles ───────────────────────────────────────────
//        const string selFg = "black";
//        const string selBg = "lightskyblue1";
//        var blue = new Style(Color.LightSkyBlue1);

//        // helper that wraps a value in markup when selected
//        Markup Cell(string value, int width, bool on)
//            => on
//               ? new Markup($"[{selFg} on {selBg}]{Fill(value, width)}[/]")
//               : new Markup($"[lightskyblue1]{Fill(value, width)}[/]");

//        // ── populate rows ────────────────────────────────────
//        for (int i = first; i < Math.Min(items.Count, first + rowsVis); i++)
//        {
//            var h = items[i];
//            bool on = i == sel;

//            string url, rs, ns, tgt;
//            if (h is PortForwardEndpointHandler pf)
//            {
//                url = pf.Url;
//                rs = pf.Resource;
//                ns = pf.Namespace;
//                tgt = $"{pf.LocalIp}";
//            }
//            else
//            {
//                url = h.Url;
//                rs = ns = "";
//                tgt = h.Url;
//            }

//            tbl.AddRow(
//                Cell(url, wUrl, on),
//                Cell(rs, wRs, on),
//                Cell(ns, wNs, on),
//                Cell(tgt, wTgt, on));
//        }

//        if (!items.Any())
//            tbl.AddRow(new Text("No endpoints available", Color.Grey));

//        return new Panel(tbl)
//            .Header($"endpoints (all) [[{items.Count}]] ", Justify.Center)
//            .BorderColor(Color.Cyan1)
//            .Padding(1, 0, 0, 1)
//            .Expand();
//    }

//    /// <summary>
//    /// Split usable console width into 4 columns:
//    /// n   – flexible
//    /// ns  – flexible
//    /// rs  – fixed (second-to-last)
//    /// tgt – fixed (last column)
//    /// </summary>
//    private static (int n, int ns, int rs, int tgt) Split(int usable)
//    {
//        const int minEach = 3;   // minimum for flexible columns
//        const int rsWidth = 30;  // ← fixed width for RS column
//        const int tgtWidth = 24;  // ← fixed width for TARGET column

//        // ensure at least enough space for the fixed cols + minima
//        int minTotal = rsWidth + tgtWidth + 2 * minEach; // 2 flex columns
//        usable = Math.Max(minTotal, usable);

//        // remaining space for the two flexible columns
//        int flexible = usable - (rsWidth + tgtWidth);

//        // split evenly + hand out any remainder
//        int baseFlex = flexible / 2;
//        int extra = flexible % 2;   // either 0 or 1

//        int nWidth = Math.Max(minEach, baseFlex + (extra > 0 ? 1 : 0));
//        int nsWidth = Math.Max(minEach, baseFlex);

//        return (nWidth, nsWidth, rsWidth, tgtWidth);
//    }

//    private static string Fill(string s, int w) => s.Length >= w ? s[..w] : s.PadRight(w);
//}
