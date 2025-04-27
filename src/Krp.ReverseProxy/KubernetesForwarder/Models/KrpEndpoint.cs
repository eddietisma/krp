namespace Krp.ReverseProxy.KubernetesForwarder.Models;

public class KrpEndpoint
{
    public bool IsStatic { get; set; }
    public int LocalPort { get; set; }
    public string Namespace { get; set; }
    public int RemotePort { get; set; }
    public string Resource { get; set; }
    public string Type { get; set; }
}