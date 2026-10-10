namespace Krp.Dns;

public class DnsMasqOptions
{
    /// <summary>
    /// Path to the dnsmasq override configuration file.
    /// </summary>
    public string OverridePath { get; set; } = "/run/dnsmasq/krp.override.conf";

    /// <summary>
    /// Executable used to restart dnsmasq after configuration changes.
    /// </summary>
    public string? RestartCommand { get; set; }

    public string RestartArguments { get; set; } = string.Empty;
}
