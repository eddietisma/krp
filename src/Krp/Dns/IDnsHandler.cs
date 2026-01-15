using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Dns;

public interface IDnsHandler
{
    Task RunAsync(CancellationToken stoppingToken);
    Task UpdateAsync(List<string> hostnames);
    Task StopAsync(CancellationToken stoppingToken);
}
