using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TRobot.Application.WhatsApp;
using TRobot.Web.Security;
using TRobot.Web.Services;

namespace TRobot.Tests;

public sealed class ProductionHostingTests
{
    [Theory]
    [InlineData("Hosting:Enabled", null)]
    [InlineData("Hosting:Enabled", "false")]
    [InlineData("Hosting:Enabled", "invalid")]
    [InlineData("AllowedHosts", "*")]
    [InlineData("AllowedHosts", "*.example.test")]
    [InlineData("AllowedHosts", "example.test;")]
    [InlineData("AllowedHosts", "https://example.test")]
    [InlineData("AllowedHosts", "example.test:5443")]
    [InlineData("Hosting:ListenAddress", "example.test")]
    [InlineData("Hosting:HttpsPort", "0")]
    [InlineData("Hosting:HttpsPort", "65536")]
    [InlineData("Hosting:HttpsPort", "invalid")]
    [InlineData("Hosting:DataProtectionKeysPath", "relative-keys")]
    [InlineData("ConnectionStrings:TRobot", "Data Source=relative.db;Mode=ReadWrite")]
    [InlineData("ConnectionStrings:TRobot", "Data Source=:memory:;Mode=Memory")]
    [InlineData("ConnectionStrings:TRobot", "Malformed")]
    [InlineData("Hosting:CertificatePath", "relative.pfx")]
    [InlineData("Hosting:CertificatePassword", "wrong-password")]
    public void Invalid_configuration_is_rejected(string key, string? value)
    {
        using var files = new ProductionFiles();
        files.Settings[key] = value;
        Assert.Throws<InvalidOperationException>(() => files.Load());
    }

    [Fact]
    public void Existing_database_and_absolute_keys_are_required_without_creating_files()
    {
        using var files = new ProductionFiles();
        files.Settings["ConnectionStrings:TRobot"] = $"Data Source={Path.Combine(files.Root, "missing.db")};Mode=ReadWrite";
        Assert.Throws<InvalidOperationException>(() => files.Load());
        Assert.False(File.Exists(Path.Combine(files.Root, "missing.db")));
        files.Settings["ConnectionStrings:TRobot"] = $"Data Source={files.DatabasePath}";
        Assert.Throws<InvalidOperationException>(() => files.Load());
        files.Settings["ConnectionStrings:TRobot"] += ";Mode=ReadWrite";
        files.Settings["Hosting:DataProtectionKeysPath"] = Path.Combine(files.Root, "missing-keys");
        Assert.Throws<InvalidOperationException>(() => files.Load());
        Assert.False(Directory.Exists(Path.Combine(files.Root, "missing-keys")));
    }

    [Fact]
    public void Public_key_directory_and_initialization_are_rejected()
    {
        using var files = new ProductionFiles();
        files.Settings["Hosting:DataProtectionKeysPath"] = files.WebRoot;
        Assert.Throws<InvalidOperationException>(() => files.Load());
        var child = Directory.CreateDirectory(Path.Combine(files.WebRoot, "keys")).FullName;
        files.Settings["Hosting:DataProtectionKeysPath"] = child;
        Assert.Throws<InvalidOperationException>(() => files.Load());
        files.Settings["Hosting:DataProtectionKeysPath"] = files.Root;
        Assert.Throws<InvalidOperationException>(() => HostingProductionConfiguration.Load(files.Configuration, files.WebRoot, initializeDemo: true));
        Assert.Throws<InvalidOperationException>(() => HostingProductionConfiguration.Load(files.Configuration, files.WebRoot, initializeIdentity: true));
    }

    [Theory]
    [InlineData(-3, -1, true, "1.3.6.1.5.5.7.3.1")]
    [InlineData(1, 3, true, "1.3.6.1.5.5.7.3.1")]
    [InlineData(-1, 3, false, "1.3.6.1.5.5.7.3.1")]
    [InlineData(-1, 3, true, "1.3.6.1.5.5.7.3.2")]
    public void Invalid_certificate_is_rejected(int beforeDays, int afterDays, bool privateKey, string usage)
    {
        using var files = new ProductionFiles();
        files.WriteCertificate(beforeDays, afterDays, privateKey, usage);
        Assert.Throws<InvalidOperationException>(() => files.Load());
    }

