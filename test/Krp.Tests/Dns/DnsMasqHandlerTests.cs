using Krp.Dns;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Krp.Tests.Dns;

[TestClass]
public sealed class DnsMasqHandlerTests
{
    private string _directory;
    private string _callsPath;
    private string _failurePath;
    private DnsMasqOptions _options;
    private DnsMasqHandler _sut;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"krp dnsmasq {Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _callsPath = Path.Combine(_directory, "restarts");
        _failurePath = Path.Combine(_directory, "fail");
        _options = new DnsMasqOptions
        {
            OverridePath = Path.Combine(_directory, "config", "krp.override.conf"),
        };

        var scriptPath = Path.Combine(_directory, OperatingSystem.IsWindows() ? "restart.cmd" : "restart.sh");
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(scriptPath, $"@echo off\r\nif exist \"{_options.OverridePath}\" (echo present>>\"{_callsPath}\") else (echo missing>>\"{_callsPath}\")\r\nif exist \"{_failurePath}\" exit /b 1\r\nexit /b 0\r\n");
            _options.RestartCommand = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            _options.RestartArguments = $"/d /c \"\"{scriptPath}\"\"";
        }
        else
        {
            File.WriteAllText(scriptPath, $"#!/bin/sh\nif [ -f '{_options.OverridePath}' ]; then echo present >> '{_callsPath}'; else echo missing >> '{_callsPath}'; fi\nif [ -f '{_failurePath}' ]; then echo 'restart failed' >&2; exit 1; fi\n");
            _options.RestartCommand = "/bin/sh";
            _options.RestartArguments = $"\"{scriptPath}\"";
        }

        _sut = CreateHandler();
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldWriteConfigurationBeforeRestart_WhenUsingCustomPath()
    {
        await _sut.UpdateAsync(["127.0.0.2 app.local", "127.0.0.3 api.local"]);

        var lines = await File.ReadAllLinesAsync(_options.OverridePath);
        CollectionAssert.Contains(lines, "address=/app.local/127.0.0.2");
        CollectionAssert.Contains(lines, "address=/api.local/127.0.0.3");
        CollectionAssert.AreEqual(new[] { "present" }, await File.ReadAllLinesAsync(_callsPath));
        Assert.AreEqual(1, Directory.GetFiles(Path.GetDirectoryName(_options.OverridePath)).Length);
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldSkipRestart_WhenConfigurationIsUnchanged()
    {
        await _sut.UpdateAsync(["127.0.0.2 app.local"]);
        await _sut.UpdateAsync(["127.0.0.2 app.local"]);

        Assert.AreEqual(1, File.ReadAllLines(_callsPath).Length);
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldRestart_WhenExistingConfigurationMatchesOnStartup()
    {
        await _sut.UpdateAsync(["127.0.0.2 app.local"]);
        var newHandler = CreateHandler();

        await newHandler.UpdateAsync(["127.0.0.2 app.local"]);

        Assert.AreEqual(2, File.ReadAllLines(_callsPath).Length);
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldRetryRestart_WhenPreviousRestartFailed()
    {
        File.WriteAllText(_failurePath, "fail");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.UpdateAsync(["127.0.0.2 app.local"]));
        File.Delete(_failurePath);

        await _sut.UpdateAsync(["127.0.0.2 app.local"]);

        Assert.AreEqual(2, File.ReadAllLines(_callsPath).Length);
    }

    [TestMethod]
    public async Task UpdateAsync_ShouldRemoveOldMappings_WhenEndpointsAreEmpty()
    {
        await _sut.UpdateAsync(["127.0.0.2 app.local"]);
        await _sut.UpdateAsync([]);

        Assert.IsFalse(File.ReadAllLines(_options.OverridePath).Any(line => line.StartsWith("address=")));
        Assert.AreEqual(2, File.ReadAllLines(_callsPath).Length);
    }

    [TestMethod]
    public async Task StopAsync_ShouldRemoveConfigurationAndRestart_WhenFileIsManaged()
    {
        await _sut.UpdateAsync(["127.0.0.2 app.local"]);

        await _sut.StopAsync(CancellationToken.None);
        await _sut.UpdateAsync(["127.0.0.3 api.local"]);

        Assert.IsFalse(File.Exists(_options.OverridePath));
        CollectionAssert.AreEqual(new[] { "present", "missing" }, File.ReadAllLines(_callsPath));
    }

    [TestMethod]
    public async Task StopAsync_ShouldCancelUpdateAndRemoveConfiguration_WhenRestartIsRunning()
    {
        var scriptPath = Path.Combine(_directory, OperatingSystem.IsWindows() ? "restart.cmd" : "restart.sh");
        var script = OperatingSystem.IsWindows()
            ? $"@echo off\r\nif exist \"{_options.OverridePath}\" (echo present>>\"{_callsPath}\" & ping -n 30 127.0.0.1 >nul) else (echo missing>>\"{_callsPath}\")\r\nexit /b 0\r\n"
            : $"#!/bin/sh\nif [ -f '{_options.OverridePath}' ]; then echo present >> '{_callsPath}'; sleep 30; else echo missing >> '{_callsPath}'; fi\n";
        File.WriteAllText(scriptPath, script);

        var update = _sut.UpdateAsync(["127.0.0.2 app.local"]);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!File.Exists(_callsPath))
        {
            await Task.Delay(10, timeout.Token);
        }

        await _sut.StopAsync(timeout.Token);
        await update;

        Assert.IsFalse(File.Exists(_options.OverridePath));
        CollectionAssert.AreEqual(new[] { "present", "missing" }, File.ReadAllLines(_callsPath));
    }

    [TestMethod]
    public async Task StopAsync_ShouldPreserveConfiguration_WhenFileIsNotManaged()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_options.OverridePath));
        const string existing = "address=/existing.local/10.0.0.1";
        File.WriteAllText(_options.OverridePath, existing);

        await _sut.StopAsync(CancellationToken.None);

        Assert.AreEqual(existing, File.ReadAllText(_options.OverridePath));
        Assert.IsFalse(File.Exists(_callsPath));
    }

    private DnsMasqHandler CreateHandler() => new(Options.Create(_options), NullLogger<DnsMasqHandler>.Instance);
}
