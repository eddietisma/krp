using Krp.KubernetesForwarder.Endpoints;
using System.Collections.Generic;

namespace Krp.DependencyInjection;

public class KubernetesForwarderOptions
{
    public List<KrpEndpoint> Endpoints { get; set; } = new();
    public List<KrpHttpEndpoint> HttpEndpoints { get; set; } = new();
}