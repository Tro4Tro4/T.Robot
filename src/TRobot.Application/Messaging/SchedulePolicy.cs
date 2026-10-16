using TRobot.Domain;

namespace TRobot.Application.Messaging;

public static class SchedulePolicy
{
    public static int? SummaryIntervalDays(DateTime nowUtc, Event ev)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId);
        var days = (TimeZoneInfo.ConvertTimeFromUtc(ev.StartsAtUtc, zone).Date - TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone).Date).Days;
        return days switch { > 30 => 7, >= 15 => 4, >= 7 => 2, >= 1 => 1, _ => null };
    }
    public static bool IsQuiet(DateTime localNow, BotSettings settings)
    {
        var hour = localNow.Hour;
        return settings.QuietStartHour < settings.QuietEndHour
            ? hour >= settings.QuietStartHour && hour < settings.QuietEndHour
            : settings.QuietStartHour > settings.QuietEndHour && (hour >= settings.QuietStartHour || hour < settings.QuietEndHour);
    }
    public static bool CanSend(OutboundMessage message, BotSettings settings, DateTime nowUtc, TimeZoneInfo zone,
        IEnumerable<OutboundMessage> messages)
    {
        if (settings.Paused || message.Status != OutboundMessageStatus.Queued || message.ScheduledAtUtc > nowUtc) return false;
        if (message.Kind == MessageKind.Operational) return true;
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        if (IsQuiet(local, settings)) return false;
        return messages.Count(x => x.Status == OutboundMessageStatus.Sent && x.Kind != MessageKind.Operational && x.SentAtUtc.HasValue
            && TimeZoneInfo.ConvertTimeFromUtc(x.SentAtUtc.Value, zone).Date == local.Date) < settings.MaxSpontaneousPerDay;
    }
}
