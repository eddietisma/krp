using Krp.Tool.TerminalUi.Extensions;
using System;

namespace Krp.Tool.TerminalUi.Tables;

public class ColumnDefinition<T>
{
    public ColumnDefinition(string header, int width, SortField sort, Func<T, string> valueSelector, bool allowGrow)
    {
        Header = header;
        Width = width;
        Sort = sort;
        ValueSelector = valueSelector;
        AllowGrow = allowGrow;
    }

    public string Header { get; set; }
    public int Width { get; set; }
    public SortField Sort { get; set; }
    public Func<T, string> ValueSelector { get; set; }
    public bool AllowGrow { get; set; }
}