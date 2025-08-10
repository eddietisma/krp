﻿using Krp.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using System;

namespace Krp.Tool.TerminalUi.Logging;

public static class LoggingBuilderExtensions
{
    public static void AddKrpTerminalLogger(this ILoggingBuilder builder)
    {
        // Enable emojis in console output.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        builder.Services.AddSingleton<InMemoryLoggingProvider>();
        builder.Services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<InMemoryLoggingProvider>());

        builder.AddConsole(options =>
        {
            options.FormatterName = "krp";
        });

        builder.AddConsoleFormatter<KrpConsoleFormatter, SimpleConsoleFormatterOptions>(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });

        builder.AddFilter(_ => false);
        builder.AddFilter<InMemoryLoggingProvider>("Default", LogLevel.Information);
        builder.AddFilter<InMemoryLoggingProvider>("Krp", LogLevel.Trace);
        builder.AddFilter<InMemoryLoggingProvider>("Microsoft.AspNetCore", LogLevel.Warning);
        builder.AddFilter<InMemoryLoggingProvider>("Yarp.ReverseProxy.Forwarder.HttpForwarder", LogLevel.Warning);
        builder.AddFilter<InMemoryLoggingProvider>(level => level >= LogLevel.Debug);

        builder.Services.Configure<ConsoleLifetimeOptions>(options =>
        {
            // https://andrewlock.net/suppressing-the-startup-and-shutdown-messages-in-asp-net-core/
            options.SuppressStatusMessages = true;
        });
    }
}