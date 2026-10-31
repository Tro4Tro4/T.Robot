using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Data.Sqlite;

namespace TRobot.Web.Security;

/// <summary>Explicit prerequisites for the organizer-only Production host. Never creates data or keys directories.</summary>
public sealed record HostingProductionConfiguration(
    IPAddress ListenAddress, int HttpsPort, X509Certificate2 Certificate,
    string DataProtectionKeysPath, string ConnectionString)
{
    public static HostingProductionConfiguration Load(IConfiguration configuration, string webRootPath,
        bool initializeDemo = false, bool initializeIdentity = false)
    {
        if (initializeDemo || initializeIdentity)
            throw Invalid("I comandi di inizializzazione sono ammessi solo in Development.");
        if (!bool.TryParse(configuration["Hosting:Enabled"], out var enabled) || !enabled)
            throw Invalid("Hosting:Enabled deve essere esplicitamente true.");

        var hosts = configuration["AllowedHosts"]?.Split(';', StringSplitOptions.TrimEntries);
        if (hosts is null || hosts.Length == 0 || hosts.Any(host => string.IsNullOrWhiteSpace(host)
            || host.Contains('*') || Uri.CheckHostName(host.Trim('[', ']')) == UriHostNameType.Unknown))
            throw Invalid("AllowedHosts deve contenere solo nomi host o indirizzi espliciti, senza wildcard o porte.");

        if (!IPAddress.TryParse(configuration["Hosting:ListenAddress"] ?? "127.0.0.1", out var address))
            throw Invalid("Hosting:ListenAddress deve essere un indirizzo IP.");
        var portText = configuration["Hosting:HttpsPort"];
        var port = 5443;
        if (portText is not null && (!int.TryParse(portText, out port) || port is < 1 or > 65535))
            throw Invalid("Hosting:HttpsPort deve essere compreso fra 1 e 65535.");

        var keys = configuration["Hosting:DataProtectionKeysPath"];
        if (string.IsNullOrWhiteSpace(keys) || !Path.IsPathFullyQualified(keys) || !Directory.Exists(keys))
            throw Invalid("Hosting:DataProtectionKeysPath deve essere una directory assoluta esistente.");
        var normalizedKeys = Path.GetFullPath(keys);
        var relativeKeys = Path.GetRelativePath(Path.GetFullPath(webRootPath), normalizedKeys);
        if (relativeKeys == "." || (!Path.IsPathRooted(relativeKeys) && relativeKeys != ".."
            && !relativeKeys.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw Invalid("Le chiavi Data Protection devono essere esterne a wwwroot.");

        SqliteConnectionStringBuilder connection;
        try { connection = new(configuration.GetConnectionString("TRobot") ?? ""); }
        catch (ArgumentException) { throw Invalid("ConnectionStrings:TRobot non valida."); }
        if (!Path.IsPathFullyQualified(connection.DataSource) || !File.Exists(connection.DataSource)
            || connection.Mode != SqliteOpenMode.ReadWrite)
            throw Invalid("SQLite richiede un file assoluto esistente e Mode=ReadWrite.");
        connection.ForeignKeys = true;

        var certificatePath = configuration["Hosting:CertificatePath"];
        if (string.IsNullOrWhiteSpace(certificatePath) || !Path.IsPathFullyQualified(certificatePath) || !File.Exists(certificatePath))
            throw Invalid("Hosting:CertificatePath deve essere un file PKCS#12 assoluto esistente.");
        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath,
                configuration["Hosting:CertificatePassword"], X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        { throw Invalid("Certificato HTTPS non leggibile o password non valida."); }
        var now = DateTime.UtcNow;
        var usages = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().ToArray();
        if (!certificate.HasPrivateKey || certificate.NotBefore.ToUniversalTime() > now || certificate.NotAfter.ToUniversalTime() <= now
            || (usages.Length > 0 && !usages.Any(extension => extension.EnhancedKeyUsages.Cast<Oid>()
                .Any(oid => oid.Value is "1.3.6.1.5.5.7.3.1" or "2.5.29.37.0"))))
        {
            certificate.Dispose();
            throw Invalid("Il certificato HTTPS deve avere chiave privata, validità corrente e uso server TLS.");
        }
        return new(address, port, certificate, normalizedKeys, connection.ToString());
    }

    private static InvalidOperationException Invalid(string message) => new("T.Robot Production: " + message);
}
