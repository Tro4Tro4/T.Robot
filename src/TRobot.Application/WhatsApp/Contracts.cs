using System.Security.Cryptography;
using System.Text;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed class WhatsAppGatewayOptions(bool enabled, string baseUrl, string token)
{
    public bool Enabled { get; } = enabled;
    public string BaseUrl { get; } = baseUrl;
    public string Token { get; } = token;
    public void RequireEnabled()
    {
        if (!Enabled) throw new LocalValidationException("Gateway WhatsApp disattivato in questa istanza.");
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var url) || url.Scheme != "http" || !url.IsLoopback || url.Port != 5240
            || !string.IsNullOrEmpty(url.UserInfo) || url.AbsolutePath != "/" || !string.IsNullOrEmpty(url.Query)
            || Token.Length != 64 || !Token.All(Uri.IsHexDigit))
            throw new LocalValidationException("Configurazione locale del gateway non valida.");
    }
    public string Fingerprint(string kind, string value)
    { RequireEnabled(); return Convert.ToHexStringLower(HMACSHA256.HashData(Convert.FromHexString(Token), Encoding.UTF8.GetBytes($"TRobot:whatsapp:v1:{kind}:{value}"))); }
}

public sealed record GatewayHealth(string Status, string? QrDataUrl = null, string? ActiveGroupId = null);
public sealed record GatewayGroup(string Id, string Name);
public sealed record GatewayMember(string Id, IReadOnlyList<string> AlternateIds, string Label, bool IsAssociated = false);
public sealed record GatewaySendResult(string Status, string? MessageId = null);
public sealed record WhatsAppIncoming(string GroupId, IReadOnlyList<string> SenderIds, string MessageId, string Text, long TimestampUnixSeconds, bool IsPrivate = false);
public sealed record WhatsAppIncomingResult(bool Handled, bool Duplicate = false, string? Reaction = null,
    string? ReplyText = null, Guid? ReplyOperationId = null, bool ReplyPrivately = false);
public sealed record WhatsAppListPreview(Guid EventId, WhatsAppListKind Kind, string GroupId, int ConnectionRevision,
    string GroupName, IReadOnlyList<string> Pages, string Fingerprint, bool Approved);
public sealed record WhatsAppPage(Event Event, WhatsAppConnection? Connection, IReadOnlyList<LocalPerson> Participants,
    IReadOnlyList<WhatsAppIdentityAlias> Aliases, IReadOnlyList<WhatsAppLiveMessage> Messages, bool CanManageDevice, bool GatewayEnabled);
public sealed record WhatsAppMessagePreview(string EventName, WhatsAppLiveMessage Message, string? GroupName,
    bool RecipientVerified, string RecipientStatus)
{
    public bool CanSend => RecipientVerified && Message.Status == WhatsAppLiveMessageStatus.Draft;
}

public interface IWhatsAppGateway
{
    Task<GatewayHealth> HealthAsync(CancellationToken ct);
    Task<GatewayHealth> ConnectAsync(CancellationToken ct);
    Task<GatewayHealth> DisconnectAsync(CancellationToken ct);
    Task<IReadOnlyList<GatewayGroup>> GroupsAsync(CancellationToken ct);
    Task<IReadOnlyList<GatewayMember>> MembersAsync(string groupId, CancellationToken ct);
    Task SetGroupAsync(string? groupId, CancellationToken ct);
    Task<GatewaySendResult> SendAsync(Guid operationId, string groupId, string text, CancellationToken ct);
}
public interface IWhatsAppStore
{
    Task<bool> IsAdministratorAsync(Guid personId, CancellationToken ct);
    Task<WhatsAppConnection?> ActiveConnectionAsync(CancellationToken ct);
    Task<EventParticipant?> ParticipantAsync(Guid eventId, Guid personId, CancellationToken ct);
    Task<WhatsAppIncomingReceipt?> ReceiptAsync(Guid eventId, string messageHash, CancellationToken ct);
}
