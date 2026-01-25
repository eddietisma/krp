namespace Krp.Https;

public class CertificateOptions
{
    public string DefaultCertificateAuthoritySubject { get; set; } = "CN=krp";
    public string DefaultHostName { get; set; } = "localhost";
}
