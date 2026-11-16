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
    private readonly K3sFixture _fixture;
    private readonly ConcurrentQueue<string> _logs = new();
    private Process _process;

    public string HostsPath => Path.Combine(_fixture.DirectoryPath, "hosts");
    public string Logs => string.Join(Environment.NewLine, _logs);

    public KrpTestProcess(K3sFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task StartAsync(string mode)
    {
        await File.WriteAllTextAsync(HostsPath, "127.0.0.1 localhost\n");
        var startInfo = _fixture.CreateStartInfo("dotnet", typeof(RootCommand).Assembly.Location, "--no-ui", "--routing", "hosts", "--forwarder", mode);

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
            return hosts.Contains("app-one.krp-tests-app-one", StringComparison.Ordinal) && hosts.Contains("app-two.krp-tests-app-two", StringComparison.Ordinal);
        });
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
                    await socket.ConnectAsync(IPAddress.Parse(entry[0]), context.DnsEndPoint.Port, token);
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
        if (_process == null)
        {
            return;
        }
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