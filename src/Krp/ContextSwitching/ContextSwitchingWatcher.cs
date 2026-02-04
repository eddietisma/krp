using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.ContextSwitching;

public class ContextSwitchingWatcher : BackgroundService
{
    private readonly ContextSwitchingService _service;

    public ContextSwitchingWatcher(ContextSwitchingService service)
    {
        _service = service;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await _service.RunAsync(ct);
    }
}
