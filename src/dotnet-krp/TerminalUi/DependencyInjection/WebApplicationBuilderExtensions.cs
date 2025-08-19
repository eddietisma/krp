using Krp.Tool.TerminalUi.Logging;
using Krp.Tool.TerminalUi.Tables;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Krp.Tool.TerminalUi.DependencyInjection;

public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddKrpTerminalUi(this WebApplicationBuilder webApplicationBuilder)
    {
        webApplicationBuilder.Logging.AddKrpTerminalLogger();
        webApplicationBuilder.Services.AddSingleton<KrpTerminalUi>();
        webApplicationBuilder.Services.AddSingleton<KrpTerminalState>();
        webApplicationBuilder.Services.AddSingleton<LogsTable>();
        webApplicationBuilder.Services.AddSingleton<PortForwardTable>();
        return webApplicationBuilder;
    }
}