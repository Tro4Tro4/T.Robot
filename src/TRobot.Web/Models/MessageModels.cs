using System.ComponentModel.DataAnnotations;
using TRobot.Application.Messaging;
using TRobot.Domain;

namespace TRobot.Web.Models;

public sealed class MessageInput
{
    public Guid EventId { get; set; }
    public Guid Id { get; set; }
    public Guid OperationId { get; set; }
    public int Revision { get; set; }
    [Required, Display(Name = "Testo per il gruppo")] public string Body { get; set; } = "";
    [Display(Name = "Categoria")] public MessageKind Kind { get; set; }
    [Display(Name = "Programmazione locale (vuoto: immediata)")] public DateTime? ScheduledLocal { get; set; }
    public MessageEdit ToEdit() => new(OperationId, Body, Kind, ScheduledLocal, Revision);
}
public sealed record MessagePage(MessageInput Input, string EventName, string TimeZoneId);
public sealed class MessageTransitionInput
{
    public Guid EventId { get; set; }
    public Guid Id { get; set; }
    public int Revision { get; set; }
    public bool Confirmed { get; set; }
    public bool Cancel { get; set; }
}
public sealed class MessageResolveInput
{
    public Guid EventId { get; set; }
    public Guid Id { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] public int Revision { get; set; }
    public bool AsSent { get; set; }
    [Required, StringLength(500, MinimumLength = 3)] public string Note { get; set; } = "";
    public bool Confirmed { get; set; }
}
public sealed class MessageSettingsInput
{
    public Guid EventId { get; set; }
    public int Revision { get; set; }
    public bool Paused { get; set; }
    [Range(0,23)] public int QuietStart { get; set; }
    [Range(0,23)] public int QuietEnd { get; set; }
    [Range(0,3)] public int Cap { get; set; }
    [Range(0,10)] public int Sarcasm { get; set; }
    public SettingsEdit ToEdit() => new(Paused, QuietStart, QuietEnd, Cap, Sarcasm, Revision);
}
public sealed class MessageAutomationInput
{
    public Guid EventId { get; set; }
    public bool Enabled { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] public int Revision { get; set; }
    public bool Confirmed { get; set; }
}
public static class MessageLabels
{
    public static string Kind(MessageKind value) => value switch { MessageKind.Operational => "Operativo", MessageKind.Summary => "Riepilogo", MessageKind.Entertainment => "Intrattenimento", _ => "Non valido" };
    public static string Delivery(MessageDeliveryMode value) => value switch
    { MessageDeliveryMode.WhatsApp => "WhatsApp", MessageDeliveryMode.Simulated => "Simulazione", _ => "Non valido" };
    /// <summary>Labels never present a simulated outcome as a WhatsApp delivery.</summary>
    public static string Status(OutboundMessageStatus value, MessageDeliveryMode mode) => (value, mode) switch
    {
        (OutboundMessageStatus.Draft, _) => "Bozza",
        (OutboundMessageStatus.Queued, MessageDeliveryMode.WhatsApp) => "Confermato, in attesa",
        (OutboundMessageStatus.Queued, _) => "Coda storica simulata (esclusa dall'invio)",
        (OutboundMessageStatus.Sending, _) => "Invio in corso",
        (OutboundMessageStatus.Sent, MessageDeliveryMode.WhatsApp) => "Inviato",
        (OutboundMessageStatus.Sent, _) => "Simulato (nessun invio WhatsApp)",
        (OutboundMessageStatus.Failed, _) => "Fallito",
        (OutboundMessageStatus.Uncertain, _) => "Esito incerto",
        (OutboundMessageStatus.Cancelled, _) => "Annullato",
        _ => "Non valido"
    };
    public static string Local(DateTime? utc, TimeZoneInfo zone) => utc.HasValue
        ? TimeZoneInfo.ConvertTimeFromUtc(utc.Value, zone).ToString("yyyy-MM-dd HH:mm:ss")
        : "-";
    public static string Group(string? groupId) => string.IsNullOrEmpty(groupId)
        ? "-"
        : groupId.Length > 12 ? "…" + groupId[^12..] : groupId;
}
