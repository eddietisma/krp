using Krp.Https;
using Krp.Tool.Help;
using Krp.Tool.Commands;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;

namespace Krp.Tool;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        try
        {
            return await new HostBuilder()
                .ConfigureServices(services =>
                {
                    services.AddHttpsCertificateManagement();
                })
                .RunCommandLineApplicationAsync<RootCommand>(args, app =>
                {
                    app.HelpTextGenerator = new KrpHelpTextGenerator();
                    app.ExtendedHelpText = @"Environment variables:
  KRP_HOSTS                       Override path to hosts file";
                });
        }
        catch (UnrecognizedCommandParsingException)
        {
            return 1;
        }
    }
}
