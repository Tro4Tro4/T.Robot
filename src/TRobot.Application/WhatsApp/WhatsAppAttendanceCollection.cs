using System.Globalization;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed record AttendanceCollectionInfo(string TimeZoneId, DateTime? DeadlineLocal, bool Expired, int Revision);

public sealed partial class WhatsAppService
{
    private const string AttendanceReminderBody = "📣 Piccolo appello per chi deve ancora rispondere o completare le date!\n\n"
        + "Se ci sei, scrivi !presenza si e segui le domande su arrivo e giorni.\n"
        + "Se sei già confermato e vuoi sistemare le date, scrivi !presenza modifica.\n\n"
        + "🧳 Anche le valigie vogliono sapere quando partire. Grazie!";

    public Task SetAttendanceDeadlineAsync(Guid actorId, Guid eventId, DateTime? deadlineLocal, int revision,
        bool confirmed, CancellationToken ct = default) => operations.ChangeAsync(actorId, eventId, async token =>
    {
        Confirm(confirmed);
        var settings = await store.FindAsync<AttendanceCollectionSettings>(eventId, eventId, token);
        var fresh = settings is null; settings ??= new(eventId);
        LocalOperations.Revision(settings, revision);
        var deadline = await operations.ToUtcAsync(eventId, deadlineLocal, token);
        if (settings.SetDeadline(deadline))
        {
            if (fresh) store.Add(settings);
            operations.Audit(eventId, actorId, eventId, "Attendance", "CollectionDeadlineChanged");
            await store.SaveAsync(token);
        }
        return true;
    }, ct);

    private async Task<AttendanceCollectionInfo> AttendanceCollectionInfoAsync(Guid eventId, CancellationToken ct)
    {
        var ev = await store.EventAsync(eventId, ct) ?? throw new LocalAccessException();
        var settings = await store.FindAsync<AttendanceCollectionSettings>(eventId, eventId, ct);
        var deadline = settings?.DeadlineUtc;
        return new(ev.TimeZoneId, deadline.HasValue ? TimeZoneInfo.ConvertTimeFromUtc(deadline.Value,
            TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId)) : null,
            deadline.HasValue && deadline.Value <= clock.GetUtcNow().UtcDateTime, settings?.Revision ?? 0);
    }

    internal async Task<string?> AttendanceDeadlineTextAsync(Guid eventId, CancellationToken ct)
    {
        var info = await AttendanceCollectionInfoAsync(eventId, ct);
        return info.DeadlineLocal is { } date
            ? $"⏰ Termine adesioni: {date.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} ({info.TimeZoneId}){(info.Expired ? ", scaduto" : ", ancora aperto")}. Risposte e correzioni tardive sono comunque ammesse."
            : null;
    }

    public async Task<Guid> DraftAttendanceReminderAsync(Guid actorId, Guid eventId, Guid operationId, CancellationToken ct = default)
    {
        await operations.AuthorizeAsync(actorId, eventId, ct);
        var people = await store.ParticipationsAsync(eventId, ct);
        var stays = await store.ListAsync<AttendanceStay>(eventId, ct);
        if (!people.Any(p => p.Status is ParticipationStatus.NoResponse or ParticipationStatus.Undecided
            || p.Status == ParticipationStatus.Confirmed && !stays.Any(s => s.Id == p.PersonId)))
            throw new LocalValidationException("Non ci sono risposte o soggiorni da completare.");
        return await DraftAsync(actorId, eventId, operationId, AttendanceReminderBody, ct);
    }
}
