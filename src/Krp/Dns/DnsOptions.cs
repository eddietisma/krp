namespace Krp.Dns;

public enum DnsOptions
{
    /// <summary>
    /// Use Windows HOSTS file DNS routing.
    /// </summary>
    WindowsHostsFile,

    /// <summary>
    /// Use WinDivert for DNS routing.
    /// </summary>
    WinDivert,
}
