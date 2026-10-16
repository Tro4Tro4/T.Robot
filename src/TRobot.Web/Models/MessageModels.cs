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
public static class MessageLabels
{
    public static string Kind(MessageKind value) => value switch { MessageKind.Operational => "Operativo", MessageKind.Summary => "Riepilogo", MessageKind.Entertainment => "Intrattenimento", _ => "Non valido" };
    public static string Status(OutboundMessageStatus value) => value switch { OutboundMessageStatus.Draft => "Bozza", OutboundMessageStatus.Queued => "In coda", OutboundMessageStatus.Sent => "Inviato (simulato)", OutboundMessageStatus.Cancelled => "Annullato", _ => "Non valido" };
}
