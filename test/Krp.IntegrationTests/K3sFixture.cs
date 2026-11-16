using Krp.Tool.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System;
using Testcontainers.K3s;

namespace Krp.IntegrationTests;

internal sealed class K3sFixture : IAsyncDisposable
{
    private readonly K3sContainer _container;
    private bool _createdCertificateAuthority;

    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), $"krp-tests-{Guid.NewGuid():N}");
    public string KubeConfigPath => Path.Combine(DirectoryPath, ".kube", "config");
    public string KubectlPath => OperatingSystem.IsLinux() ? Path.Combine(DirectoryPath, "kubectl") : "kubectl";
    public string CertificateAuthorityPath => Path.Combine(DirectoryPath, "ca.pem");

    public K3sFixture()
    {
        var builder = new K3sBuilder("rancher/k3s:v1.35.5-k3s1");
        var hostOverride = Environment.GetEnvironmentVariable("TESTCONTAINERS_HOST_OVERRIDE");
        if (!string.IsNullOrWhiteSpace(hostOverride))
        {
            builder = builder.WithCommand("--tls-san", hostOverride);
        }
        _container = builder.Build();
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(KubeConfigPath)!);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await _container.StartAsync(timeout.Token);
        var config = await _container.GetKubeconfigAsync();
        await File.WriteAllTextAsync(KubeConfigPath, config);
        if (OperatingSystem.IsLinux())
        {
            // k3s also runs as kubectl when invoked with that file name.
            var kubectl = await _container.ReadFileAsync("/bin/k3s", timeout.Token);
            await File.WriteAllBytesAsync(KubectlPath, kubectl, timeout.Token);
            File.SetUnixFileMode(KubectlPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        await KubectlAsync("apply", "-f", Path.Combine(AppContext.BaseDirectory, "k3s.yaml"));
        foreach (var app in new[] { "app-one", "app-two" })
        {
            await KubectlAsync("rollout", "status", $"deployment/{app}", "-n", $"krp-tests-{app}", "--timeout=60s");
        }

        var certificateCheck = await RunAsync("dotnet", [typeof(RootCommand).Assembly.Location, "https", "--check"], assertSuccess: false);
        Assert.IsTrue(certificateCheck.ExitCode is 0 or 1, certificateCheck.Output);
        await RunAsync("dotnet", [typeof(RootCommand).Assembly.Location, "https"]);
        _createdCertificateAuthority = certificateCheck.ExitCode != 0;
        await RunAsync("dotnet", [typeof(RootCommand).Assembly.Location, "https", "--check"]);
        await RunAsync("dotnet", [typeof(RootCommand).Assembly.Location, "https", "--export", CertificateAuthorityPath, "--format", "pem"]);
    }

    public async Task<string> KubectlAsync(params string[] arguments)
    {
        var result = await RunAsync(KubectlPath, ["--kubeconfig", KubeConfigPath, .. arguments]);
        return result.Output;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _container.DisposeAsync();
        }
        finally
        {
            try
            {
                if (_createdCertificateAuthority)
                {
                    await RunAsync("dotnet", [typeof(RootCommand).Assembly.Location, "https", "--clean"]);
                    var certificateCheck = await RunAsync("dotnet", [typeof(RootCommand).Assembly.Location, "https", "--check"], assertSuccess: false);
                    Assert.AreEqual(1, certificateCheck.ExitCode, certificateCheck.Output);
                    _createdCertificateAuthority = false;
                }
            }
            finally
            {
                if (Directory.Exists(DirectoryPath))
                {
                    var path = Path.GetFullPath(DirectoryPath);
                    Assert.AreEqual(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(path));
                    Directory.Delete(path, recursive: true);
                }
            }
        }
    }

    public ProcessStartInfo CreateStartInfo(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = DirectoryPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        startInfo.Environment["USERPROFILE"] = DirectoryPath;
        startInfo.Environment["HOME"] = DirectoryPath;
        startInfo.Environment["KUBECONFIG"] = KubeConfigPath;
        startInfo.Environment["KRP_HOSTS"] = Path.Combine(DirectoryPath, "hosts");
        startInfo.Environment["PATH"] = DirectoryPath + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        startInfo.Environment["Logging__LogLevel__Microsoft.AspNetCore.Server.Kestrel"] = "Debug";
        return startInfo;
    }

    private async Task<(int ExitCode, string Output)> RunAsync(string fileName, string[] arguments, bool assertSuccess = true)
    {
        var startInfo = CreateStartInfo(fileName, arguments);
        using var process = Process.Start(startInfo);
        Assert.IsNotNull(process);
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (assertSuccess)
        {
            Assert.AreEqual(0, process.ExitCode, $"{fileName} {string.Join(' ', arguments)} failed.\nstdout:\n{stdout}\nstderr:\n{stderr}");
        }
        return (process.ExitCode, stdout + stderr);
    }
}