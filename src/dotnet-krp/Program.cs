using Krp.Tool.Commands;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;

namespace Krp.Tool;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        return await new HostBuilder()
            .RunCommandLineApplicationAsync<RootCommand>(args, app =>
            {
                app.ExtendedHelpText = @"
Environment variables:
  KRP_HOSTS                       Override path to hosts file
";
            });
    }
}