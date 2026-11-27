using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed partial class WhatsAppService
{
    private sealed record PrivateMealRoute(WhatsAppConnection Connection, Guid Actor, Guid Voter, string Identity);

    private async Task<WhatsAppIncomingResult> MealIncomingAsync(WhatsAppIncoming input, CancellationToken ct)
    {
        if (input.GroupId is { } groupId && (groupId.Length > 200 || !groupId.EndsWith("@g.us", StringComparison.Ordinal)))
            return new(false);
        if (!input.IsPrivate && string.IsNullOrWhiteSpace(input.GroupId)) return new(false);
        if (string.IsNullOrWhiteSpace(input.MessageId) || input.MessageId.Length > 200
            || string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > (input.IsPrivate ? 300 : 100)) return new(false);
        string[] hashes;
        try { hashes = HashAliases(input.SenderIds); } catch (LocalValidationException) { return new(false); }
        DateTime timestamp;
        try { timestamp = DateTimeOffset.FromUnixTimeSeconds(input.TimestampUnixSeconds).UtcDateTime; }
        catch (ArgumentOutOfRangeException) { return new(false); }

        if (input.IsPrivate)
        {
            var route = await ResolvePrivateMealRouteAsync(hashes, timestamp, ct);
            if (route is null) return await PrivateMealReplayAsync(input, hashes, ct) ?? new(false);
            return await MealTransactionAsync(input, route.Connection, route.Actor, route.Voter, route.Identity, timestamp, ct);
        }

        var connection = await identities.ConnectionByGroupAsync(input.GroupId!, ct);
        if (connection?.ActorId is not { } actor || !await AuthorizedActivatorAsync(connection, ct)) return new(false);
        return await MealTransactionAsync(input, connection, actor, null, null, timestamp, ct);
    }

    private async Task<WhatsAppIncomingResult> MealTransactionAsync(WhatsAppIncoming input, WhatsAppConnection resolved,
        Guid actor, Guid? knownVoter, string? knownIdentity, DateTime timestamp, CancellationToken ct)
    {
        var groupId = resolved.GroupId!;
        return await operations.ChangeAsync(actor, resolved.EventId, async token =>
        {
            await RequireAdmin(actor, resolved.EventId, token);
            var connection = await identities.ConnectionByGroupAsync(groupId, token);
            var now = clock.GetUtcNow().UtcDateTime;
            if (connection is null || !connection.Enabled || connection.EventId != resolved.EventId || connection.ActorId != actor
                || connection.GroupId != groupId || !connection.ActivatedAtUtc.HasValue || timestamp < connection.ActivatedAtUtc.Value
                || timestamp < now.AddSeconds(-120) || timestamp > now.AddSeconds(30)) return new WhatsAppIncomingResult(false);

            var text = input.Text.Trim();
            if (!input.IsPrivate && text.Equals("!preferenze", StringComparison.OrdinalIgnoreCase)
                && !connection.Allows(WhatsAppFeature.MealPreferences)) return new WhatsAppIncomingResult(false);
            if (!input.IsPrivate && text.Equals("!proponi", StringComparison.OrdinalIgnoreCase)
                && !connection.Allows(WhatsAppFeature.MealProposals)) return new WhatsAppIncomingResult(false);

            var aliases = await store.ListAsync<WhatsAppIdentityAlias>(connection.EventId, token);
            Guid voter;
            if (knownVoter is { } known) voter = known;
            else
            {
                var hashes = HashAliases(input.SenderIds);
                var matches = aliases.Where(x => x.IsActive && hashes.Contains(x.SenderHash)).Select(x => x.PersonId).Distinct().ToArray();
                if (matches.Length != 1) return new WhatsAppIncomingResult(false);
                voter = matches[0];
            }
            if (await identities.ParticipantAsync(connection.EventId, voter, token) is not { } participant)
                return new WhatsAppIncomingResult(false);
            var ev = await store.EventAsync(connection.EventId, token) ?? throw new LocalAccessException();
            var identity = knownIdentity ?? MealIdentity(ev, aliases, participant.PersonId);
            var messageHash = options.Fingerprint("message", (input.IsPrivate ? "private:" : groupId + ":") + input.MessageId);
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
            if (!connection.Allows(WhatsAppFeature.MealProposals))
            {
                if (session is not null && session.Step != MealProposalStep.Closed) { session.Close(); await store.SaveAsync(token); }
                return new WhatsAppIncomingResult(false);
            }
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
            string? rejection = null;
            var stay = await store.FindAsync<AttendanceStay>(connection.EventId, participant.PersonId, token);
            var eventDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(ev.StartsAtUtc, TimeZoneInfo.FindSystemTimeZoneById(ev.TimeZoneId)));
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
                if (text.Equals("!annulla", StringComparison.OrdinalIgnoreCase))
                { session.Close(); reply = "Proposta annullata. Nessun piatto registrato."; }
                else if (text.Equals("!proponi", StringComparison.OrdinalIgnoreCase)) reply = MealPrompt(session, eventDate);
                else
                {
                    switch (session.Step)
                    {
                        case MealProposalStep.Day:
                            var day = ParseArrival(text, eventDate);
                            if (day.HasValue && Math.Abs(day.Value.DayNumber - eventDate.DayNumber) <= 31)
                            {
                                rejection = MealProposalIneligible(participant, stay, day.Value);
                                if (rejection is null) session.SetDay(day.Value);
                            }
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
                                rejection = MealProposalIneligible(participant, stay, session.Day!.Value);
                                if (rejection is not null)
                                {
                                    session.Close();
                                    rejection += " Nessun piatto registrato. Aggiorna la presenza e scrivi !proponi nel gruppo per ripartire.";
                                }
                                else
                                {
                                    var proposal = session.Confirm(now); store.Add(proposal);
                                    operations.Audit(connection.EventId, actor, proposal.Id, "MealProposals", "Confirmed"); success = true;
                                }
                            }
                            break;
                    }
                    reply = success ? "Proposta registrata per gli organizzatori. Grazie!" : rejection is null ? MealPrompt(session, eventDate)
                        : rejection + (session.Step == MealProposalStep.Day ? "\n" + MealPrompt(session, eventDate) : "");
                }
            }
            store.Add(receipt); await store.SaveAsync(token);
            return new WhatsAppIncomingResult(true, Reaction: !input.IsPrivate || success ? "✅" : null,
                ReplyText: reply, ReplyOperationId: receipt.Id, ReplyPrivately: true);
        }, ct);
    }

    private async Task<PrivateMealRoute?> ResolvePrivateMealRouteAsync(string[] hashes, DateTime timestamp, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var routes = new List<PrivateMealRoute>();
        foreach (var connection in await identities.ActiveConnectionsAsync(ct))
        {
            if (connection.GroupId is not { } groupId || connection.ActorId is not { } actor
                || !await AuthorizedActivatorAsync(connection, ct)) continue;
            var aliases = await store.ListAsync<WhatsAppIdentityAlias>(connection.EventId, ct);
            var matches = aliases.Where(x => x.IsActive && hashes.Contains(x.SenderHash)).Select(x => x.PersonId).Distinct().ToArray();
            if (matches.Length != 1 || await identities.ParticipantAsync(connection.EventId, matches[0], ct) is not { } participant)
                continue;
            var ev = await store.EventAsync(connection.EventId, ct) ?? throw new LocalAccessException();
            var identity = MealIdentity(ev, aliases, participant.PersonId);

            var proposal = await store.FindAsync<MealProposalSession>(connection.EventId, participant.PersonId, ct);
            if (proposal is not null && proposal.Step != MealProposalStep.Closed)
            {
                var valid = proposal.ActorId == actor && proposal.GroupId == groupId
                    && proposal.ConnectionRevision == connection.Revision && proposal.IdentityFingerprint == identity
                    && new DateTimeOffset(timestamp).ToUnixTimeSeconds() >= new DateTimeOffset(proposal.StartedAtUtc).ToUnixTimeSeconds()
                    && now >= proposal.StartedAtUtc && now < proposal.ExpiresAtUtc;
                if (valid && connection.Allows(WhatsAppFeature.MealProposals))
                    routes.Add(new(connection, actor, participant.PersonId, identity));
                else { proposal.Close(); await store.SaveAsync(ct); }
            }

            var preference = await store.FindAsync<MealPreferenceSession>(connection.EventId, participant.PersonId, ct);
            if (preference is not null && preference.Step != MealPreferenceStep.Closed)
            {
                var valid = preference.IsValid(actor, groupId, connection.Revision, identity, now);
                if (valid && connection.Allows(WhatsAppFeature.MealPreferences))
                    routes.Add(new(connection, actor, participant.PersonId, identity));
                else { preference.Close(); await store.SaveAsync(ct); }
            }
        }
        // Zero or more than one open dialog: never choose an event arbitrarily.
        return routes.Count == 1 ? routes[0] : null;
    }

    private async Task<WhatsAppIncomingResult?> PrivateMealReplayAsync(WhatsAppIncoming input, string[] hashes, CancellationToken ct)
    {
        foreach (var connection in await identities.ActiveConnectionsAsync(ct))
        {
            if (connection.GroupId is null || connection.ActorId is not { } actor
                || !await AuthorizedActivatorAsync(connection, ct)) continue;
            var aliases = await store.ListAsync<WhatsAppIdentityAlias>(connection.EventId, ct);
            var matches = aliases.Where(x => x.IsActive && hashes.Contains(x.SenderHash)).Select(x => x.PersonId).Distinct().ToArray();
            if (matches.Length != 1 || await identities.ParticipantAsync(connection.EventId, matches[0], ct) is not { } participant)
                continue;
            var messageHash = options.Fingerprint("message", "private:" + input.MessageId);
            var previous = (await store.ListAsync<WhatsAppCommandReceipt>(connection.EventId, ct))
                .SingleOrDefault(x => x.MessageHash == messageHash);
            if (previous is null) continue;
            var commandHash = options.Fingerprint("command", input.Text);
            if (previous.PersonId != participant.PersonId || previous.ActorId != actor || previous.CommandHash != commandHash)
                throw new LocalConflictException("Identificativo messaggio già elaborato con dati diversi.");
            return new WhatsAppIncomingResult(true, true);
        }
        return null;
    }

    private string MealIdentity(Event ev, IReadOnlyList<WhatsAppIdentityAlias> aliases, Guid personId) =>
        options.Fingerprint("meal-identity", $"event:{ev.Revision};" + string.Join(';', aliases.Where(x => x.PersonId == personId)
            .OrderBy(x => x.Id).Select(x => $"{x.Id}:{x.Revision}:{x.IsActive}")));

    private static string? MealProposalIneligible(EventParticipant participant, AttendanceStay? stay, DateOnly day)
    {
        if (participant.Status != ParticipationStatus.Confirmed) return "Per proporre un piatto serve la tua presenza confermata.";
        if (stay is null || stay.Days < 1 || stay.Arrival == default) return "Per proporre un piatto serve il tuo soggiorno con le date confermate.";
        return day < stay.Arrival || day > stay.LastDay ? $"Giorno fuori dal tuo soggiorno confermato, dal {stay.Arrival:dd/MM/yyyy} al {stay.LastDay:dd/MM/yyyy}." : null;
    }

    private static string MealPrompt(MealProposalSession session, DateOnly eventDate) => session.Step switch
    {
        MealProposalStep.Day => $"Per quale giorno proponi un piatto? Scrivi gg/mm oppure gg/mm/aaaa, dal {eventDate.AddDays(-31):dd/MM/yyyy} al {eventDate.AddDays(31):dd/MM/yyyy}. Puoi proporre solo nei giorni del tuo soggiorno con presenza confermata, inclusi arrivo e ultimo giorno. Hai cinque minuti dall'avvio. !annulla interrompe.",
        MealProposalStep.Meal => "Per quale pasto? 1 = colazione, 2 = pranzo, 3 = cena.",
        MealProposalStep.Dish => "Quale piatto proponi? Scrivi solo il nome, massimo 200 caratteri.",
        MealProposalStep.Availability => "1 = solo suggerimento, 2 = mi offro di cucinarlo.",
        MealProposalStep.Confirmation => $"Riepilogo: {session.Day:dd/MM/yyyy}, {MealLabel(session.Meal!.Value)}, {SafeMealDish(session.DishName!)}.\n{(session.OffersToCook == true ? "Ti offri di cucinare." : "Solo suggerimento.")}\nScrivi !conferma per registrare oppure !annulla. La scadenza resta cinque minuti dall'avvio.",
        _ => "Scrivi !proponi nel gruppo per una nuova proposta."
    };
    private static string SafeMealDish(string text) => new(text.Select(c => c is '*' or '_' or '~' or '`' or '@' ? ' ' : c).ToArray());
    private static string MealLabel(MealKind meal) => meal switch { MealKind.Breakfast => "colazione", MealKind.Lunch => "pranzo", _ => "cena" };
}
