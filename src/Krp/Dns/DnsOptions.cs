namespace Krp.Dns;

public enum DnsOptions
{
    /// <summary>
    /// Use HOSTS file for DNS routing.
    /// </summary>
    HostsFile,

    /// <summary>
    /// Use WinDivert for DNS routing.
    /// </summary>
    WinDivert,
}
