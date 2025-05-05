namespace Krp.KubernetesForwarder.Endpoints;

public class KrpHttpEndpoint
{
    public string Host { get; set; }
    public int LocalPort { get; set; }
    public string Path { get; set; }
}