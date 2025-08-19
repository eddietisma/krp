using k8s;
using Krp.Tool.TerminalUi.Extensions;
using Krp.Tool.TerminalUi.Tables;
using Spectre.Console;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Krp.Tool.TerminalUi;

public class KrpTerminalUi
{
    public const int HEADER_SIZE = 8;      // Space (chars) treated as top menu.
    public const int CROP_MARGIN = 4;      // Space (chars) treated as "near edge".
    public const int MIN_COL_WIDTH = 5;    // Space (chars) for minimum column width.
    
    private readonly string _version = Assembly.GetExecutingAssembly().GetName().Version!.ToString();
    private readonly KrpTerminalState _state;
    private readonly PortForwardTable _portForwardTable;
    private readonly LogsTable _logsTable;
    private readonly FigletFont _logoFont;

    private string _kubeCurrentContext;

    public KrpTerminalUi(KrpTerminalState state, PortForwardTable portForwardTable, LogsTable logsTable)
    {
        _state = state;
        _portForwardTable = portForwardTable;
        _logsTable = logsTable;
        _logoFont = FigletFont.Load(Assembly.GetExecutingAssembly().GetManifestResourceStream($"{typeof(KrpTerminalUi).Namespace}.Fonts.3D.flf") ?? throw new InvalidOperationException("Embedded font '3D.flf' not found."));

        state.SortField = SortField.PortForward;
        state.SortAscending = false;
    }

    public async Task RunUiAsync()
    {
        var kubeCfg = await KubernetesClientConfiguration.LoadKubeConfigAsync();
        _kubeCurrentContext = kubeCfg.CurrentContext ?? "unknown";

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
                    var lastCtx = DateTime.MinValue;
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

                            // Keyboard handling.
                            if (Console.KeyAvailable)
                            {
                                var key = Console.ReadKey(true);
                                var shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

                                switch (key.Key)
                                {
                                    case ConsoleKey.Home: _state.SelectedRow[_state.SelectedTable] = 0; redraw = true; break;
                                    case ConsoleKey.End: _state.SelectedRow[_state.SelectedTable] = count - 1; redraw = true; break;
                                    case ConsoleKey.LeftArrow: _state.ColumnOffset = Math.Max(0, _state.ColumnOffset - 1); redraw = true; break;
                                    case ConsoleKey.RightArrow: _state.ColumnOffset = Math.Min(_state.ColumnOffset + 1, _state.ColumnOffsetMax); redraw = true; break;
                                    case ConsoleKey.UpArrow: _state.SelectedRow[_state.SelectedTable] = Math.Max(0, _state.SelectedRow[_state.SelectedTable] - 1); redraw = true; break;
                                    case ConsoleKey.DownArrow: _state.SelectedRow[_state.SelectedTable] = Math.Min(Math.Max(0, count - 1), _state.SelectedRow[_state.SelectedTable] + 1); redraw = true; break;
                                    case ConsoleKey.D1: _state.SelectedTable = KrpTable.PortForwards; redraw = true; break;
                                    case ConsoleKey.D2: _state.SelectedTable = KrpTable.Logs; redraw = true; break;
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
                                
                                var detectChanges = _state.SelectedTable switch
                                {
                                    KrpTable.PortForwards => _portForwardTable.DetectChanges(),
                                    KrpTable.Logs => _logsTable.DetectChanges(),
                                    _ => false,
                                };

                                if (detectChanges)
                                {
                                    redraw = true;
                                }

                                lastCtx = DateTime.UtcNow;
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
            KrpTable.Logs => _logsTable.BuildPanel(),
            _ => throw new ArgumentOutOfRangeException(nameof(_state.SelectedTable), _state.SelectedTable, "Invalid selected table index."),
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

        if (_state.WindowSize == WindowSize.XS)
        {
            figlet.Centered();
        }

        return panel.NoBorder().Padding(1, 0, 0, 0);
    }
    
    private void ToggleSort(SortField field, ref bool redraw)
    {
        _state.SortAscending = _state.SortField != field || !_state.SortAscending;
        _state.SortField = field;
        _state.SelectedRow[_state.SelectedTable] = 0; // Reset cursor to top.
        redraw = true;
    }
}
