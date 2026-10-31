using Microsoft.Extensions.Configuration;
using TRobot.Web.Security;

namespace TRobot.Tests;

public sealed class WhatsAppProductionConfigurationTests
{
    private readonly Dictionary<string, string?> settings = new()
    {
        ["WhatsAppGateway:Enabled"] = "true",
        ["WhatsAppGateway:BaseUrl"] = "http://127.0.0.1:5240",
        ["WhatsAppGateway:Token"] = new string('a', 64)
    };
    private IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Fact]
    public void Production_is_disabled_until_explicitly_opted_in()
    {
        Assert.False(WhatsAppHostingConfiguration.Load(Configuration(), false).Enabled);
        settings["WhatsAppGateway:ProductionEnabled"] = "false";
        Assert.False(WhatsAppHostingConfiguration.Load(Configuration(), false).Enabled);
    }

    [Fact]
    public void Production_opt_in_preserves_existing_token_and_configuration()
    {
        settings["WhatsAppGateway:ProductionEnabled"] = "true";
        var configuration = Configuration();
        var options = WhatsAppHostingConfiguration.Load(configuration, false);
        Assert.True(options.Enabled);
        Assert.Equal(settings["WhatsAppGateway:Token"], options.Token);
        Assert.Equal("http://127.0.0.1:5240", configuration["WhatsAppGateway:BaseUrl"]);
    }

    [Theory]
    [InlineData("Enabled", "false")]
    [InlineData("Enabled", null)]
    [InlineData("BaseUrl", null)]
    [InlineData("BaseUrl", "http://localhost:5240")]
    [InlineData("BaseUrl", "http://user@127.0.0.1:5240")]
    [InlineData("BaseUrl", "http://127.0.0.1:5240/")]
    [InlineData("BaseUrl", "http://127.0.0.1:5240?x=1")]
    [InlineData("BaseUrl", "http://127.0.0.1:5240#x")]
    [InlineData("Token", "")]
    [InlineData("Token", "gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public void Invalid_production_opt_in_is_rejected(string key, string? value)
    {
        settings["WhatsAppGateway:ProductionEnabled"] = "true";
        settings["WhatsAppGateway:" + key] = value;
        Assert.Throws<InvalidOperationException>(() => WhatsAppHostingConfiguration.Load(Configuration(), false));
    }

    [Fact]
    public void Development_uses_Enabled_independently_of_production_opt_in()
    {
        settings["WhatsAppGateway:ProductionEnabled"] = "false";
        Assert.True(WhatsAppHostingConfiguration.Load(Configuration(), true).Enabled);
        settings["WhatsAppGateway:Enabled"] = "false";
        settings["WhatsAppGateway:ProductionEnabled"] = "true";
        Assert.False(WhatsAppHostingConfiguration.Load(Configuration(), true).Enabled);
    }
}
