using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Menu;

public sealed record MealProposalEntry(Guid Id, Guid PersonId, string PersonName, DateOnly Day, MealKind Meal,
    string DishName, bool OffersToCook, DateTime CreatedAtUtc, DateTime? WithdrawnAtUtc);
public sealed record MealProposalPage(Guid EventId, string EventName, string TimeZoneId, IReadOnlyList<MealProposalEntry> Proposals);

public sealed class MealProposalService(ILocalStore store, LocalOperations operations)
{
    public async Task<MealProposalPage> ReadAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        var proposals = await operations.ReadAsync<MealProposal>(actorId, eventId, ct);
        var ev = await store.EventAsync(eventId, ct) ?? throw new LocalAccessException();
        var people = await store.PeopleAsync(eventId, ct);
        return new(eventId, ev.Name, ev.TimeZoneId, proposals.OrderBy(x => x.Day).ThenBy(x => x.Meal).ThenBy(x => x.CreatedAtUtc)
            .Select(x => new MealProposalEntry(x.Id, x.PersonId, people.SingleOrDefault(p => p.Id == x.PersonId)?.Name ?? "Partecipante",
                x.Day, x.Meal, x.DishName, x.OffersToCook, x.CreatedAtUtc, x.WithdrawnAtUtc)).ToArray());
    }
}
