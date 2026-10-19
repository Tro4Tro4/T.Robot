using System.Globalization;
using TRobot.Application.Local;
using TRobot.Application.Simulation;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed partial class WhatsAppService
{
    private async Task<string> AttendanceSummaryReplyAsync(Guid eventId, int page, CancellationToken ct, DateOnly? day = null)
    {
        // Event-scoped participation is the source of truth: a sender's confirmation never
        // implies confirmation of their family, and an alias is not needed to be counted.
        var people = (await store.ParticipationsAsync(eventId, ct)).DistinctBy(x => x.PersonId).ToArray();
        var confirmed = people.Where(x => x.Status == ParticipationStatus.Confirmed).Select(x => x.PersonId).ToHashSet();
        var stays = (await store.ListAsync<AttendanceStay>(eventId, ct)).ToDictionary(x => x.Id);
        var missingStays = confirmed.Count(x => !stays.ContainsKey(x));
        if (day.HasValue)
            confirmed.RemoveWhere(x => !stays.TryGetValue(x, out var stay) || stay.Arrival > day.Value || stay.LastDay < day.Value);
        var members = (await store.ListAsync<HouseholdMember>(eventId, ct))
            .Where(x => x.IsActive && confirmed.Contains(x.PersonId)).ToArray();
        var adults = members.Count(x => x.Kind == HouseholdMemberKind.Adult);
        var children = members.Count(x => x.Kind == HouseholdMemberKind.Child);
        var unknown = confirmed.Count - members.Length;
        var summaryCommand = day.HasValue ? "!riepilogo " + day.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "!riepilogo";
        var blocks = (await store.ListAsync<Household>(eventId, ct))
            .OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Id)
            .Select(h => (Household: h, Members: members.Where(x => x.HouseholdId == h.Id).ToArray()))
            .Where(x => x.Members.Length > 0)
            .Select(x => $"🏡 *{OperationalListText.PublicText(x.Household.Name)}*\n"
                + $"{Number(x.Members.Length)} confermati · {Number(x.Members.Count(m => m.Kind == HouseholdMemberKind.Adult))} adulti · {Number(x.Members.Count(m => m.Kind == HouseholdMemberKind.Child))} bambini")
            .ToArray();
        var pages = Math.Max(1, (blocks.Length + 4) / 5);
        if (page > pages) return $"ℹ️ Pagina inesistente.\nDisponibili {Number(pages)} pagine. Riparti da:\n{summaryCommand} 1";
        var dateHeading = day.HasValue ? " · " + day.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "";
        var reply = $"🎉 *PRESENZE{dateHeading} · Pagina {Number(page)}/{Number(pages)}*\n\n"
            + $"✅ Confermati: {Number(confirmed.Count)}\n👥 Adulti: {Number(adults)} · Bambini: {Number(children)}\n"
            + $"Fuori nucleo attivo / età non specificata: {Number(unknown)}\n"
            + $"🤔 Indecisi: {Number(people.Count(x => x.Status == ParticipationStatus.Undecided))} · Assenti: {Number(people.Count(x => x.Status == ParticipationStatus.Absent))} · Senza risposta: {Number(people.Count(x => x.Status == ParticipationStatus.NoResponse))}\n"
            + $"🧳 Confermati con soggiorno da completare: {Number(missingStays)}" + (day.HasValue ? " (esclusi dal giorno)" : "") + "\n\n"
            + (blocks.Length == 0 ? "Nessun nucleo con presenze confermate per ora." : string.Join("\n\n", blocks.Skip((page - 1) * 5).Take(5)))
            + (page < pages ? $"\n\n➡️ Pagina successiva:\n{summaryCommand} {Number(page + 1)}" : "")
            + "\n\nIo conto le presenze, voi contate i dolci. 🍰";
        if (reply.Length > 2000) throw new LocalValidationException("Riepilogo troppo lungo per la risposta WhatsApp.");
        return reply;
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
