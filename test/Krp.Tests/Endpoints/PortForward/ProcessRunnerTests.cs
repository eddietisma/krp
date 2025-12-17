using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Krp.Tests.Endpoints.PortForward;

[TestClass]
public class ProcessRunnerTests
{
    [TestMethod]
    public async Task MonitorScript_KillsChild_WhenParentExits()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Inconclusive("This test requires Linux/WSL (/proc).");
            return;
        }

        var probeProjectPath = FindProbeProjectPath();

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(probeProjectPath);

        using var probeProcess = Process.Start(startInfo);
        Assert.IsNotNull(probeProcess);

        var stderrTask = probeProcess.StandardError.ReadToEndAsync();
        var stdout = new StringBuilder();
        int monitorPid = 0;
        string pidFilePath = string.Empty;

        var readStdoutTask = Task.Run(async () =>
        {
            string line;
            while ((line = await probeProcess.StandardOutput.ReadLineAsync()) != null)
            {
                stdout.AppendLine(line);

                if (monitorPid == 0 && line.StartsWith("Started child pid:", StringComparison.Ordinal))
                {
                    var value = line.Substring("Started child pid:".Length).Trim();
                    if (int.TryParse(value, out var parsed))
                    {
                        monitorPid = parsed;
                    }
                }

                if (pidFilePath.Length == 0 && line.StartsWith("Child pid file:", StringComparison.Ordinal))
                {
                    pidFilePath = line.Substring("Child pid file:".Length).Trim();
                }
            }
        });

        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline && (monitorPid == 0 || pidFilePath.Length == 0))
            {
                await Task.Delay(50);
            }

            var stderr = await stderrTask;
            Assert.IsGreaterThan(0, monitorPid, $"Did not see 'Started child pid' from probe.\nstdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.AreNotEqual(string.Empty, pidFilePath, $"Did not see 'Child pid file' from probe.\nstdout:\n{stdout}\nstderr:\n{stderr}");

            await WaitForNonEmptyFileAsync(pidFilePath, TimeSpan.FromSeconds(5));
            var childPid = int.Parse(File.ReadAllText(pidFilePath).Trim());

            Assert.IsTrue(IsLinuxPidAlive(monitorPid), $"Expected monitor process {monitorPid} to be running.\nstdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.IsTrue(IsLinuxPidAlive(childPid), $"Expected child process {childPid} to be running.\nstdout:\n{stdout}\nstderr:\n{stderr}");

            await probeProcess.WaitForExitAsync();
            await readStdoutTask;

            deadline = DateTimeOffset.UtcNow.AddSeconds(10);
            while (DateTimeOffset.UtcNow < deadline && (IsLinuxPidAlive(monitorPid) || IsLinuxPidAlive(childPid)))
            {
                await Task.Delay(200);
            }

            Assert.IsFalse(IsLinuxPidAlive(monitorPid), $"Monitor still alive: {monitorPid}\nstdout:\n{stdout}\nstderr:\n{stderr}");
            Assert.IsFalse(IsLinuxPidAlive(childPid), $"Child still alive: {childPid}\nstdout:\n{stdout}\nstderr:\n{stderr}");
        }
        finally
        {
            try
            {
                if (!probeProcess.HasExited)
                {
                    probeProcess.Kill();
                }
            }
            catch
            {
                // Suppress
            }
        }
    }

    private static bool IsLinuxPidAlive(int pid) => Directory.Exists($"/proc/{pid}");

    private static async Task WaitForNonEmptyFileAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(path))
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Length > 0)
                    {
                        return;
                    }
                }
                catch
                {
                    // Suppress
                }
            }

            await Task.Delay(50);
        }

        Assert.Fail($"Timed out waiting for '{path}'.");
    }

    private static string FindProbeProjectPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "test", "Krp.ProcessRunnerProbe", "Krp.ProcessRunnerProbe.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        Assert.Fail("Could not locate test/ProcessRunnerProbe/ProcessRunnerProbe.csproj from test base directory.");
        return string.Empty;
    }
}
