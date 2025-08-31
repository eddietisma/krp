using Krp.Tool.Commands;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;

namespace Krp.Tool;

public class Program
{
    public async static Task Main(string[] args)
    {
        await new HostBuilder().RunCommandLineApplicationAsync<RootCommand>(args);
    }
}