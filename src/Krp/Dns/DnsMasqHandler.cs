using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Dns;

/// <summary>
/// Writes endpoint mappings to a dnsmasq configuration override.
/// <para>
/// Each hostname is written as an <c>address</c> directive pointing to the
/// assigned loopback IP. The override file is placed in a run-time directory so
/// it remains ephemeral.
/// </para>
/// </summary>
public class DnsMasqHandler : IDnsHandler
{
    private readonly DnsMasqOptions _options;
    private readonly ILogger<DnsMasqHandler> _logger;

    public DnsMasqHandler(IOptions<DnsMasqOptions> options, ILogger<DnsMasqHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken stoppingToken)
    {
        await Task.CompletedTask;
    }

    public async Task UpdateAsync(List<string> hostnames)
    {
        try
        {
            var configLines = hostnames
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length == 2)
                .Select(parts => $"address=/{parts[1]}/{parts[0]}")
                .ToList();

            var directory = Path.GetDirectoryName(_options.OverridePath);
            if (string.IsNullOrEmpty(directory))
            {
                _logger.LogError("dnsmasq override path is invalid: {path}", _options.OverridePath);
                return;
            }

            Directory.CreateDirectory(directory);

            var lines = new List<string>
            {
                "# Managed by krp",
                "# Generated dnsmasq overrides for loopback routing.",
            };

            lines.AddRange(configLines);

            if (File.Exists(_options.OverridePath))
            {
                var existing = await File.ReadAllLinesAsync(_options.OverridePath);
                if (existing.SequenceEqual(lines))
                {
                    _logger.LogInformation("Skipped updating DNS due to no changes in dnsmasq override file");
                    return;
                }
            }

            await File.WriteAllLinesAsync(_options.OverridePath, lines);
            _logger.LogInformation("Successfully updated dnsmasq override ({count} entries)", configLines.Count);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error when updating dnsmasq override file");
            throw;
        }
    }
}
