namespace Krp.Endpoints.Models;

public class HttpEndpoint
{
    public string Host { get; set; } = string.Empty;
    public int LocalPort { get; set; }
    public string LocalScheme { get; set; } = string.Empty;
    public string? Path { get; set; }
}
