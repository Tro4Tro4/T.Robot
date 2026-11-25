using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

// One-off reconstruction of attendance lost with the Azure database (25 November 2026).
// Mirrors OrganizerAttendanceService (status + organizer receipt) and the WhatsApp stay confirmation
// (stay, stay change, audit), attributed to the organizer. Dry run by default; apply requires its fingerprint.
const string AllowedEvent = "fc3dfb11-fbcf-46e9-b677-9521e1c07f03";
if (args.Length is not (3 or 5) || (args.Length == 5 && args[3] != "--apply"))
{
    Console.Error.WriteLine("Uso: TRobot.AttendanceRestore <db> <piano.json> <utente-organizzatore> [--apply <fingerprint>]");
    return 2;
}
try
{
    var path = Path.GetFullPath(args[0]);
    if (!File.Exists(path)) throw new InvalidOperationException("Il DB deve esistere.");
    var entries = JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(args[1]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("Piano vuoto.");
    var connection = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, ForeignKeys = true, Pooling = false }.ToString();
    await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
    var eventId = Guid.Parse(AllowedEvent);
    var ev = await db.Events.SingleAsync(x => x.Id == eventId);
    var eventDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(ev.StartsAtUtc, TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId)));
    var user = await db.Users.SingleOrDefaultAsync(x => x.UserName == args[2]) ?? throw new InvalidOperationException("Organizzatore non trovato.");
    var organizer = user.PersonId;
    if (!await new AttendanceStore(db).IsEnabledOrganizerAsync(eventId, organizer, default))
        throw new InvalidOperationException("L'utente non è un organizzatore abilitato dell'evento.");

    var participants = await db.EventParticipants.Where(x => x.EventId == eventId).ToListAsync();
    var people = await db.People.Where(p => participants.Select(x => x.PersonId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.DisplayName);
    var members = await db.Set<HouseholdMember>().Where(x => x.EventId == eventId && x.IsActive).ToListAsync();
    var stays = await db.Set<AttendanceStay>().Where(x => x.EventId == eventId).ToDictionaryAsync(x => x.Id);

    var plan = new List<PlanItem>();
    foreach (var entry in entries)
    {
        var matches = people.Where(x => x.Value == entry.Name).Select(x => x.Key).ToList();
        if (matches.Count != 1) throw new InvalidOperationException($"\"{entry.Name}\": partecipante non trovato o nome ambiguo.");
        var person = matches[0];
        if (!Enum.TryParse<ParticipationStatus>(entry.Status, out var status) || !Enum.IsDefined(status))
            throw new InvalidOperationException($"{entry.Name}: stato non valido.");
        var own = members.Where(x => x.PersonId == person).ToArray();
        if (own.Length == 1 && own[0].Kind == HouseholdMemberKind.Child)
            throw new InvalidOperationException($"{entry.Name}: i bambini seguono il genitore collegato al nucleo.");
        DateOnly? arrival = null;
        if (status == ParticipationStatus.Confirmed)
        {
            if (entry.Arrival is null || entry.Days is not (>= 1 and <= 31)) throw new InvalidOperationException($"{entry.Name}: per i confermati servono arrivo e giorni (1-31).");
            arrival = DateOnly.ParseExact(entry.Arrival, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (Math.Abs(arrival.Value.DayNumber - eventDate.DayNumber) > 31) throw new InvalidOperationException($"{entry.Name}: arrivo fuori dal periodo ammesso.");
        }
        else if (entry.Arrival is not null || entry.Days is not null)
            throw new InvalidOperationException($"{entry.Name}: date ammesse solo per i confermati.");

        // Same target rules as the WhatsApp stay confirmation.
        var targets = new List<Guid> { person };
        if (own.Length == 1 && own[0].Kind == HouseholdMemberKind.Adult)
        {
            var ids = participants.Select(x => x.PersonId).ToHashSet();
            // Organizer rule (25 November 2026): whoever has a household answers for the whole household.
            targets = members.Where(x => x.HouseholdId == own[0].HouseholdId && ids.Contains(x.PersonId)).Select(x => x.PersonId).Distinct().Order().ToList();
        }

        foreach (var target in targets)
        {
            if (plan.Any(x => x.PersonId == target)) throw new InvalidOperationException($"{people[target]}: indicato più volte nel piano.");
            var participant = participants.Single(x => x.PersonId == target);
            stays.TryGetValue(target, out var stay);
            plan.Add(new(target, people[target], entry.Name, participant.Status.ToString(), participant.UpdatedAtUtc?.Ticks,
                stay?.Arrival.ToString("yyyy-MM-dd"), stay?.Days, stay?.Revision, status.ToString(), arrival?.ToString("yyyy-MM-dd"), entry.Days));
        }
    }
    var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(plan)))).ToLowerInvariant();
    foreach (var item in plan)
    {
        var before = item.PreviousStatus + (item.PreviousArrival is null ? "" : $" {item.PreviousArrival} x{item.PreviousDays}");
        var after = item.Status + (item.Arrival is null ? "" : $" dal {DateOnly.Parse(item.Arrival, CultureInfo.InvariantCulture):dd/MM/yyyy}, {item.Days} giorni fino al {DateOnly.Parse(item.Arrival, CultureInfo.InvariantCulture).AddDays(item.Days!.Value - 1):dd/MM/yyyy}");
        Console.WriteLine($"{item.Name,-14} {before,-28} -> {after}{(item.Name == item.Source ? "" : $"   (da {item.Source})")}");
    }
    Console.WriteLine($"Persone: {plan.Count}. Fingerprint: {fingerprint}");
    if (args.Length == 3) { Console.WriteLine("Anteprima: nessuna modifica salvata."); return 0; }
    if (args[4] != fingerprint) throw new InvalidOperationException("Fingerprint diverso: dati o piano cambiati dopo l'anteprima.");

    await using var transaction = await db.Database.BeginTransactionAsync();
    var now = DateTime.UtcNow;
    foreach (var item in plan)
    {
        var participant = participants.Single(x => x.PersonId == item.PersonId);
        var previousStatus = participant.Status;
        var status = Enum.Parse<ParticipationStatus>(item.Status);
        // OrganizerAttendanceService.SetAsync
        participant.ChangeStatus(status, now);
        db.Add(new ParticipationReceipt(eventId, item.PersonId, organizer, "restore-" + Guid.NewGuid().ToString("N"),
            previousStatus, status, now, AttendanceOrigin.Organizer));
        if (item.Arrival is null) continue;
        // WhatsAppAttendanceService stay confirmation, sender and actor = organizer
        stays.TryGetValue(item.PersonId, out var stay);
        var arrival = DateOnly.ParseExact(item.Arrival, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var change = new AttendanceStayChange(eventId, item.PersonId, organizer, organizer, stay?.Arrival, stay?.Days, previousStatus, arrival, item.Days!.Value, now);
        if (stay is null) { stay = new(eventId, item.PersonId); db.Add(stay); }
        stay.Set(arrival, item.Days.Value);
        db.Add(change);
        db.Add(new LocalAudit(eventId, organizer, change.Id, "Attendance", "StayRestored", now));
    }
    await db.SaveChangesAsync();
    await transaction.CommitAsync();
    Console.WriteLine($"Applicato: {plan.Count} persone aggiornate.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Ricostruzione interrotta: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

sealed record Entry(string Name, string Status, string? Arrival, int? Days);
sealed record PlanItem(Guid PersonId, string Name, string Source, string PreviousStatus, long? PreviousUpdatedTicks,
    string? PreviousArrival, int? PreviousDays, int? PreviousStayRevision, string Status, string? Arrival, int? Days);
