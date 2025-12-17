using Krp.Endpoints.PortForward;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Krp.ProcessRunnerProbe
//
// Purpose:
// - Launches a long-running child process via `ProcessRunner` and then exits shortly after.
// - Used by the WSL/Linux integration test to verify the monitor logic kills the child when the parent exits.
//
// How it works:
// - Starts `/bin/sh -lc` that writes its PID to a temp file, prints a "Forwarding from ..." line (so ProcessRunner returns),
//   and then sleeps for a while.
// - Prints:
//   - `Started child pid: <pid>`  (the monitor process PID returned by ProcessRunner)
//   - `Child pid file: <path>`   (file containing the real `/bin/sh` child PID)
// - Waits ~2 seconds and exits. The monitor should terminate the sleeping child shortly after.
//
// How to run (Linux/WSL):
// - `dotnet run -c Release --project test/Krp.ProcessRunnerProbe/Krp.ProcessRunnerProbe.csproj`
// - While it runs, you can `cat` the printed pid file and `ps -p <pid>` to observe the child.
// - After the probe exits, the child PID should disappear within a couple seconds.

var childPidFile = Path.Combine(Path.GetTempPath(), $"krp-probe-child-{Guid.NewGuid():N}.pid");
const int sleepSeconds = 30;
const int delayBeforeExitSeconds = 2;

var services = new ServiceCollection();
services.AddLogging(builder =>
{
    builder.AddSimpleConsole(options =>
    {
        options.IncludeScopes = false;
        options.SingleLine = true;
    });
    builder.SetMinimumLevel(LogLevel.Warning);
});
services.AddSingleton<ProcessRunner>();

await using var provider = services.BuildServiceProvider();
var runner = provider.GetRequiredService<ProcessRunner>();

// Print the readiness line so ProcessRunner returns immediately, then sleep long enough that we can verify cleanup.
var script = $"echo $$ > '{childPidFile.Replace("'", "'\\''")}'; echo Forwarding from probe; sleep {sleepSeconds}";
var wrapper = await runner.RunCommandAsync("/bin/sh", "-lc", script);
Console.WriteLine($"Started child pid: {wrapper.Process?.Id}");
Console.WriteLine($"Child pid file: {childPidFile}");

await Task.Delay(TimeSpan.FromSeconds(delayBeforeExitSeconds));
Console.WriteLine("Parent exiting now; child should be gone shortly.");
