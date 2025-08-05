using k8s;
using Krp.Common;
using System;
using System.IO;

namespace Krp.Validation;

public static class KubernetesValidator
{
    public static bool Validate()
    {
        var fileExists = File.Exists(KubernetesClientConfiguration.KubeConfigDefaultLocation);
        if (!fileExists)
        {
            Console.Error.WriteLine($"\u001b[31m • Kubernetes config file not found at '{KubernetesClientConfiguration.KubeConfigDefaultLocation}'\u001b[0m");
        }
        else
        {
            Console.WriteLine($" • Found kubeconfig at '{KubernetesClientConfiguration.KubeConfigDefaultLocation}'");
        }

        var hasAccess = KubernetesHelper.WaitForAccess(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
        if (!hasAccess)
        {
            Console.Error.WriteLine(
                "\u001b[31m • Unable to reach Kubernetes (30s timeout).\n" +
                $"   • Kube-config: {KubernetesClientConfiguration.KubeConfigDefaultLocation}\n" +
                "   • Authenticate or refresh credentials using your cloud CLI:\n" +
                "     ▸ Azure :  az login …\n" +
                "     ▸ AWS   :  aws login …\n" +
                "     ▸ GCP   :  gcloud auth login …\n" +
                "   • Or set KUBECONFIG to a valid file and retry.\u001b[0m");
        }
        else
        {
            Console.WriteLine($" • Successfully connected to context: {KubernetesClientConfiguration.BuildConfigFromConfigFile().CurrentContext}");
        }

        return fileExists && hasAccess;
    }
}