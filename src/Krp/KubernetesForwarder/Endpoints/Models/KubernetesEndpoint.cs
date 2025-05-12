namespace Krp.KubernetesForwarder.Endpoints.Models;

public class KubernetesEndpoint
{
    public bool IsStatic { get; set; }
    public int LocalPort { get; set; }
    public string Namespace { get; set; }
    public int RemotePort { get; set; }
    public string Resource { get; set; }
}