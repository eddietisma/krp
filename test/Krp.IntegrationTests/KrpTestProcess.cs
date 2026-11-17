using Krp.Tool.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Threading;
using System;

namespace Krp.IntegrationTests;

internal sealed class KrpTestProcess : IAsyncDisposable
{
    private static readonly HashSet<int> AllocatedPorts = new();
    private readonly K3sFixture _fixture;
    private readonly ConcurrentQueue<string> _logs = new();
    private readonly string _namespacePrefix = $"krp-tests-{Guid.NewGuid():N}";
    private readonly string _directoryPath;
    private Process _process;
    private bool _resourcesCreated;
    private int _httpPort;
    private int _httpsPort;

    public string HostsPath => Path.Combine(_directoryPath, "hosts");
    public string Logs => string.Join(Environment.NewLine, _logs);

    public KrpTestProcess(K3sFixture fixture)
    {
        _fixture = fixture;
        _directoryPath = Path.Combine(fixture.DirectoryPath, _namespacePrefix);
    }

    public string GetNamespace(string app) => $"{_namespacePrefix}-{app}";

    public string GetUrl(string app, string path = "/", bool https = false) => $"{(https ? "https" : "http")}://{app}.{GetNamespace(app)}{path}";

    public async Task StartAsync(string mode)
    {
        Directory.CreateDirectory(_directoryPath);
        var manifestPath = Path.Combine(_directoryPath, "k3s.yaml");
        var manifest = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "k3s.yaml"));
        await File.WriteAllTextAsync(manifestPath, manifest.Replace("krp-tests-", $"{_namespacePrefix}-", StringComparison.Ordinal));
        _resourcesCreated = true;
        await _fixture.KubectlAsync("apply", "-f", manifestPath);
        await Task.WhenAll(new[] { "app-one", "app-two" }.Select(app => _fixture.KubectlAsync("rollout", "status", $"deployment/{app}", "-n", GetNamespace(app), "--timeout=60s")));

        await File.WriteAllTextAsync(HostsPath, "127.0.0.1 localhost\n");
        var startInfo = _fixture.CreateStartInfo("dotnet", typeof(RootCommand).Assembly.Location, "--no-ui", "--routing", "hosts", "--forwarder", mode);
        startInfo.Environment["KRP_HOSTS"] = HostsPath;
        startInfo.Environment["Krp__EndpointExplorer__Filter__0"] = $"namespace/{_namespacePrefix}-*/service/*";

        // Reserve distinct ports until immediately before launching the child process.
        using (var http = ReservePort())
        using (var https = ReservePort())
        {
            _httpPort = ((IPEndPoint)http.LocalEndpoint).Port;
            _httpsPort = ((IPEndPoint)https.LocalEndpoint).Port;
        }
        startInfo.Environment["Krp__HttpPort"] = _httpPort.ToString();
        startInfo.Environment["Krp__HttpsPort"] = _httpsPort.ToString();
        startInfo.Environment["Krp__Http2Port"] = "0";

        _process = new Process { StartInfo = startInfo };
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                _logs.Enqueue(e.Data);
            }
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                _logs.Enqueue(e.Data);
            }
        };
        Assert.IsTrue(_process.Start());
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilAsync(async () =>
        {
            Assert.IsFalse(_process.HasExited, Logs);
            var hosts = string.Join('\n', await ReadHostsAsync());
            return hosts.Contains($"app-one.{GetNamespace("app-one")}", StringComparison.Ordinal) && hosts.Contains($"app-two.{GetNamespace("app-two")}", StringComparison.Ordinal);
        });
    }

    private static TcpListener ReservePort()
    {
        lock (AllocatedPorts)
        {
            while (true)
            {
                var listener = new TcpListener(IPAddress.Any, 0);
                listener.Start();
                if (AllocatedPorts.Add(((IPEndPoint)listener.LocalEndpoint).Port))
                {
                    return listener;
                }
                listener.Stop();
            }
        }
    }

    public HttpClient CreateClient(X509Certificate2 certificateAuthority = null)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = async (context, token) =>
            {
                var hosts = await ReadHostsAsync(token);
                var entry = hosts.Select(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Single(x => x.Length == 2 && x[1] == context.DnsEndPoint.Host);
                var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    var port = context.DnsEndPoint.Port == 443 ? _httpsPort : _httpPort;
                    await socket.ConnectAsync(IPAddress.Parse(entry[0]), port, token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        if (certificateAuthority != null)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
            {
                if (certificate == null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
                {
                    return false;
                }

                using var chain = new X509Chain();
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(certificateAuthority);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                return chain.Build((X509Certificate2)certificate);
            };
        }
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public int[] GetForwardedPorts()
    {
        const string prefix = "Forwarding from 127.0.0.1:";
        return _logs.Where(x => x.Contains(prefix, StringComparison.Ordinal))
            .Select(x => int.Parse(x[(x.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length)..].Split(' ')[0]))
            .ToArray();
    }

    private async Task<string[]> ReadHostsAsync(CancellationToken token = default)
    {
        await using var stream = new FileStream(HostsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var hosts = await reader.ReadToEndAsync(token);
        return hosts.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    public async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }
            await Task.Delay(100);
        }
        Assert.Fail($"Timed out waiting for KRP.\n{Logs}");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_process != null)
            {
                if (!_process.HasExited)
                {
                    // Kill only the parent so Windows Job Object cleanup must stop kubectl.
                    _process.Kill();
                    await _process.WaitForExitAsync();
                }
                _process.Dispose();
                _process = null;
            }
        }
        finally
        {
            if (_resourcesCreated)
            {
                await _fixture.KubectlAsync("delete", "namespace", GetNamespace("app-one"), GetNamespace("app-two"), "--ignore-not-found=true", "--wait=false");
                _resourcesCreated = false;
            }
        }
    }
}