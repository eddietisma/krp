using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;

namespace Krp.Logging;

public static class LoggingBuilderExtensions
{
    public static void AddKrpLogger(this ILoggingBuilder builder)
    {
        // Enable emojis in console output.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        builder.AddConsole(options =>
        {
            options.FormatterName = "krp";
        });

        builder.AddConsoleFormatter<KrpConsoleFormatter, SimpleConsoleFormatterOptions>(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        builder.AddFilter("Default", LogLevel.Information);
        builder.AddFilter("Krp", LogLevel.None);
        builder.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.AddFilter("Yarp.ReverseProxy.Forwarder.HttpForwarder", LogLevel.Warning);

        builder.Services.Configure<ConsoleLifetimeOptions>(options =>
        {
            // https://andrewlock.net/suppressing-the-startup-and-shutdown-messages-in-asp-net-core/
            options.SuppressStatusMessages = true;
        });
    }
}