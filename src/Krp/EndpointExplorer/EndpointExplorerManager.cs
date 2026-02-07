using Krp.Endpoints;
using Krp.Kubernetes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Krp.EndpointExplorer;

/// <summary>
/// Provides functionality to discover and manage service endpoints using Kubernetes and configurable filtering.
/// </summary>
public class EndpointExplorerManager
{
    private static readonly TimeSpan _minBackoff = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _maxBackoff = TimeSpan.FromMinutes(5);

    private readonly IEndpointManager _endpointManager;
    private readonly IKubernetesClient _kubernetesClient;
    private readonly ILogger<EndpointExplorerManager> _logger;

    private readonly List<Regex> _compiledFilters = [];
    private readonly Channel<bool> _refreshChannel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropOldest });

    public EndpointExplorerManager(IEndpointManager endpointManager, IKubernetesClient kubernetesClient, IOptions<EndpointExplorerOptions> options, ILogger<EndpointExplorerManager> logger)
    {
        _endpointManager = endpointManager;
        _kubernetesClient = kubernetesClient;
        _logger = logger;

        foreach (var pattern in options.Value.Filter)
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            _compiledFilters.Add(new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
        }
    }

    public void RequestRefresh()
    {
        _refreshChannel.Writer.TryWrite(true);
    }

    public async Task RunDiscoveryCycleAsync(CancellationToken ct)
    {
        var backoff = _minBackoff;

        while (!ct.IsCancellationRequested)
        {
            using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            using var discoveryCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var discoveryTask = DiscoverEndpointsAsync(discoveryCts.Token);
            var refreshTask = WaitForRefreshAsync(refreshCts.Token);

            var completed = await Task.WhenAny(discoveryTask, refreshTask);
            if (completed == refreshTask)
            {
                await refreshCts.CancelAsync();
                await discoveryCts.CancelAsync();
                await IgnoreCancellationAsync(discoveryTask);
                backoff = _minBackoff;
                continue;
            }

            await refreshCts.CancelAsync();
            await IgnoreCancellationAsync(refreshTask);

            try
            {
                await discoveryTask;
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to discover endpoints: {Error}. Retrying in {Backoff}...", ex.Message, FormatBackoff(backoff));

                var refreshed = await WaitForRefreshOrDelayAsync(backoff, ct);
                backoff = refreshed ? _minBackoff : TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, _maxBackoff.TotalSeconds));
            }
        }
    }

    public async Task<bool> WaitForRefreshOrDelayAsync(TimeSpan delay, CancellationToken ct)
    {
        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var delayTask = Task.Delay(delay, ct);
        var refreshTask = WaitForRefreshAsync(refreshCts.Token);

        var completed = await Task.WhenAny(delayTask, refreshTask);
        if (completed == refreshTask)
        {
            return !ct.IsCancellationRequested;
        }

        await refreshCts.CancelAsync();
        await IgnoreCancellationAsync(refreshTask);
        return false;
    }

    private async Task DiscoverEndpointsAsync(CancellationToken ct)
    {
        _logger.LogInformation("Discovering endpoints...");

        var endpoints = await _kubernetesClient.FetchServices(_compiledFilters, ct);
        _endpointManager.AddEndpoints(endpoints.ToList());
        await _endpointManager.TriggerEndPointsChangedEventAsync();
    }

    private static async Task IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // Suppress cancellation.
        }
    }

    private static string FormatBackoff(TimeSpan ts)
    {
        if (ts < TimeSpan.FromSeconds(1))
        {
            return "0s";
        }

        var minutes = (int)ts.TotalMinutes;
        var seconds = ts.Seconds;

        return minutes > 0 ? $"{minutes}m {seconds}s" : $"{seconds}s";
    }

    private Task WaitForRefreshAsync(CancellationToken ct)
    {
        return _refreshChannel.Reader.ReadAsync(ct).AsTask();
    }
}
