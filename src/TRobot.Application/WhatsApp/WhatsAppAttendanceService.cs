using System.Text.RegularExpressions;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed partial class WhatsAppService
{
    private static bool AttendanceCandidate(string text) => Regex.IsMatch(text.Trim(), @"^(?:\d{1,2}[/\.\-]\d{1,2}(?:[/\.\-]\d{4})?|\d{1,3}|s[iì]|no)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static string SafeAttendanceName(string text) => new(text.Where(c => !char.IsControl(c)
        && char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.Format && c is not '*' and not '_' and not '~' and not '`' and not '@').Take(80).ToArray());

    private async Task<string> StartAttendanceAsync(WhatsAppConnection connection, EventParticipant participant, DateTime now, CancellationToken ct)
    {
        var dialogue = await store.FindAsync<AttendanceDialogue>(connection.EventId, participant.PersonId, ct);
        if (dialogue is null) { dialogue = new(connection.EventId, participant.PersonId); store.Add(dialogue); }
        dialogue.Start(connection.ActorId!.Value, connection.GroupId!, connection.Revision, now);
        var name = (await store.ParticipantsAsync(connection.EventId, ct)).Single(x => x.Id == participant.PersonId).Name;
        return $"🎉 {SafeAttendanceName(name)}, ci sei! Quando arrivi?\nScrivi gg/mm oppure gg/mm/aaaa.\n!annulla interrompe i dettagli.";
    }

    private async Task<bool> CloseAttendanceAsync(Guid eventId, Guid personId, CancellationToken ct)
    {
        var dialogue = await store.FindAsync<AttendanceDialogue>(eventId, personId, ct);
        if (dialogue is null || dialogue.Step == AttendanceDialogueStep.Closed) return false;
        dialogue.Close(); return true;
    }

    // Runs within IncomingAsync's authorized, serializable SQLite transaction; no external effects here.
    private async Task<string?> AttendanceReplyAsync(WhatsAppConnection connection, EventParticipant participant, string text, DateTime now, DateTime messageUtc, CancellationToken ct)
    {
        var dialogue = await store.FindAsync<AttendanceDialogue>(connection.EventId, participant.PersonId, ct);
        if (dialogue is null || dialogue.Step == AttendanceDialogueStep.Closed) return null;
        // The transport carries whole seconds; allow messages in the starting second,
        // but never apply an older, previously ignored reply to a newly started dialogue.
        if (messageUtc.Ticks < dialogue.StartedAtUtc.Ticks - dialogue.StartedAtUtc.Ticks % TimeSpan.TicksPerSecond) return null;
        if (dialogue.ActorId != connection.ActorId || dialogue.GroupId != connection.GroupId || dialogue.ConnectionRevision != connection.Revision
            || now < dialogue.StartedAtUtc || now >= dialogue.ExpiresAtUtc)
        { dialogue.Close(); return "⌛ Dettagli scaduti o collegamento cambiato. Riparti con !presenza si."; }
        if (text.Equals("!annulla", StringComparison.OrdinalIgnoreCase))
        { dialogue.Close(); return "👌 Raccolta dettagli interrotta. La presenza e i soggiorni già registrati restano salvati."; }
        text = text.Trim().ToLowerInvariant();
        var ev = (await store.EventAsync(connection.EventId, ct))!;
        var eventDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(ev.StartsAtUtc, TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId)));
        if (dialogue.Step == AttendanceDialogueStep.Arrival)
        {
            var arrival = ParseArrival(text, eventDate);
            if (!arrival.HasValue || Math.Abs(arrival.Value.DayNumber - eventDate.DayNumber) > 31)
                return $"📅 Scrivi una data valida gg/mm o gg/mm/aaaa, dal {eventDate.AddDays(-31):dd/MM/yyyy} al {eventDate.AddDays(31):dd/MM/yyyy}.";
            dialogue.SetArrival(arrival.Value);
            var name = (await store.ParticipantsAsync(connection.EventId, ct)).Single(x => x.Id == participant.PersonId).Name;
            return $"🧳 {SafeAttendanceName(name)}, quanti giorni resti? Scrivi un numero da 1 a 31 (arrivo e ultimo giorno inclusi).";
        }
        if (dialogue.Step == AttendanceDialogueStep.Days)
        {
            if (!int.TryParse(text, out var days) || days is < 1 or > 31) return "🧮 Scrivi i giorni da 1 a 31, contando anche il giorno di arrivo.";
            var members = await store.ListAsync<HouseholdMember>(connection.EventId, ct);
            var participants = await store.ParticipantsAsync(connection.EventId, ct);
            var own = members.Where(x => x.IsActive && x.PersonId == participant.PersonId).ToArray();
            Guid? household = own.Length == 1 && own[0].Kind == HouseholdMemberKind.Adult
                && members.Any(x => x.IsActive && x.HouseholdId == own[0].HouseholdId && x.PersonId != participant.PersonId
                    && participants.Any(p => p.Id == x.PersonId)) ? own[0].HouseholdId : null;
            dialogue.Summarize(days, household, await AttendanceSnapshotAsync(connection.EventId, participant.PersonId, household, ct));
            var summary = $"📅 Dal {dialogue.Arrival:dd/MM/yyyy} al {dialogue.Arrival!.Value.AddDays(days - 1):dd/MM/yyyy}: {days} giorni, {days - 1} notti.\n";
            if (household.HasValue)
            {
                var family = await store.FindAsync<Household>(connection.EventId, household.Value, ct);
                return summary + $"👨‍👩‍👧 Confermi le stesse date per tutta la famiglia {SafeAttendanceName(family!.Name)}?\nSì per tutti, no solo per te. !annulla per interrompere.";
            }
            return summary + "✅ Confermi il tuo soggiorno? Sì per salvare, no per ricominciare.";
        }
        if (text is not ("si" or "sì" or "no")) return "✅ Rispondi sì oppure no. !annulla interrompe i dettagli.";
        if (dialogue.SnapshotHash != await AttendanceSnapshotAsync(connection.EventId, participant.PersonId, dialogue.HouseholdId, ct))
        { dialogue.Close(); return "🔄 Presenze, soggiorni o nucleo sono cambiati. Riparti con !presenza si per confermare dati aggiornati."; }
        if (text == "no" && !dialogue.HouseholdId.HasValue)
        { dialogue.Close(); return "👌 Soggiorno non salvato. Per correggere le date riparti con !presenza si."; }
        var targets = new List<Guid> { participant.PersonId };
        if (text != "no" && dialogue.HouseholdId.HasValue)
        {
            var participants = await store.ParticipantsAsync(connection.EventId, ct);
            targets = (await store.ListAsync<HouseholdMember>(connection.EventId, ct))
                .Where(x => x.IsActive && x.HouseholdId == dialogue.HouseholdId && participants.Any(p => p.Id == x.PersonId)).Select(x => x.PersonId).Distinct().ToList();
        }
        foreach (var person in targets)
        {
            var target = (await identities.ParticipantAsync(connection.EventId, person, ct))!;
            var stay = await store.FindAsync<AttendanceStay>(connection.EventId, person, ct);
            var change = new AttendanceStayChange(connection.EventId, person, participant.PersonId, connection.ActorId!.Value,
                stay?.Arrival, stay?.Days, target.Status, dialogue.Arrival!.Value, dialogue.Days!.Value, now);
            if (stay is null) { stay = new(connection.EventId, person); store.Add(stay); }
            stay.Set(dialogue.Arrival.Value, dialogue.Days.Value); target.ChangeStatus(ParticipationStatus.Confirmed, now);
            store.Add(change); operations.Audit(connection.EventId, connection.ActorId.Value, change.Id, "Attendance", "StayConfirmed");
        }
        dialogue.Close();
        return targets.Count > 1 ? "🎉 Presenza e date salvate per tutta la famiglia!" : "🎉 Soggiorno salvato! Per correggerlo riparti con !presenza si.";
    }

    private async Task<string> AttendanceSnapshotAsync(Guid eventId, Guid personId, Guid? householdId, CancellationToken ct)
    {
        var members = await store.ListAsync<HouseholdMember>(eventId, ct);
        var own = members.Where(x => x.PersonId == personId).OrderBy(x => x.Id).ToArray();
        var scope = householdId.HasValue ? members.Where(x => x.HouseholdId == householdId || x.PersonId == personId).OrderBy(x => x.Id).ToArray() : own;
        var family = householdId.HasValue ? await store.FindAsync<Household>(eventId, householdId.Value, ct) : null;
        var ev = (await store.EventAsync(eventId, ct))!;
        var value = $"event:{ev.Revision};family:{family?.Id}:{family?.Revision};" + string.Join(';', scope.Select(x => $"{x.Id}:{x.PersonId}:{x.HouseholdId}:{x.Revision}:{x.IsActive}:{x.Kind}"));
        var people = await store.ParticipantsAsync(eventId, ct);
        foreach (var person in scope.Select(x => x.PersonId).Append(personId).Distinct().Order())
        {
            var participant = await identities.ParticipantAsync(eventId, person, ct);
            var stay = await store.FindAsync<AttendanceStay>(eventId, person, ct);
            value += $";p:{person}:{people.Any(x => x.Id == person)}:{participant?.Status}:{participant?.UpdatedAtUtc?.Ticks}:s:{stay?.Revision}";
        }
        return options.Fingerprint("attendance-snapshot", value);
    }

    private static DateOnly? ParseArrival(string text, DateOnly eventDate)
    {
        var parts = text.Split('/', '.', '-');
        if (parts.Length is not (2 or 3) || !int.TryParse(parts[0], out var day) || !int.TryParse(parts[1], out var month)) return null;
        var candidates = new List<DateOnly>();
        var years = parts.Length == 3 && int.TryParse(parts[2], out var year) ? new[] { year } : parts.Length == 2 ? new[] { eventDate.Year - 1, eventDate.Year, eventDate.Year + 1 } : [];
        foreach (var y in years) { try { candidates.Add(new(y, month, day)); } catch (ArgumentOutOfRangeException) { } }
        return candidates.OrderBy(x => Math.Abs(x.DayNumber - eventDate.DayNumber)).ThenBy(x => x).Cast<DateOnly?>().FirstOrDefault();
    }
}
