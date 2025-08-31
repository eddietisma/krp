using System.Collections.Generic;
using System.Threading.Tasks;

namespace Krp.Dns;

public interface IDnsHandler
{
    Task UpdateAsync(List<string> hostnames);
}