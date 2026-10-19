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
        if (await IsAttendanceChildAsync(connection.EventId, participant.PersonId, ct))
        {
            await CloseAttendanceAsync(connection.EventId, participant.PersonId, ct);
            return "👨‍👩‍👧 Per le date dei bambini risponde il genitore collegato al nucleo. Chiedigli di completare !presenza si oppure !presenza modifica.";
        }
        var dialogue = await store.FindAsync<AttendanceDialogue>(connection.EventId, participant.PersonId, ct);
        if (dialogue is null) { dialogue = new(connection.EventId, participant.PersonId); store.Add(dialogue); }
        dialogue.Start(connection.ActorId!.Value, connection.GroupId!, connection.Revision, now);
        var name = (await store.ParticipantsAsync(connection.EventId, ct)).Single(x => x.Id == participant.PersonId).Name;
        return await WithAttendanceDeadlineAsync(connection.EventId, $"🎉 {SafeAttendanceName(name)}, ci sei! Quando arrivi?\nScrivi gg/mm oppure gg/mm/aaaa.\n!annulla interrompe i dettagli.", ct);
    }

    private async Task<string> WithAttendanceDeadlineAsync(Guid eventId, string text, CancellationToken ct)
    {
        var deadline = await AttendanceDeadlineTextAsync(eventId, ct);
        return deadline is null ? text : text + "\n\n" + deadline;
    }

    private async Task<Guid?> AttendanceHouseholdAsync(Guid eventId, Guid personId, CancellationToken ct)
    {
        var members = await store.ListAsync<HouseholdMember>(eventId, ct);
        var participants = await store.ParticipantsAsync(eventId, ct);
        var own = members.Where(x => x.IsActive && x.PersonId == personId).ToArray();
        return own.Length == 1 && own[0].Kind == HouseholdMemberKind.Adult
            && members.Any(x => x.IsActive && x.HouseholdId == own[0].HouseholdId && x.PersonId != personId
                && participants.Any(p => p.Id == x.PersonId)) ? own[0].HouseholdId : null;
    }

    private async Task<string> StartAttendanceEditAsync(WhatsAppConnection connection, EventParticipant participant, DateTime now, CancellationToken ct)
    {
        if (await IsAttendanceChildAsync(connection.EventId, participant.PersonId, ct))
            return "👨‍👩‍👧 Le date dei bambini seguono il genitore collegato al nucleo: la modifica va confermata da lui.";
        if (participant.Status != ParticipationStatus.Confirmed)
            return "ℹ️ Prima conferma la tua presenza con !presenza si. Poi raccogliamo le date!";
        var dialogue = await store.FindAsync<AttendanceDialogue>(connection.EventId, participant.PersonId, ct);
        if (dialogue is null) { dialogue = new(connection.EventId, participant.PersonId); store.Add(dialogue); }
        var stay = await store.FindAsync<AttendanceStay>(connection.EventId, participant.PersonId, ct);
        var household = await AttendanceHouseholdAsync(connection.EventId, participant.PersonId, ct);
        dialogue.Start(connection.ActorId!.Value, connection.GroupId!, connection.Revision, now);
        dialogue.BeginEdit(stay?.Arrival, stay?.Days, household,
            await AttendanceSnapshotAsync(connection.EventId, participant.PersonId, household, ct));
        var text = stay is null ? "🧳 Completiamo il soggiorno! Quando arrivi?\nScrivi gg/mm oppure gg/mm/aaaa."
            : $"✏️ Soggiorno registrato: arrivo {stay.Arrival:dd/MM/yyyy}, {stay.Days} giorni, fino al {stay.LastDay:dd/MM/yyyy}, {stay.Nights} notti.\nCosa vuoi cambiare?\n1 = arrivo\n2 = durata\n3 = entrambi";
        return await WithAttendanceDeadlineAsync(connection.EventId, text + "\n!annulla conserva i dati registrati. Hai 15 minuti.", ct);
    }

    private async Task<bool> IsAttendanceChildAsync(Guid eventId, Guid personId, CancellationToken ct) =>
        (await store.ListAsync<HouseholdMember>(eventId, ct)).Any(x => x.IsActive && x.PersonId == personId && x.Kind == HouseholdMemberKind.Child);

    private async Task<List<Guid>> AttendanceIndividualTargetsAsync(Guid eventId, Guid personId, CancellationToken ct)
    {
        var members = await store.ListAsync<HouseholdMember>(eventId, ct);
        var own = members.Where(x => x.IsActive && x.PersonId == personId).ToArray();
        var targets = new List<Guid> { personId };
        if (own.Length != 1 || own[0].Kind != HouseholdMemberKind.Adult) return targets;
        var participants = (await store.ParticipantsAsync(eventId, ct)).Select(x => x.Id).ToHashSet();
        targets.AddRange(members.Where(x => x.IsActive && x.Kind == HouseholdMemberKind.Child
            && x.HouseholdId == own[0].HouseholdId && x.CompanionOfMemberId == own[0].Id
            && participants.Contains(x.PersonId)).Select(x => x.PersonId).Distinct().Order());
        return targets;
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
        if (await IsAttendanceChildAsync(connection.EventId, participant.PersonId, ct))
        { dialogue.Close(); return "👨‍👩‍👧 Le date dei bambini seguono il genitore collegato al nucleo. Ripartite dal suo !presenza modifica."; }
        if (dialogue.IsEdit && dialogue.InitialSnapshotHash != await AttendanceSnapshotAsync(connection.EventId, participant.PersonId, dialogue.HouseholdId, ct))
        { dialogue.Close(); return "🔄 Presenze, soggiorni o nucleo sono cambiati. Riparti con !presenza modifica per usare i dati aggiornati."; }
        text = text.Trim().ToLowerInvariant();
        var ev = (await store.EventAsync(connection.EventId, ct))!;
        var eventDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(ev.StartsAtUtc, TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId)));
        if (dialogue.Step == AttendanceDialogueStep.EditChoice)
        {
            if (!int.TryParse(text, out var choice) || choice is < 1 or > 3) return "✏️ Scrivi 1 per l'arrivo, 2 per la durata oppure 3 per entrambi.";
            dialogue.ChooseEdit(choice);
            return choice == 2 ? "🧳 Quanti giorni resti? Scrivi da 1 a 31, arrivo e ultimo giorno inclusi."
                : "📅 Quando arrivi? Scrivi gg/mm oppure gg/mm/aaaa.";
        }
        if (dialogue.Step == AttendanceDialogueStep.Arrival)
        {
            var arrival = ParseArrival(text, eventDate);
            if (!arrival.HasValue || Math.Abs(arrival.Value.DayNumber - eventDate.DayNumber) > 31)
                return $"📅 Scrivi una data valida gg/mm o gg/mm/aaaa, dal {eventDate.AddDays(-31):dd/MM/yyyy} al {eventDate.AddDays(31):dd/MM/yyyy}.";
            dialogue.SetArrival(arrival.Value);
            if (dialogue.PreserveDays)
                return await SummarizeAttendanceAsync(connection, participant, dialogue, dialogue.Days!.Value, ct);
            var name = (await store.ParticipantsAsync(connection.EventId, ct)).Single(x => x.Id == participant.PersonId).Name;
            return $"🧳 {SafeAttendanceName(name)}, quanti giorni resti? Scrivi un numero da 1 a 31 (arrivo e ultimo giorno inclusi).";
        }
        if (dialogue.Step == AttendanceDialogueStep.Days)
        {
            if (!int.TryParse(text, out var days) || days is < 1 or > 31) return "🧮 Scrivi i giorni da 1 a 31, contando anche il giorno di arrivo.";
            return await SummarizeAttendanceAsync(connection, participant, dialogue, days, ct);
        }
        if (text is not ("si" or "sì" or "no")) return "✅ Rispondi sì oppure no. !annulla interrompe i dettagli.";
        if (dialogue.SnapshotHash != await AttendanceSnapshotAsync(connection.EventId, participant.PersonId, dialogue.HouseholdId, ct))
        { dialogue.Close(); return "🔄 Presenze, soggiorni o nucleo sono cambiati. Riparti con !presenza si per confermare dati aggiornati."; }
        if (text == "no" && !dialogue.HouseholdId.HasValue)
        { dialogue.Close(); return "👌 Soggiorno non salvato. Per correggere le date riparti con !presenza si."; }
        var targets = await AttendanceIndividualTargetsAsync(connection.EventId, participant.PersonId, ct);
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
        return text != "no" && dialogue.HouseholdId.HasValue ? "🎉 Presenza e date salvate per tutta la famiglia!"
            : targets.Count > 1 ? $"🎉 Soggiorno salvato per te e {Number(targets.Count - 1)} bambini collegati!"
            : "🎉 Soggiorno salvato! Per correggerlo usa !presenza modifica.";
    }

    private async Task<string> SummarizeAttendanceAsync(WhatsAppConnection connection, EventParticipant participant, AttendanceDialogue dialogue, int days, CancellationToken ct)
    {
        var household = dialogue.IsEdit ? dialogue.HouseholdId : await AttendanceHouseholdAsync(connection.EventId, participant.PersonId, ct);
        dialogue.Summarize(days, household, await AttendanceSnapshotAsync(connection.EventId, participant.PersonId, household, ct));
        var summary = $"📅 Dal {dialogue.Arrival:dd/MM/yyyy} al {dialogue.Arrival!.Value.AddDays(days - 1):dd/MM/yyyy}: {days} giorni, {days - 1} notti.\n";
        if (household.HasValue)
        {
            var family = await store.FindAsync<Household>(connection.EventId, household.Value, ct);
            var children = (await AttendanceIndividualTargetsAsync(connection.EventId, participant.PersonId, ct)).Count - 1;
            return summary + $"👨‍👩‍👧 Confermi le stesse date per tutta la famiglia {SafeAttendanceName(family!.Name)}?\nSì per tutti, no "
                + (children == 0 ? "solo per te" : $"per te e {Number(children)} bambini collegati") + ". !annulla per interrompere.";
        }
        return summary + "✅ Confermi il tuo soggiorno? Sì per salvare, no per ricominciare.";
    }

    private async Task<string> AttendanceSnapshotAsync(Guid eventId, Guid personId, Guid? householdId, CancellationToken ct)
    {
        var members = await store.ListAsync<HouseholdMember>(eventId, ct);
        var own = members.Where(x => x.PersonId == personId).OrderBy(x => x.Id).ToArray();
        // A lone adult still belongs to a nucleus: additions/reactivations must invalidate
        // the proposal even when no family-wide confirmation was offered initially.
        var activeOwn = own.Where(x => x.IsActive).ToArray();
        var effectiveHousehold = householdId ?? (activeOwn.Length == 1 ? activeOwn[0].HouseholdId : (Guid?)null);
        var scope = effectiveHousehold.HasValue ? members.Where(x => x.HouseholdId == effectiveHousehold || x.PersonId == personId).OrderBy(x => x.Id).ToArray() : own;
        var family = effectiveHousehold.HasValue ? await store.FindAsync<Household>(eventId, effectiveHousehold.Value, ct) : null;
        var ev = (await store.EventAsync(eventId, ct))!;
        var value = $"event:{ev.Revision};family:{family?.Id}:{family?.Revision};" + string.Join(';', scope.Select(x => $"{x.Id}:{x.PersonId}:{x.HouseholdId}:{x.Revision}:{x.IsActive}:{x.Kind}:{x.CompanionOfMemberId}"));
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
