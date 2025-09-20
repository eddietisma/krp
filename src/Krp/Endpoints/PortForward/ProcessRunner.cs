using Meziantou.Framework.Win32;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Krp.Endpoints.PortForward;

public record ProcessWrapper(Process Process, ConcurrentStack<string> Logs);

public class ProcessRunner
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(IServiceProvider serviceProvider, ILogger<ProcessRunner> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<ProcessWrapper> RunCommandAsync(string filename, string command)
    {
        _logger.LogInformation("Running command: '{filename} {command}'", filename, command);

        try
        {
            var logs = new ConcurrentStack<string>();

            var processStartInfo = new ProcessStartInfo
            {
                FileName = filename,
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            var process = Process.Start(processStartInfo);
            if (process == null)
            {
                return new ProcessWrapper(null, logs);
            }

            process.EnableRaisingEvents = true;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
#pragma warning disable CA1416
                // Assign process handle to ensure all child processes are killed when the application exits.
                var jobObject = _serviceProvider.GetRequiredService<JobObject>();
                jobObject.AssignProcess(process.Handle);
#pragma warning restore CA1416
            }

            var readyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            process.OutputDataReceived += OnOutputDataReceived(readyTcs, logs);
            process.ErrorDataReceived += OnErrorDataReceived(readyTcs, logs);
            process.Exited += OnExited(readyTcs);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await readyTcs.Task;

            return !readyTcs.Task.Result
                ? new ProcessWrapper(null, logs)
                : new ProcessWrapper(process, logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running process");
            throw;
        }
    }
    
    private DataReceivedEventHandler OnOutputDataReceived(TaskCompletionSource<bool> readyTcs, ConcurrentStack<string> logs)
    {
        return (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            _logger.LogDebug(e.Data);
            logs.Push(e.Data);

            if (e.Data.StartsWith("Forwarding from"))
            {
                readyTcs.TrySetResult(true);
            }
        };
    }

    private DataReceivedEventHandler OnErrorDataReceived(TaskCompletionSource<bool> readyTcs, ConcurrentStack<string> logs)
    {
        return (_, e) =>
        {
            if (e.Data == null)
            {
                return;
            }

            _logger.LogError(e.Data);
            logs.Push(e.Data);

            if (e.Data.Contains("Only one usage of each socket address"))
            {
                readyTcs.TrySetResult(false);
            }
        };
    }

    private EventHandler OnExited(TaskCompletionSource<bool> readyTcs)
    {
        return (_, _) =>
        {
            _logger.LogError("Process exited before signaling readiness");
            readyTcs.TrySetResult(false);
        };
    }
}