    [Fact]
    public void Explicit_configuration_uses_safe_defaults_and_readwrite_database()
    {
        using var files = new ProductionFiles();
        var settings = files.Load();
        using var certificate = settings.Certificate;
        Assert.Equal(IPAddress.Loopback, settings.ListenAddress);
        Assert.Equal(5443, settings.HttpsPort);
        Assert.Equal(SqliteOpenMode.ReadWrite, new SqliteConnectionStringBuilder(settings.ConnectionString).Mode);
        Assert.True(new SqliteConnectionStringBuilder(settings.ConnectionString).ForeignKeys);
        Assert.Equal(0, new FileInfo(files.DatabasePath).Length);
    }

    [Fact]
    public async Task Production_keeps_authentication_secure_and_disables_local_integrations()
    {
        using var files = new ProductionFiles();
        files.Settings["WhatsAppGateway:Enabled"] = "true";
        await using var factory = new ProductionFactory(files);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, BaseAddress = new Uri("https://organizers.example.test") });
        var protectedPage = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, protectedPage.StatusCode);
        Assert.Contains("/Access/Login", protectedPage.Headers.Location!.OriginalString);
        var login = await client.GetAsync("/Access/Login");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains(login.Headers.GetValues("Set-Cookie"), value => value.Contains("secure", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("nosniff", login.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", login.Headers.GetValues("X-Frame-Options").Single());
        Assert.True(login.Headers.Contains("Strict-Transport-Security"));
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always,
            factory.Services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.SecurePolicy);
        Assert.Equal(Microsoft.AspNetCore.Http.CookieSecurePolicy.Always,
            factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme).Cookie.SecurePolicy);
        Assert.Equal("TRobot", factory.Services.GetRequiredService<IOptions<DataProtectionOptions>>().Value.ApplicationDiscriminator);
        Assert.False(factory.Services.GetRequiredService<WhatsAppGatewayOptions>().Enabled);
        Assert.DoesNotContain(factory.Services.GetServices<IHostedService>(), service => service is AutomaticSchedulerWorker);
        using var deniedHost = new HttpRequestMessage(HttpMethod.Get, "https://unlisted.example.test/Access/Login");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(deniedHost)).StatusCode);
        Assert.Equal(0, new FileInfo(files.DatabasePath).Length);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Unknown")]
    public void Unsupported_environments_are_rejected(string environment)
    {
        using var files = new ProductionFiles();
        using var factory = new ProductionFactory(files, environment);
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    private sealed class ProductionFactory(ProductionFiles files, string environment = "Production") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment(environment);

        protected override IHost CreateHost(IHostBuilder builder)
        {
            // Minimal-host validation runs before deferred ConfigureAppConfiguration callbacks.
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(files.Settings));
            return base.CreateHost(builder);
        }
    }
    private sealed class ProductionFiles : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "trobot-production-tests-" + Guid.NewGuid().ToString("N"));
        public string WebRoot => Path.Combine(Root, "wwwroot");
        public string DatabasePath => Path.Combine(Root, "trobot.db");
        public Dictionary<string, string?> Settings { get; } = new();
        public IConfiguration Configuration => new ConfigurationBuilder().AddInMemoryCollection(Settings).Build();
        public ProductionFiles()
        {
            Directory.CreateDirectory(WebRoot);
            File.WriteAllBytes(DatabasePath, []);
            Settings["Hosting:Enabled"] = "true";
            Settings["AllowedHosts"] = "organizers.example.test";
            Settings["Hosting:DataProtectionKeysPath"] = Root;
            Settings["Hosting:CertificatePath"] = Path.Combine(Root, "certificate.pfx");
            Settings["Hosting:CertificatePassword"] = "synthetic-test-password";
            Settings["ConnectionStrings:TRobot"] = $"Data Source={DatabasePath};Mode=ReadWrite";
            WriteCertificate(-1, 3, true, "1.3.6.1.5.5.7.3.1");
        }
        public HostingProductionConfiguration Load() => HostingProductionConfiguration.Load(Configuration, WebRoot);
        public void WriteCertificate(int beforeDays, int afterDays, bool privateKey, string usage)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=organizers.example.test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new(usage) }, true));
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(beforeDays), DateTimeOffset.UtcNow.AddDays(afterDays));
            using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
            File.WriteAllBytes(Settings["Hosting:CertificatePath"]!, (privateKey ? certificate : publicOnly)
                .Export(X509ContentType.Pfx, Settings["Hosting:CertificatePassword"]));
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
