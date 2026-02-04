namespace Krp.Endpoints.Models;

public class HttpEndpoint
{
    public required string Host { get; set; }
    public int LocalPort { get; set; }
    public required string LocalScheme { get; set; }
    public string? Path { get; set; }
}