using Krp.Tool.TerminalUi.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Krp.Tool.TerminalUi.DependencyInjection;

public static class WebApplicationBuilderExtensions
{
    public static WebApplicationBuilder AddKrpTerminalUi(this WebApplicationBuilder webApplicationBuilder)
    {
        webApplicationBuilder.Logging.AddKrpTerminalLogger();
        webApplicationBuilder.Services.AddSingleton<KrpTerminalUi>();
        return webApplicationBuilder;
    }
}