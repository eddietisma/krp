using System;

namespace Krp.EndpointExplorer;

public class EndpointExplorerOptions
{
    /// <summary>
    /// Interval to refresh and discover new endpoints.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; }

    /// <summary>
    /// Filtering options for the discovered endpoints.
    /// <example>
    /// <br/><br/>
    ///  - namespace/*<br/>
    ///  - namespace/sharedsvcs/*<br/>
    ///  - namespace/sharedsvcs/service/customer-*<br/>
    ///  - namespace/sharedsvcs/service/customer-api<br/>
    /// </example>
    /// </summary>
    public string[] Filter { get; set; } = [];
}