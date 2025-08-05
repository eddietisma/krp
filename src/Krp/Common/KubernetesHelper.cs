using k8s;
using System;
using System.IO;
using System.Threading;

namespace Krp.Common;

public static class KubernetesHelper
{
    private static readonly Lock _lockObj = new();

    /// <summary>
    /// True if a kube-config file exists and contains a usable token.
    /// </summary>
    public static bool HasAccess()
    {
        try
        {
            lock (_lockObj)
            {
                var cfg = KubernetesClientConfiguration.BuildConfigFromConfigFile();
                return !string.IsNullOrEmpty(cfg.AccessToken);
            }
        }
        catch (k8s.Exceptions.KubeConfigException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Blocks until <see cref="HasAccess"/> returns true or the timeout expires.
    /// </summary>
    /// <param name="timeout">Maximum time to wait. Default is 30 seconds.</param>
    /// <param name="pollInterval">Delay between probes. Default is 5 seconds.</param>
    /// <returns>True if access was obtained inside the timeout; otherwise false.</returns>
    public static bool WaitForAccess(TimeSpan timeout, TimeSpan pollInterval)
    {
        var deadline = timeout <= TimeSpan.Zero
            ? DateTimeOffset.MaxValue
            : DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow <= deadline)
        {
            if (HasAccess())
            {
                return true;
            }

            Thread.Sleep(pollInterval);
        }

        return false;
    }
}