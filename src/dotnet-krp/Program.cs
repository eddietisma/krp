using Krp.Tool.Commands;
using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;

namespace Krp.Tool;

public class Program
{
    public async static Task Main(string[] args)
    {
        await Host.CreateDefaultBuilder(args).RunCommandLineApplicationAsync<StartCommand>(args);
    }
}