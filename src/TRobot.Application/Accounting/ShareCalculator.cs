using System.Numerics;
using TRobot.Domain;

namespace TRobot.Application.Accounting;

public sealed record WeightedPerson(Guid PersonId, decimal Weight);
public sealed record ShareAllocation(IReadOnlyDictionary<Guid, decimal> Shares, decimal Unallocated);

public static class ShareCalculator
{
    // Largest remainders in integer cents/weight units: no floating-point or intermediate rounding.
    public static ShareAllocation Allocate(decimal total, IReadOnlyList<WeightedPerson> people)
    {
        if (total < 0 || decimal.Round(total, 2) != total) throw new ArgumentException("Totale non valido.");
        if (people.Select(x => x.PersonId).Distinct().Count() != people.Count || people.Any(x => x.PersonId == Guid.Empty))
            throw new ArgumentException("Partecipanti duplicati o non validi.");
        foreach (var person in people) AccountingRules.Weight(person.Weight);
        var cents = new BigInteger(total * 100m);
        var units = people.ToDictionary(x => x.PersonId, x => new BigInteger(x.Weight * 1000m));
        var sum = units.Values.Aggregate(BigInteger.Zero, (a, b) => a + b);
        if (sum.IsZero) return new(people.ToDictionary(x => x.PersonId, _ => 0m), total);
        var rows = units.Select(x =>
        {
            var floor = BigInteger.DivRem(cents * x.Value, sum, out var remainder);
            return (Id: x.Key, Cents: floor, Remainder: remainder);
        }).ToArray();
        var remaining = cents - rows.Aggregate(BigInteger.Zero, (a, b) => a + b.Cents);
        var winners = rows.OrderByDescending(x => x.Remainder).ThenBy(x => x.Id.ToString("N"), StringComparer.Ordinal)
            .Take(checked((int)remaining)).Select(x => x.Id).ToHashSet();
        return new(rows.ToDictionary(x => x.Id, x => checked((decimal)(x.Cents + (winners.Contains(x.Id) ? 1 : 0))) / 100m), 0m);
    }
}
