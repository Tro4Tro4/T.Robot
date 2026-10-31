using TRobot.Application.WhatsApp;

namespace TRobot.Web.Security;

public static class WhatsAppHostingConfiguration
{
    public static WhatsAppGatewayOptions Load(IConfiguration configuration, bool development)
    {
        var enabled = configuration.GetValue("WhatsAppGateway:Enabled", false);
        var url = configuration["WhatsAppGateway:BaseUrl"] ?? "http://127.0.0.1:5240";
        var token = configuration["WhatsAppGateway:Token"] ?? "";
        if (development) return new(enabled, url, token);
        if (!configuration.GetValue("WhatsAppGateway:ProductionEnabled", false))
            return new(false, url, token);
        if (!enabled || configuration["WhatsAppGateway:BaseUrl"] != "http://127.0.0.1:5240"
            || token.Length != 64 || token.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException("T.Robot Production: configurazione WhatsApp esplicita non valida.");
        return new(true, url, token);
    }
}
