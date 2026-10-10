using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Dns;

/// <summary>
/// Writes endpoint mappings as dnsmasq address directives and restarts dnsmasq
/// after changes. The generated file is removed on shutdown.
/// </summary>
public class DnsMasqHandler : IDnsHandler
{
    private const string MANAGED_HEADER = "# Managed by krp";
    private readonly DnsMasqOptions _options;
    private readonly ILogger<DnsMasqHandler> _logger;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly CancellationTokenSource _stopping = new();
    private bool _restartRequired = true;

    public DnsMasqHandler(IOptions<DnsMasqOptions> options, ILogger<DnsMasqHandler> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task RunAsync(CancellationToken stoppingToken) => Task.CompletedTask;

    public async Task UpdateAsync(List<string> hostnames)
    {
        await _updateLock.WaitAsync();
        try
        {
            if (_stopping.IsCancellationRequested)
            {
                return;
            }

            var configLines = hostnames
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length == 2)
                .Select(parts => $"address=/{parts[1]}/{parts[0]}")
                .ToList();

            var lines = new List<string>
            {
                MANAGED_HEADER,
                "# Generated dnsmasq overrides for loopback routing.",
            };
            lines.AddRange(configLines);

            var overridePath = Path.GetFullPath(_options.OverridePath);
            var existing = File.Exists(overridePath) ? await File.ReadAllLinesAsync(overridePath, _stopping.Token) : [];
            if (!existing.SequenceEqual(lines))
            {
                var directory = Path.GetDirectoryName(overridePath)!;
                Directory.CreateDirectory(directory);
                var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(overridePath)}.{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllLinesAsync(temporaryPath, lines, _stopping.Token);
                    File.Move(temporaryPath, overridePath, overwrite: true);
                    _restartRequired = true;
                }
                finally
                {
                    File.Delete(temporaryPath);
                }
            }
            else if (!_restartRequired)
            {
                _logger.LogInformation("Skipped updating DNS due to no changes in dnsmasq override file");
                return;
            }

            await RestartDnsMasqAsync(_stopping.Token);
            _restartRequired = false;
            _logger.LogInformation("Successfully updated dnsmasq override ({count} entries)", configLines.Count);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Shutdown cancels an active update so cleanup can acquire the lock.
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error when updating dnsmasq override file");
            throw;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken stoppingToken)
    {
        _stopping.Cancel();
        await _updateLock.WaitAsync(stoppingToken);
        try
        {
            var overridePath = Path.GetFullPath(_options.OverridePath);
            if (!File.Exists(overridePath))
            {
                return;
            }

            var lines = await File.ReadAllLinesAsync(overridePath, stoppingToken);
            if (lines.Length == 0 || lines[0] != MANAGED_HEADER)
            {
                return;
            }

            File.Delete(overridePath);
            await RestartDnsMasqAsync(stoppingToken);
            _logger.LogInformation("Removed dnsmasq override file");
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private async Task RestartDnsMasqAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.RestartCommand))
        {
            throw new InvalidOperationException("KRP_DNSMASQ_RESTART_COMMAND must specify an executable that restarts dnsmasq.");
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = _options.RestartCommand,
            Arguments = _options.RestartArguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("Could not start the dnsmasq restart command.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var stdoutTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderrTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await stdoutTask;
            var stderr = await stderrTask;
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"dnsmasq restart command failed with exit code {process.ExitCode}: {stderr.Trim()}");
            }
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // The process already exited.
            }

            throw;
        }
    }
}
