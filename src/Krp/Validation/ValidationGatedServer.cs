using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Krp.Validation;

public class ValidationGatedServer : IServer
{
    private readonly IServer _inner;
    private readonly ValidationState _validationState;
    private readonly ILogger<ValidationGatedServer> _logger;

    public ValidationGatedServer(IServer inner, ValidationState validationState, ILogger<ValidationGatedServer> logger)
    {
        _inner = inner;
        _validationState = validationState;
        _logger = logger;
    }

    public IFeatureCollection Features => _inner.Features;

    public async Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken cancellationToken) where TContext : notnull
    {
        var succeeded = await _validationState.WaitForCompletionAsync(cancellationToken);
        if (!succeeded)
        {
            _logger.LogWarning("Skipping HTTP server start because validation failed");
        }

        await _validationState.WaitForValidAsync(cancellationToken);
        await _inner.StartAsync(application, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _inner.StopAsync(cancellationToken);
    }

    public void Dispose()
    {
        _inner.Dispose();
        GC.SuppressFinalize(this);
    }
}
