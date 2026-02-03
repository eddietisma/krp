namespace Krp.Dns;

public class DnsMasqOptions
{
    /// <summary>
    /// Path to the dnsmasq override configuration file.
    /// </summary>
    public string OverridePath { get; set; } = "/run/dnsmasq/krp.override.conf";
}
