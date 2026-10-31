using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed partial class WhatsAppService
{
    private async Task<WhatsAppIncomingResult> MealIncomingAsync(WhatsAppIncoming input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.GroupId) || input.GroupId.Length > 200 || !input.GroupId.EndsWith("@g.us", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(input.MessageId) || input.MessageId.Length > 200
            || string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > (input.IsPrivate ? 300 : 100)) return new(false);
        string[] hashes;
        try { hashes = HashAliases(input.SenderIds); } catch (LocalValidationException) { return new(false); }
        DateTime timestamp;
        try { timestamp = DateTimeOffset.FromUnixTimeSeconds(input.TimestampUnixSeconds).UtcDateTime; }
        catch (ArgumentOutOfRangeException) { return new(false); }
        var active = await identities.ActiveConnectionAsync(ct);
        if (active?.ActorId is not { } actor || active.GroupId != input.GroupId) return new(false);
        return await operations.ChangeAsync(actor, active.EventId, async token =>
        {
            await RequireAdmin(actor, active.EventId, token);
            var connection = await identities.ActiveConnectionAsync(token);
            var now = clock.GetUtcNow().UtcDateTime;
            if (connection is null || !connection.Enabled || connection.EventId != active.EventId || connection.ActorId != actor
                || connection.GroupId != input.GroupId || !connection.ActivatedAtUtc.HasValue || timestamp < connection.ActivatedAtUtc.Value
                || timestamp < now.AddSeconds(-120) || timestamp > now.AddSeconds(30)) return new WhatsAppIncomingResult(false);
            var aliases = await store.ListAsync<WhatsAppIdentityAlias>(connection.EventId, token);
            var matches = aliases.Where(x => x.IsActive && hashes.Contains(x.SenderHash)).Select(x => x.PersonId).Distinct().ToArray();
            if (matches.Length != 1 || await identities.ParticipantAsync(connection.EventId, matches[0], token) is not { } participant)
                return new WhatsAppIncomingResult(false);
            var ev = await store.EventAsync(connection.EventId, token) ?? throw new LocalAccessException();
            var eventDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(ev.StartsAtUtc, TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId)));
            var identity = options.Fingerprint("meal-identity", $"event:{ev.Revision};" + string.Join(';', aliases.Where(x => x.PersonId == participant.PersonId)
                .OrderBy(x => x.Id).Select(x => $"{x.Id}:{x.Revision}:{x.IsActive}")));
            var messageHash = options.Fingerprint("message", (input.IsPrivate ? "private:" : "") + input.GroupId + ":" + input.MessageId);
            var commandHash = options.Fingerprint("command", input.Text);
            var previous = (await store.ListAsync<WhatsAppCommandReceipt>(connection.EventId, token)).SingleOrDefault(x => x.MessageHash == messageHash);
            if (previous is not null)
            {
                if (previous.PersonId != participant.PersonId || previous.ActorId != actor || previous.CommandHash != commandHash)
                    throw new LocalConflictException("Identificativo messaggio già elaborato con dati diversi.");
                return new WhatsAppIncomingResult(true, true);
            }
            if (await identities.ReceiptAsync(connection.EventId, messageHash, token) is not null)
                throw new LocalConflictException("Identificativo messaggio già utilizzato per una presenza.");
            var receipt = new WhatsAppCommandReceipt(connection.EventId, messageHash, commandHash, participant.PersonId, actor, now);
            var preference = await PreferenceIncomingAsync(input, connection, participant.PersonId, actor, identity, now, receipt.Id, token);
            if (preference is not null)
            {
                if (preference.Handled) store.Add(receipt);
                await store.SaveAsync(token); return preference;
            }
            var session = await store.FindAsync<MealProposalSession>(connection.EventId, participant.PersonId, token);
            var valid = session is not null && session.Step != MealProposalStep.Closed && session.ActorId == actor
                && session.GroupId == connection.GroupId && session.ConnectionRevision == connection.Revision
                && session.IdentityFingerprint == identity && now >= session.StartedAtUtc && now < session.ExpiresAtUtc;
            if (session is not null && session.Step != MealProposalStep.Closed && !valid) session.Close();
            var cycles = await store.ListAsync<MealVotingCycle>(connection.EventId, token);
            if ((!input.IsPrivate || valid) && cycles.Any() && cycles.All(x => x.State != MealVotingState.Collection))
            {
                session?.Close(); store.Add(receipt); await store.SaveAsync(token);
                return new(true, ReplyText: "La raccolta delle proposte e chiusa. Scrivi !preferenze nel gruppo per consultare la votazione.",
                    ReplyOperationId: receipt.Id, ReplyPrivately: true);
            }
            string? reply;
            var success = false;
            if (!input.IsPrivate)
            {
                if (session is null) { session = new(connection.EventId, participant.PersonId); store.Add(session); }
                if (!valid) session.Start(actor, connection.GroupId!, connection.Revision, identity, now);
                reply = MealPrompt(session, eventDate);
            }
            else
            {
                if (!valid)
                {
                    if (session is not null) await store.SaveAsync(token);
                    return new WhatsAppIncomingResult(false);
                }
                if (timestamp.Ticks < session!.StartedAtUtc.Ticks - session.StartedAtUtc.Ticks % TimeSpan.TicksPerSecond)
                    return new WhatsAppIncomingResult(false);
                var text = input.Text.Trim();
                if (text.Equals("!annulla", StringComparison.OrdinalIgnoreCase))
                { session.Close(); reply = "Proposta annullata. Nessun piatto registrato."; }
                else if (text.Equals("!proponi", StringComparison.OrdinalIgnoreCase)) reply = MealPrompt(session, eventDate);
                else
                {
                    switch (session.Step)
                    {
                        case MealProposalStep.Day:
                            var day = ParseArrival(text, eventDate);
                            if (day.HasValue && Math.Abs(day.Value.DayNumber - eventDate.DayNumber) <= 31) session.SetDay(day.Value);
                            break;
                        case MealProposalStep.Meal:
                            var meal = text.ToLowerInvariant() switch { "1" or "colazione" => MealKind.Breakfast, "2" or "pranzo" => MealKind.Lunch, "3" or "cena" => MealKind.Dinner, _ => (MealKind?)null };
                            if (meal.HasValue) session.SetMeal(meal.Value);
                            break;
                        case MealProposalStep.Dish:
                            if (!text.StartsWith('!'))
                            { try { session.SetDish(text); } catch (ArgumentException) { } }
                            break;
                        case MealProposalStep.Availability:
                            if (text.Equals("1") || text.Equals("suggerimento", StringComparison.OrdinalIgnoreCase)) session.SetAvailability(false);
                            else if (text.Equals("2") || text.Equals("cucino", StringComparison.OrdinalIgnoreCase)) session.SetAvailability(true);
                            break;
                        case MealProposalStep.Confirmation:
                            if (text.Equals("!conferma", StringComparison.OrdinalIgnoreCase))
                            {
                                var proposal = session.Confirm(now); store.Add(proposal);
                                operations.Audit(connection.EventId, actor, proposal.Id, "MealProposals", "Confirmed"); success = true;
                            }
                            break;
                    }
                    reply = success ? "Proposta registrata per gli organizzatori. Grazie!" : MealPrompt(session, eventDate);
                }
            }
            store.Add(receipt); await store.SaveAsync(token);
            return new WhatsAppIncomingResult(true, Reaction: !input.IsPrivate || success ? "✅" : null,
                ReplyText: reply, ReplyOperationId: receipt.Id, ReplyPrivately: true);
        }, ct);
    }

    private static string MealPrompt(MealProposalSession session, DateOnly eventDate) => session.Step switch
    {
        MealProposalStep.Day => $"Per quale giorno proponi un piatto? Scrivi gg/mm oppure gg/mm/aaaa, dal {eventDate.AddDays(-31):dd/MM/yyyy} al {eventDate.AddDays(31):dd/MM/yyyy}. Hai cinque minuti dall'avvio. !annulla interrompe.",
        MealProposalStep.Meal => "Per quale pasto? 1 = colazione, 2 = pranzo, 3 = cena.",
        MealProposalStep.Dish => "Quale piatto proponi? Scrivi solo il nome, massimo 200 caratteri.",
        MealProposalStep.Availability => "1 = solo suggerimento, 2 = mi offro di cucinarlo.",
        MealProposalStep.Confirmation => $"Riepilogo: {session.Day:dd/MM/yyyy}, {MealLabel(session.Meal!.Value)}, {SafeMealDish(session.DishName!)}.\n{(session.OffersToCook == true ? "Ti offri di cucinare." : "Solo suggerimento.")}\nScrivi !conferma per registrare oppure !annulla. La scadenza resta cinque minuti dall'avvio.",
        _ => "Scrivi !proponi nel gruppo per una nuova proposta."
    };
    private static string SafeMealDish(string text) => new(text.Select(c => c is '*' or '_' or '~' or '`' or '@' ? ' ' : c).ToArray());
    private static string MealLabel(MealKind meal) => meal switch { MealKind.Breakfast => "colazione", MealKind.Lunch => "pranzo", _ => "cena" };
}
