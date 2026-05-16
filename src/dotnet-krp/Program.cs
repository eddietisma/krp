using Krp.Https.DependencyInjection;
using Krp.Tool.Commands;
using Krp.Tool.Help;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Hosting;
using System;
using System.IO;
using System.Linq;
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
                    if (args.Length > 0 && !args.Any(IsInfoOption))
                    {
                        app.Out = TextWriter.Null;
                    }

                    app.HelpTextGenerator = new KrpHelpTextGenerator();
                    app.ExtendedHelpText = @"Environment variables:
  KRP_HOSTS                       Override path to hosts file";
                });
        }
        catch (UnrecognizedCommandParsingException ex)
        {
            var message = ex.Command.Name == "krp" && !args[0].StartsWith('-')
               ? $"unknown command '{string.Join(" ", args)}'"
               : ex.Message;

            await Console.Error.WriteLineAsync($"krp: {message}");
            await Console.Error.WriteLineAsync();
            await Console.Error.WriteLineAsync("Run 'krp --help' for more information");

            return 1;
        }
    }

    private static bool IsInfoOption(string arg)
    {
        return arg is "--help" or "-h" or "-?" or "--version" or "-v";
    }
}
