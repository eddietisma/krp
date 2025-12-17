using Meziantou.Framework.Win32;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Krp.Endpoints.PortForward;

public record ProcessWrapper(Process Process, ConcurrentStack<string> Logs);

public class ProcessRunner
{
    private const string PROCESS_REAPER_SCRIPT = """
        #!/bin/sh
        parent="$1"
        parent_start="$2"
        shift 2

        # parent_start is an OS-specific "parent identity" token used to avoid PID reuse issues:
        # - Linux/WSL: /proc/<pid>/stat field 22 (starttime in clock ticks)
        # - macOS: `ps -o lstart= -p <pid>` (process start time string)
        use_proc=0
        use_ps=0
        if [ -n "$parent_start" ]; then
          if [ -d "/proc" ] && [ -r "/proc/1/stat" ]; then
            use_proc=1
          elif command -v ps >/dev/null 2>&1; then
            use_ps=1
          fi
        fi

        start_child() {
          if command -v setsid >/dev/null 2>&1; then
            setsid "$@" &
          elif command -v perl >/dev/null 2>&1; then
            perl -MPOSIX -e 'POSIX::setsid(); exec @ARGV' "$@" &
          elif command -v python3 >/dev/null 2>&1; then
            python3 -c 'import os,sys; os.setsid(); os.execvp(sys.argv[1], sys.argv[1:])' "$@" &
          else
            "$@" &
          fi
          child=$!
        }

        # Start child in a new session when possible (so we can kill the whole session/process-group).
        start_child "$@"

        kill_term() { kill -TERM -- "$1" >/dev/null 2>&1 || kill -TERM "$1" >/dev/null 2>&1 || true; }
        kill_kill() { kill -KILL -- "$1" >/dev/null 2>&1 || kill -KILL "$1" >/dev/null 2>&1 || true; }

        cleanup_child() {
          kill_term "-$child" || kill_term "$child"

          # If it's already gone, don't delay (avoids SIGKILL'ing a PID-reused process).
          kill -0 "$child" >/dev/null 2>&1 || return 0

          sleep 0.2
          kill_kill "-$child" || kill_kill "$child"
        }

        normal_exit=0
        cleanup_on_exit() { [ "$normal_exit" -eq 1 ] && return 0; cleanup_child; }
        trap cleanup_on_exit INT TERM HUP EXIT

        (
          while true; do
            if [ "$use_proc" -eq 1 ]; then
              current_start=$(awk '{print $22}' "/proc/$parent/stat" 2>/dev/null || true)
              [ -n "$current_start" ] || break
              [ "$current_start" = "$parent_start" ] || break
            elif [ "$use_ps" -eq 1 ]; then
              current_start=$(ps -o lstart= -p "$parent" 2>/dev/null | awk '{$1=$1};1')
              [ -n "$current_start" ] || break
              [ "$current_start" = "$parent_start" ] || break
            else
              kill -0 "$parent" >/dev/null 2>&1 || break
            fi
            sleep 1
          done
          cleanup_child
        ) &
        watcher=$!

        wait "$child"
        status=$?
        normal_exit=1
        trap - EXIT

        kill "$watcher" >/dev/null 2>&1
        exit "$status"
        """;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(IServiceProvider serviceProvider, ILogger<ProcessRunner> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<ProcessWrapper> RunCommandAsync(string fileName, params string[] args)
    {
        var command = $"{fileName} {string.Join(' ', args)}";
        _logger.LogInformation("Running command: '{command}'", command);

        try
        {
            var logs = new ConcurrentStack<string>();

            var processStartInfo = BuildProcessStartInfo(fileName, args);
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
            process.Exited += OnExited(readyTcs, command);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (processStartInfo.RedirectStandardInput)
            {
                try
                {
                    await process.StandardInput.WriteAsync(PROCESS_REAPER_SCRIPT.Replace("\r\n", "\n").Replace("\r", "\n"));
                    process.StandardInput.Close();
                }
                catch
                {
                    // Best-effort; if this fails, the process will likely exit and be handled below.
                }
            }

            var completed = await Task.WhenAny(readyTcs.Task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (completed != readyTcs.Task || !readyTcs.Task.Result)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Suppress
                }
                return new ProcessWrapper(null, logs);
            }

            return new ProcessWrapper(process, logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running process");
            throw;
        }
    }

    private ProcessStartInfo BuildProcessStartInfo(string fileName, string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            startInfo.FileName = fileName;
            startInfo.RedirectStandardInput = false;
            foreach (var arg in args ?? [])
            {
                startInfo.ArgumentList.Add(arg);
            }
            return startInfo;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Avoid shell parsing by passing argv directly through the monitor script (provided on stdin).
            startInfo.FileName = "/bin/sh";
            startInfo.ArgumentList.Add("-s");
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add(GetUnixProcessStartToken());
            startInfo.ArgumentList.Add(fileName);
            foreach (var arg in args ?? [])
            {
                startInfo.ArgumentList.Add(arg);
            }
            return startInfo;
        }

        throw new NotImplementedException("Unsupported OS");
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

    private EventHandler OnExited(TaskCompletionSource<bool> readyTcs, string command)
    {
        return (_, _) =>
        {
            _logger.LogError("Process exited: '{command}'", command);
            readyTcs.TrySetResult(false);
        };
    }

    private static string GetUnixProcessStartToken()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var stat = File.ReadAllText("/proc/self/stat");
                var closeParenIndex = stat.IndexOf(')');
                if (closeParenIndex < 0 || closeParenIndex + 2 >= stat.Length)
                {
                    return string.Empty;
                }

                var after = stat[(closeParenIndex + 2)..];
                var fields = after.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return fields.Length > 19 ? fields[19] : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            try
            {
                var psPath = File.Exists("/bin/ps") ? "/bin/ps" : "/usr/bin/ps";
                if (!File.Exists(psPath))
                {
                    return string.Empty;
                }

                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = psPath,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    ArgumentList = { "-o", "lstart=", "-p", Environment.ProcessId.ToString(), },
                });

                if (process == null)
                {
                    return string.Empty;
                }

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1000);
                return output.Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        return string.Empty;
    }
}
