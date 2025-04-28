using Krp.KubernetesForwarder.Models;
using System.Collections.Generic;

namespace Krp.DependencyInjection;

public class KubernetesForwarderOptions
{
    public List<KrpEndpoint> Endpoints { get; set; } = new();
}