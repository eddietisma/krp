namespace Krp.Endpoints.Models;

public class KubernetesEndpoint
{
    public bool IsStatic { get; set; }
    public int LocalPort { get; set; }
    public string Host { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public string Resource { get; set; } = string.Empty;
}
