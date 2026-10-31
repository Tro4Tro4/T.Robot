using TRobot.Application.Local;
using TRobot.Application.Menu;
using TRobot.Domain;

namespace TRobot.Application.WhatsApp;

public sealed partial class WhatsAppService
{
    private async Task<WhatsAppIncomingResult?> PreferenceIncomingAsync(WhatsAppIncoming input, WhatsAppConnection connection,
        Guid voter, Guid actor, string identity, DateTime now, Guid operationId, CancellationToken ct)
    {
        var session = await store.FindAsync<MealPreferenceSession>(connection.EventId, voter, ct);
        var valid = session?.IsValid(actor, connection.GroupId!, connection.Revision, identity, now) == true;
        if (session is not null && session.Step != MealPreferenceStep.Closed && !valid) session.Close();
        var starting = !input.IsPrivate && input.Text.Trim().Equals("!preferenze", StringComparison.OrdinalIgnoreCase);
        WhatsAppIncomingResult Reply(string text) => new(true, Reaction: starting ? "✅" : null,
            ReplyText: text, ReplyOperationId: operationId, ReplyPrivately: true);
        if (!starting && !valid) return null;
        if (!input.IsPrivate && !starting)
            return Reply("Hai un dialogo preferenze aperto. Completalo o scrivi !annulla in privato prima di proporre un piatto.");

        var proposals = await store.FindAsync<MealProposalSession>(connection.EventId, voter, ct);
        if (starting && proposals is not null && proposals.Step != MealProposalStep.Closed
            && proposals.ActorId == actor && proposals.GroupId == connection.GroupId && proposals.ConnectionRevision == connection.Revision
            && proposals.IdentityFingerprint == identity && now >= proposals.StartedAtUtc && now < proposals.ExpiresAtUtc)
            return Reply("Hai una proposta aperta. Completala o scrivi !annulla in privato prima di iniziare le preferenze.");

        var voting = new MealVotingService(store, operations, clock);
        var page = await voting.ReadAsync(actor, connection.EventId, ct);
        var cycle = page.CurrentCycle;
        if (cycle is null || cycle.State is MealVotingState.Collection or MealVotingState.Preparation)
        {
            session?.Close(); return Reply("Le preferenze non sono ancora aperte. Gli organizzatori stanno preparando l'elenco.");
        }
        IReadOnlyList<MealVotingPerson> people;
        try { people = await voting.ReadVoterAsync(actor, connection.EventId, voter, ct); }
        catch (LocalValidationException ex) { session?.Close(); return Reply(ex.Message); }
        catch (LocalAccessException)
        {
            await RequireAdmin(actor, connection.EventId, ct);
            session?.Close();
            return Reply("Per le preferenze serve una classificazione adulta attiva. Chiedi agli organizzatori di verificare la tua scheda e i collegamenti dei figli.");
        }
        if (people.Count == 0) { session?.Close(); return Reply("Per votare serve la classificazione adulta e il collegamento dei figli aggiornato dagli organizzatori."); }
        var sortedPeople = people.OrderBy(x => x.PersonId).ToArray();
        if (starting && !valid)
        {
            session ??= new(connection.EventId, voter);
            if (await store.FindAsync<MealPreferenceSession>(connection.EventId, voter, ct) is null) store.Add(session);
            session.Start(actor, connection.GroupId!, connection.Revision, identity, cycle.Id, cycle.Revision, sortedPeople.Select(x => x.PersonId), now);
        }
        if (session is null) return new(false);
        if (session.CycleId != cycle.Id || session.CycleRevision != cycle.Revision
            || session.PersonOptions != MealPreferenceSession.Encode(sortedPeople.Select(x => x.PersonId)))
        {
            session.Close(); return Reply("Elenco o persone gestibili cambiati. Scrivi !preferenze nel gruppo per ricaricare.");
        }
        if (input.IsPrivate && input.TimestampUnixSeconds < new DateTimeOffset(session.StartedAtUtc).ToUnixTimeSeconds()) return new(false);
        var text = input.Text.Trim();
        if (input.IsPrivate && text.Equals("!annulla", StringComparison.OrdinalIgnoreCase))
        { session.Close(); return Reply("Modifiche annullate. Le preferenze gia confermate restano salvate."); }
        if (input.IsPrivate && text.Equals("!cambia", StringComparison.OrdinalIgnoreCase)) session.ResetPerson();
        else if (input.IsPrivate && !text.Equals("!preferenze", StringComparison.OrdinalIgnoreCase))
        {
            switch (session.Step)
            {
                case MealPreferenceStep.Person:
                    if (text.StartsWith("!pagina ", StringComparison.OrdinalIgnoreCase) && int.TryParse(text[8..].Trim(), out var personPage)
                        && personPage >= 1 && personPage <= Math.Max(1, (sortedPeople.Length + 4) / 5))
                        session.ShowPage(personPage, Math.Max(1, (sortedPeople.Length + 4) / 5));
                    else if (int.TryParse(text, out var personNumber) && personNumber >= 1 && personNumber <= sortedPeople.Length)
                        session.ChoosePerson(sortedPeople[personNumber - 1].PersonId);
                    break;
                case MealPreferenceStep.Day:
                    var eventInfo = await store.EventAsync(connection.EventId, ct) ?? throw new LocalAccessException();
                    var eventDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(eventInfo.StartsAtUtc, TimeZoneInfo.FindSystemTimeZoneById(eventInfo.TimeZoneId)));
                    var day = ParseArrival(text, eventDay);
                    if (day.HasValue && page.Entries.Any(x => x.CycleId == cycle.Id && x.Day == day.Value && page.CurrentCounts.Any(c => c.EntryId == x.Id))) session.ChooseDay(day.Value);
                    break;
                case MealPreferenceStep.Meal:
                    var meal = text.ToLowerInvariant() switch { "1" or "colazione" => MealKind.Breakfast, "2" or "pranzo" => MealKind.Lunch, "3" or "cena" => MealKind.Dinner, _ => (MealKind?)null };
                    if (meal.HasValue)
                    {
                        var ballot = await voting.ReadBallotAsync(actor, connection.EventId, voter, session.PersonId!.Value, session.Day!.Value, meal.Value, ct);
                        if (ballot.Entries.Count != 0) session.ChooseMeal(meal.Value, ballot.SelectedEntryIds, ballot.BallotRevision, ballot.EligibilityFingerprint);
                    }
                    break;
                case MealPreferenceStep.Selection:
                case MealPreferenceStep.Confirmation:
                    var current = await voting.ReadBallotAsync(actor, connection.EventId, voter, session.PersonId!.Value, session.Day!.Value, session.Meal!.Value, ct);
                    if (current.BallotRevision != session.BallotRevision || current.EligibilityFingerprint != session.EligibilityFingerprint)
                    { session.Close(); return Reply("Presenze o preferenze cambiate. Scrivi !preferenze nel gruppo e ricontrolla il riepilogo."); }
                    var entries = current.Entries.ToArray();
                    var selected = MealPreferenceSession.Decode(session.SelectedIds).ToHashSet();
                    if (text.StartsWith("!pagina ", StringComparison.OrdinalIgnoreCase) && int.TryParse(text[8..].Trim(), out var number))
                    {
                        var count = session.Step == MealPreferenceStep.Confirmation ? Math.Max(1, (selected.Count + 4) / 5) : Math.Max(1, (entries.Length + 4) / 5);
                        if (number >= 1 && number <= count) session.ShowPage(number, count);
                    }
                    else if (session.Step == MealPreferenceStep.Confirmation)
                    {
                        if (text.Equals("!modifica", StringComparison.OrdinalIgnoreCase)) session.Edit();
                        else if (text.Equals("!conferma", StringComparison.OrdinalIgnoreCase))
                        {
                            var count = Math.Max(1, (selected.Count + 4) / 5);
                            if (!session.HasReviewed(count)) return Reply("Controlla tutte le pagine del riepilogo con !pagina N, poi !conferma.\n" + PreferenceReview(session, entries, sortedPeople));
                            if (!current.CanVote) return Reply(current.ExclusionReason ?? "Non puoi votare per questo pasto.");
                            try
                            {
                                await voting.SavePreferencesInTransactionAsync(actor, connection.EventId,
                                    new(operationId, cycle.Id, cycle.Revision, voter, session.PersonId.Value, session.Day.Value, session.Meal.Value,
                                        selected.ToArray(), session.BallotRevision, session.EligibilityFingerprint), ct);
                            }
                            catch (LocalConflictException) { session.Close(); return Reply("I dati sono cambiati. Nessuna preferenza modificata: riparti con !preferenze nel gruppo."); }
                            session.Close(); return Reply("Preferenze salvate. Scrivi !preferenze nel gruppo per consultare o scegliere un altro pasto o persona.");
                        }
                    }
                    else if (text.Equals("!conferma", StringComparison.OrdinalIgnoreCase) || text.Equals("!riepilogo", StringComparison.OrdinalIgnoreCase))
                    {
                        if (current.CanVote) session.Review();
                    }
                    else if (current.CanVote)
                    {
                        if (text.Equals("nessuna", StringComparison.OrdinalIgnoreCase)) session.SetSelection([]);
                        else if (text.Equals("tutti", StringComparison.OrdinalIgnoreCase)) session.SetSelection(entries.Select(x => x.Id));
                        else
                        {
                            var parts = text.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 0 && parts.All(x => int.TryParse(x, out var n) && n >= 1 && n <= entries.Length))
                            {
                                foreach (var n in parts.Select(int.Parse).Distinct())
                                    if (!selected.Add(entries[n - 1].Id)) selected.Remove(entries[n - 1].Id);
                                session.SetSelection(selected);
                            }
                        }
                    }
                    break;
            }
        }
        if (session.Step == MealPreferenceStep.Person)
            return Reply("Per chi vuoi consultare le preferenze?\n" + string.Join('\n', sortedPeople.Skip((session.Page - 1) * 5).Take(5)
                .Select((x, i) => $"{(session.Page - 1) * 5 + i + 1}. {(x.PersonId == voter ? "Per me" : SafeMealDish(x.Name))}"))
                + $"\nPagina {session.Page}/{Math.Max(1, (sortedPeople.Length + 4) / 5)}. !pagina N per sfogliare."
                + "\nScrivi il numero. Cinque minuti dall'avvio; !annulla interrompe.");
        if (session.Step == MealPreferenceStep.Day)
            return Reply("Per quale giorno? Scrivi gg/mm/aaaa. Giorni disponibili:\n" + string.Join(", ", page.Entries.Where(x => x.CycleId == cycle.Id && page.CurrentCounts.Any(c => c.EntryId == x.Id)).Select(x => x.Day).Distinct().Order().Select(x => x.ToString("dd/MM/yyyy")))
                + "\n!cambia torna alla scelta della persona.");
        if (session.Step == MealPreferenceStep.Meal) return Reply("Quale pasto? 1 = colazione, 2 = pranzo, 3 = cena.\n!cambia torna alla persona.");
        var view = await voting.ReadBallotAsync(actor, connection.EventId, voter, session.PersonId!.Value, session.Day!.Value, session.Meal!.Value, ct);
        if (session.Step == MealPreferenceStep.Confirmation) return Reply(PreferenceReview(session, view.Entries.ToArray(), sortedPeople));
        var choices = MealPreferenceSession.Decode(session.SelectedIds).ToHashSet();
        var counts = cycle.State == MealVotingState.Closed ? page.ClosedCounts : page.CurrentCounts;
        var lines = view.Entries.Skip((session.Page - 1) * 5).Take(5).Select((entry, offset) =>
        {
            var count = counts.SingleOrDefault(x => x.EntryId == entry.Id);
            return $"{(session.Page - 1) * 5 + offset + 1}. {(choices.Contains(entry.Id) ? "[scelto] " : "")}{SafeMealDish(entry.Name)}\nAdulti {count?.Adults ?? 0}, bambini {count?.Children ?? 0}, totale {count?.Total ?? 0}";
        });
        var label = cycle.State == MealVotingState.Closed ? "Risultato alla chiusura" : "Risultati provvisori";
        return Reply($"{session.Day:dd/MM/yyyy} - {MealLabel(session.Meal!.Value)}. {label}.\n" + string.Join('\n', lines)
            + $"\nPagina {session.Page}/{Math.Max(1, (view.Entries.Count + 4) / 5)}. !pagina N per sfogliare."
            + (view.CanVote ? "\nScrivi numeri separati da virgola per aggiungere/togliere scelte, tutti o nessuna. !riepilogo per controllare e confermare." : "\n" + view.ExclusionReason)
            + "\n!cambia per persona/giorno/pasto; !annulla per uscire.");
    }

    private static string PreferenceReview(MealPreferenceSession session, MealVotingEntry[] entries, MealVotingPerson[] people)
    {
        var selected = MealPreferenceSession.Decode(session.SelectedIds).ToHashSet();
        var chosen = entries.Where(x => selected.Contains(x.Id)).ToArray();
        var person = people.Single(x => x.PersonId == session.PersonId);
        return $"Riepilogo per {SafeMealDish(person.Name)}: {session.Day:dd/MM/yyyy}, {MealLabel(session.Meal!.Value)}.\n"
            + (chosen.Length == 0 ? "Nessuna preferenza: le scelte precedenti saranno rimosse." : string.Join('\n', chosen.Skip((session.Page - 1) * 5).Take(5).Select(x => "- " + SafeMealDish(x.Name))))
            + $"\n{chosen.Length} piatti scelti. Pagina {session.Page}/{Math.Max(1, (chosen.Length + 4) / 5)}. !pagina N per il resto."
            + "\n!conferma salva dopo il controllo di tutte le pagine; !modifica torna alle scelte; !annulla scarta le modifiche.";
    }
}
