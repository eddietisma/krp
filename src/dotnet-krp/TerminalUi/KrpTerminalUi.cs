﻿using Krp.Kubernetes;
using Krp.Tool.TerminalUi.Extensions;
using Krp.Tool.TerminalUi.Tables;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace Krp.Tool.TerminalUi;

public class KrpTerminalUi
{
    public const int HEADER_SIZE = 8;      // Space (chars) treated as top menu.
    public const int CROP_MARGIN = 4;      // Space (chars) treated as "near edge".
    public const int MIN_COL_WIDTH = 5;    // Space (chars) for minimum column width.

    private readonly string _version = Assembly.GetExecutingAssembly().GetName().Version!.ToString();
    private readonly KubernetesClient _kubernetesClient;
    private readonly KrpTerminalState _state;
    private readonly PortForwardTable _portForwardTable;
    private readonly LogsTable _logsTable;
    private readonly ILogger<KrpTerminalUi> _logger;
    private readonly FigletFont _logoFont;

    private string _kubeCurrentContext;

    public KrpTerminalUi(KubernetesClient kubernetesClient, KrpTerminalState state, PortForwardTable portForwardTable, LogsTable logsTable, ILogger<KrpTerminalUi> logger)
    {
        _kubernetesClient = kubernetesClient;
        _state = state;
        _portForwardTable = portForwardTable;
        _logsTable = logsTable;
        _logger = logger;
        _logoFont = FigletFont.Load(Assembly.GetExecutingAssembly().GetManifestResourceStream($"{typeof(KrpTerminalUi).Namespace}.Fonts.3D.flf") ?? throw new InvalidOperationException("Embedded font '3D.flf' not found."));

        state.SortField = SortField.PortForward;
        state.SortAscending = false;
    }

