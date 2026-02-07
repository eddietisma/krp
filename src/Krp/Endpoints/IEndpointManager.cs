using Krp.Endpoints.Models;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace Krp.Endpoints;

public interface IEndpointManager
{
    public event Func<Task>? EndPointsChangedEvent;

    public void Initialize();
    public void AddEndpoint(HttpEndpoint endpoint);
    public void AddEndpoint(KubernetesEndpoint endpoint);
    public void AddEndpoints(List<KubernetesEndpoint> endpoints);
    public IEndpointHandler? GetHttpEndpointByUrl(string host, string path);
    public IEnumerable<IEndpointHandler> GetHandlerByHost(string host);
    public IEndpointHandler? GetPortForwardHandlerByHost(string host);
    public IEndpointHandler? GetHandlerByIpPort(IPAddress ip);
    public IEnumerable<IEndpointHandler> GetAllHandlers();
    public void RemoveAllHandlers();
    public Task TriggerEndPointsChangedEventAsync();
}