using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.EndpointExplorer;

public class EndpointExplorerBackgroundService : BackgroundService
{
    private readonly EndpointExplorerService _service;

    public EndpointExplorerBackgroundService(EndpointExplorerService service)
    {
        _service = service;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await _service.RunAsync(ct);
    }
}