    public async Task RunUiAsync()
    {
        _kubeCurrentContext = await _kubernetesClient.FetchCurrentContext();

        var baseW = Console.WindowWidth;
        var baseH = Console.WindowHeight;

        while (true)
        {
            var init = true;
            try
            {
                _state.WindowHeight = Console.WindowHeight;
                _state.WindowWidth = Console.WindowWidth;

                var layout = BuildLayout();

                await AnsiConsole.Live(layout).StartAsync(async ctx =>
                {
                    var lastCtx = Stopwatch.StartNew();
                    var lastRedrawMain = Stopwatch.StartNew();
                    var lastChangeDetection = Stopwatch.StartNew();

                    while (true)
                    {
                        try
                        {
                            _state.WindowHeight = Console.WindowHeight;
                            _state.WindowWidth = Console.WindowWidth;

                            var count = _state.SelectedTable switch
                            {
                                KrpTable.PortForwards => _portForwardTable.Count,
                                KrpTable.Logs => _logsTable.Count,
                                _ => 0,
                            };

                            var redraw = init;
                            var redrawInfo = false;
                            var redrawContext = false;

                            // Keyboard handling.
                            if (Console.KeyAvailable)
                            {
                                ConsoleKeyInfo key;
                                do
                                {
                                    // Drain the input buffer and keep only the most recent key.
                                    //   • intercept: true => prevents the key from being echoed into the console.
                                    //   • do/while loop => clears any queued key repeats (e.g. when user holds down an arrow key), so only the last pressed key is processed each frame.
                                    key = Console.ReadKey(intercept: true);
                                }
                                while (Console.KeyAvailable);

                                var shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
                                var ctrl = (key.Modifiers & ConsoleModifiers.Control) != 0;

                                switch (key.Key)
                                {
                                    case ConsoleKey.Home: _state.SelectedRow[_state.SelectedTable] = 0; redraw = true; break;
                                    case ConsoleKey.End: _state.SelectedRow[_state.SelectedTable] = count - 1; redraw = true; break;
                                    case ConsoleKey.LeftArrow: _state.ColumnOffset = Math.Max(0, _state.ColumnOffset - 1); redraw = true; break;
                                    case ConsoleKey.RightArrow: _state.ColumnOffset = Math.Min(_state.ColumnOffset + 1, _state.ColumnOffsetMax); redraw = true; break;
                                    case ConsoleKey.UpArrow: redraw = _state.SelectedRow[_state.SelectedTable] != (_state.SelectedRow[_state.SelectedTable] = Math.Max(0, _state.SelectedRow[_state.SelectedTable] - 1)); break;
                                    case ConsoleKey.DownArrow: redraw = _state.SelectedRow[_state.SelectedTable] != (_state.SelectedRow[_state.SelectedTable] = Math.Min(count - 1, _state.SelectedRow[_state.SelectedTable] + 1)); break;
                                    case ConsoleKey.D1: _state.SelectedTable = KrpTable.PortForwards; redraw = true; redrawContext = true; break;
                                    case ConsoleKey.D2: _state.SelectedTable = KrpTable.Logs; redraw = true; redrawContext = true; break;
                                    case ConsoleKey.I when shift: ToggleSort(SortField.Ip, ref redraw); break;
                                    case ConsoleKey.N when shift: ToggleSort(SortField.Namespace, ref redraw); break;
                                    case ConsoleKey.P when shift: ToggleSort(SortField.PortForward, ref redraw); break;
                                    case ConsoleKey.R when shift: ToggleSort(SortField.Resource, ref redraw); break;
                                    case ConsoleKey.U when shift: ToggleSort(SortField.Url, ref redraw); break;
                                    case ConsoleKey.Enter when _state.SelectedTable == KrpTable.PortForwards: _ = _portForwardTable.ForceStart(); break;
                                    case ConsoleKey.D when ctrl && _state.SelectedTable == KrpTable.PortForwards: _portForwardTable.ForceStop(); break;
                                    case ConsoleKey.F5: return; // Abort inner loop to force a new AnsiConsole.Live instance, forcing a refresh.
                                }
                            }

                            // Handle kubernetes context (1s).
                            if (!redraw && lastCtx.Elapsed >= TimeSpan.FromSeconds(1))
                            {
                                var context = await _kubernetesClient.FetchCurrentContext();
                                if (context != _kubeCurrentContext)
                                {
                                    _kubeCurrentContext = context;
                                    redrawInfo = true;
                                }

                                lastCtx.Restart();
                            }

                            // Handle tables changes (1s).
                            if (!redraw && lastChangeDetection.Elapsed >= TimeSpan.FromSeconds(1))
                            {
                                var detectChanges = _state.SelectedTable switch
                                {
                                    KrpTable.PortForwards => _portForwardTable.DetectChanges(),
                                    KrpTable.Logs => _logsTable.DetectChanges(),
                                    _ => false,
                                };

                                lastChangeDetection.Restart();

                                if (detectChanges)
                                {
                                    redraw = true;
                                }
                            }

                            // Window resizing.
                            if (init || (!redraw && (_state.WindowWidth != baseW || _state.WindowHeight != baseH)))
                            {
                                try
                                {
#pragma warning disable CA1416
                                    Console.SetBufferSize(Console.WindowLeft + _state.WindowWidth, Console.WindowTop + _state.WindowHeight);
#pragma warning restore CA1416
                                }
                                catch
                                {
                                    // ignored
                                }

                                var previousWindowSize = _state.WindowSize;

                                _state.WindowGrew = _state.WindowWidth > baseW;
                                _state.UpdateWindowSize(_state.WindowWidth);

                                baseH = _state.WindowHeight;
                                baseW = _state.WindowWidth;

                                if (_state.WindowSize != previousWindowSize)
                                {
                                    return;
                                }

                                redraw = true;
                            }

                            // Redraw panels.
                            if (redraw)
                            {
                                layout["main"].Update(BuildMainPanel());
                                init = false;
                                lastRedrawMain.Restart();
                            }

                            if (redrawInfo)
                            {
                                layout["info"].Update(BuildInfoPanel());
                            }

                            if (redrawContext)
                            {
                                layout["context"].Update(BuildContextMenuPanel());
                            }

                            if (redraw || redrawInfo || redrawContext)
                            {
                                ctx.Refresh();
                            }

                            // Throttle spin delay
                            //   • Idle (no redraw ≥ 1s): insert a small 50 ms delay per iteration to lower CPU usage.
                            //   • Active (frequent redraws): no delay to preserve interactive responsiveness (e.g. scrolling).
                            var idle = lastRedrawMain.Elapsed >= TimeSpan.FromSeconds(1);
                            if (idle)
                            {
                                await Task.Delay(50);
                            }
                            else
                            {
                                await Task.Yield();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error in inner main UI loop");
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in outer main UI loop");
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
            new Layout("info") { Ratio = 3, IsVisible = _state.WindowSize != WindowSize.XS },
            new Layout("menu") { Ratio = 2, IsVisible = _state.WindowSize > WindowSize.SM },
            new Layout("context") { Ratio = 3, IsVisible = _state.WindowSize > WindowSize.MD });

        root["logo"].Update(BuildLogoPanel());
        root["info"].Update(BuildInfoPanel());
        root["menu"].Update(BuildMenuPanel());
        root["context"].Update(BuildContextMenuPanel());
        root["main"].Update(BuildMainPanel());
        return root;
    }

    private Panel BuildMainPanel()
    {
        var panel = _state.SelectedTable switch
        {
            KrpTable.PortForwards => _portForwardTable.BuildPanel(),
            KrpTable.Logs => _logsTable.BuildMainPanel(),
            _ => throw new ArgumentOutOfRangeException(nameof(_state.SelectedTable), _state.SelectedTable, "Invalid selected table index."),
        };

        return panel.NoBorder().BorderColor(new Color(135, 206, 250)).Border(BoxBorder.Square).Padding(1, 0, 0, 1).Expand();
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
        var panel = _state.SelectedTable switch
        {
            KrpTable.PortForwards => _portForwardTable.BuildContextMenuPanel(),
            KrpTable.Logs => _logsTable.BuildContextMenuPanel(),
            _ => throw new ArgumentOutOfRangeException(nameof(_state.SelectedTable), _state.SelectedTable, "Invalid selected table index."),
        };
        return panel.NoBorder().Padding(0, 0, 0, 0).HeaderAlignment(Justify.Left);
    }

    private Panel BuildLogoPanel()
    {
        var figlet = new FigletText(_logoFont, "KRP").Color(Color.Orange1);
        var panel = new Panel(figlet);
        var padding = 1;

        if (_state.WindowSize == WindowSize.XS)
        {
            figlet.Centered();
            padding = 0;
        }

        return panel.NoBorder().Padding(padding, 0, 0, 0);
    }

    private void ToggleSort(SortField field, ref bool redraw)
    {
        _state.SortAscending = _state.SortField != field || !_state.SortAscending;
        _state.SortField = field;
        _state.SelectedRow[_state.SelectedTable] = 0; // Reset cursor to top.
        redraw = true;
    }
}
