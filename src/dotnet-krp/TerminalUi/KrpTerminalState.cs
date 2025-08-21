using Krp.Tool.TerminalUi.Extensions;
using System.Collections.Generic;

namespace Krp.Tool.TerminalUi;

public enum WindowSize { XS, SM, MD, LG }
public enum KrpTable { PortForwards, Logs }

public class KrpTerminalState
{
    public Dictionary<KrpTable, int> SelectedRow { get; set; } = new();
    public KrpTable SelectedTable { get; set; }
    public SortField SortField { get; set; }
    public bool SortAscending { get; set; }

    /// <summary>
    /// Index of first visible column.
    /// </summary>
    public int ColumnOffset { get; set; }

    /// <summary>
    /// Right‑most allowed offset.
    /// </summary>
    public int ColumnOffsetMax { get; set; }
    
    /// <summary>
    /// True when right‑most col is trimmed.
    /// </summary>
    public bool LastColumnClipped { get; set; }

    /// <summary>
    /// Current Console.WindowHeight to prevent interops.
    /// </summary>
    public int WindowHeight { get; set; }

    /// <summary>
    /// Current Console.WindowWidth to prevent interops.
    /// </summary>
    public int WindowWidth { get; set; }

    /// <summary>
    /// True when console width increased.
    /// </summary>
    public bool WindowGrew { get; set; }

    /// <summary>
    /// Current window size category (XS, SM, MD, LG), used for responsive.
    /// </summary>
    public WindowSize WindowSize { get; set; }

    public void UpdateWindowSize(int width)
    {
        WindowSize = width switch
        {
            < 75 => WindowSize.XS,
            < 100 => WindowSize.SM,
            < 130 => WindowSize.MD,
            _ => WindowSize.LG,
        };
    }
}