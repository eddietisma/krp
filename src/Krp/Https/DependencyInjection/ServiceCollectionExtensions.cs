using Krp.Https.CertificateStores;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Krp.Https.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHttpsCertificateManagement(this IServiceCollection services)
    {
        services.AddOptions<CertificateOptions>();

        if (OperatingSystem.IsMacOS())
        {
            services.AddSingleton<ICertificateStore, MacCertificateStore>();
        }
        else if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<ICertificateStore, LinuxCertificateStore>();
        }
        else
        {
            services.AddSingleton<ICertificateStore, WindowsCertificateStore>();
        }

        services.AddSingleton<ICertificateManager, CertificateManager>();
        return services;
    }
}
