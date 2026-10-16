using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Accounting;

public sealed record ExpenseDeclaration(Guid OperationId, Guid AdvancedByPersonId, string Description, decimal Amount, bool Confirmed);
public sealed record PaymentDeclaration(Guid OperationId, Guid FromPersonId, Guid ToPersonId, decimal Amount, bool Confirmed);
public sealed record BalanceLine(Guid PersonId, string Name, ParticipationStatus Status, decimal Weight, decimal Share,
    decimal Advanced, decimal Paid, decimal Received, decimal Balance);
public sealed record AccountingOverview(Event Event, IReadOnlyList<LocalParticipation> Participants, IReadOnlyList<Expense> Expenses,
    IReadOnlyList<Payment> Payments, AccountingSettings Settings, IReadOnlyList<ParticipantWeight> Overrides,
    IReadOnlyList<BalanceLine> Balances, decimal ApprovedTotal, decimal Unallocated)
{
    public decimal ReconciliationResidual => Balances.Sum(x => x.Balance) + Unallocated;
}

public sealed class AccountingService(ILocalStore store, LocalOperations operations, TimeProvider clock)
{
    // One transaction gives all balances a coherent snapshot of presences, weights and ledger rows.
    public Task<AccountingOverview> ListAsync(Guid actorId, Guid eventId, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var ev = await store.EventAsync(eventId, token) ?? throw new LocalAccessException();
            var people = await store.ParticipationsAsync(eventId, token);
            var expenses = await store.ListAsync<Expense>(eventId, token);
            var payments = await store.ListAsync<Payment>(eventId, token);
            var settings = await store.FindAsync<AccountingSettings>(eventId, eventId, token) ?? new AccountingSettings(eventId);
            var overrides = await store.ListAsync<ParticipantWeight>(eventId, token);
            var members = await store.ListAsync<HouseholdMember>(eventId, token);
            decimal Weight(LocalParticipation person)
            {
                var custom = overrides.SingleOrDefault(x => x.PersonId == person.PersonId)?.Weight;
                return custom ?? (members.Any(x => x.PersonId == person.PersonId && x.IsActive && x.Kind == HouseholdMemberKind.Child)
                    ? settings.ChildWeight : 1m);
            }
            var total = expenses.Where(x => x.Status == ExpenseStatus.Approved).Sum(x => x.Amount);
            var allocation = ShareCalculator.Allocate(total, people.Where(x => x.Status == ParticipationStatus.Confirmed)
                .Select(x => new WeightedPerson(x.PersonId, Weight(x))).ToArray());
            var balances = people.Select(person =>
            {
                var advanced = expenses.Where(x => x.Status == ExpenseStatus.Approved && x.AdvancedByPersonId == person.PersonId).Sum(x => x.Amount);
                var paid = payments.Where(x => x.Status == PaymentStatus.Confirmed && x.FromPersonId == person.PersonId).Sum(x => x.Amount);
                var received = payments.Where(x => x.Status == PaymentStatus.Confirmed && x.ToPersonId == person.PersonId).Sum(x => x.Amount);
                var share = allocation.Shares.GetValueOrDefault(person.PersonId);
                return new BalanceLine(person.PersonId, person.Name, person.Status, Weight(person), share, advanced, paid, received,
                    share - advanced - paid + received);
            }).ToArray();
            return new AccountingOverview(ev, people, expenses.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToArray(),
                payments.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToArray(), settings, overrides, balances, total, allocation.Unallocated);
        }, ct);

    public Task<Guid> DeclareExpenseAsync(Guid actorId, Guid eventId, ExpenseDeclaration input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var candidate = new Expense(eventId, input.OperationId, input.AdvancedByPersonId, actorId, input.Description, input.Amount, clock.GetUtcNow().UtcDateTime);
            var existing = await store.FindAsync<Expense>(eventId, candidate.Id, token);
            if (existing is not null)
            {
                if (existing.AdvancedByPersonId == candidate.AdvancedByPersonId && existing.DeclaredByActorId == actorId
                    && existing.Description == candidate.Description && existing.Amount == candidate.Amount) return existing.Id;
                throw new LocalConflictException("Identificativo già usato per una spesa diversa.");
            }
            Confirm(input.Confirmed); await Participant(eventId, candidate.AdvancedByPersonId, token);
            store.Add(candidate); operations.Audit(eventId, actorId, candidate.Id, "Accounting", "ExpenseDeclared");
            await store.SaveAsync(token); return candidate.Id;
        }, ct);

    public Task<Guid> DeclarePaymentAsync(Guid actorId, Guid eventId, PaymentDeclaration input, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var candidate = new Payment(eventId, input.OperationId, input.FromPersonId, input.ToPersonId, actorId, input.Amount, clock.GetUtcNow().UtcDateTime);
            var existing = await store.FindAsync<Payment>(eventId, candidate.Id, token);
            if (existing is not null)
            {
                if (existing.FromPersonId == candidate.FromPersonId && existing.ToPersonId == candidate.ToPersonId
                    && existing.Amount == candidate.Amount && existing.DeclaredByActorId == actorId) return existing.Id;
                throw new LocalConflictException("Identificativo già usato per un pagamento diverso.");
            }
            Confirm(input.Confirmed); await Participant(eventId, candidate.FromPersonId, token); await Participant(eventId, candidate.ToPersonId, token);
            store.Add(candidate); operations.Audit(eventId, actorId, candidate.Id, "Accounting", "PaymentDeclared");
            await store.SaveAsync(token); return candidate.Id;
        }, ct);

    public Task<bool> ExpenseTransitionAsync(Guid actorId, Guid eventId, Guid id, int revision, ExpenseStatus target, bool confirmed,
        CancellationToken ct = default) => operations.ChangeAsync(actorId, eventId, async token =>
    {
        var expense = await store.FindAsync<Expense>(eventId, id, token) ?? throw new LocalAccessException();
        LocalOperations.Revision(expense, revision);
        if (!expense.Transition(target, confirmed, clock.GetUtcNow().UtcDateTime)) return false;
        operations.Audit(eventId, actorId, id, "Accounting", "Expense" + target); await store.SaveAsync(token); return true;
    }, ct);

    public Task<bool> PaymentTransitionAsync(Guid actorId, Guid eventId, Guid id, int revision, PaymentStatus target, bool confirmed,
        CancellationToken ct = default) => operations.ChangeAsync(actorId, eventId, async token =>
    {
        var payment = await store.FindAsync<Payment>(eventId, id, token) ?? throw new LocalAccessException();
        LocalOperations.Revision(payment, revision);
        if (!payment.Transition(target, confirmed, clock.GetUtcNow().UtcDateTime)) return false;
        operations.Audit(eventId, actorId, id, "Accounting", "Payment" + target); await store.SaveAsync(token); return true;
    }, ct);

    public Task<bool> ConfigureAsync(Guid actorId, Guid eventId, decimal childWeight, int revision, bool confirmed, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            Confirm(confirmed);
            var settings = await store.FindAsync<AccountingSettings>(eventId, eventId, token);
            var fresh = settings is null; settings ??= new AccountingSettings(eventId);
            LocalOperations.Revision(settings, revision); var changed = settings.Update(childWeight);
            if (fresh || changed)
            {
                if (fresh) store.Add(settings);
                operations.Audit(eventId, actorId, eventId, "Accounting", "WeightsConfigured"); await store.SaveAsync(token);
            }
            return fresh || changed;
        }, ct);

    public Task<bool> OverrideAsync(Guid actorId, Guid eventId, Guid personId, Guid operationId, decimal? weight, int revision, bool confirmed,
        CancellationToken ct = default) => operations.ChangeAsync(actorId, eventId, async token =>
    {
        Confirm(confirmed); await Participant(eventId, personId, token);
        var row = (await store.ListAsync<ParticipantWeight>(eventId, token)).SingleOrDefault(x => x.PersonId == personId);
        if (row is not null)
        {
            if (row.Id != operationId) throw new LocalConflictException("Peso già configurato da un'altra pagina. Ricarica.");
            var existing = await store.FindAsync<ParticipantWeight>(eventId, row.Id, token) ?? throw new LocalAccessException();
            LocalOperations.Revision(existing, revision);
            if (!existing.Update(weight)) return false;
        }
        else
        {
            if (revision != 0) throw new LocalConflictException("Revisione non valida.");
            store.Add(new ParticipantWeight(eventId, operationId, personId, weight));
        }
        operations.Audit(eventId, actorId, personId, "Accounting", "WeightOverridden"); await store.SaveAsync(token); return true;
    }, ct);

    private async Task Participant(Guid eventId, Guid personId, CancellationToken ct)
    {
        if (!(await store.ParticipationsAsync(eventId, ct)).Any(x => x.PersonId == personId))
            throw new LocalValidationException("Seleziona una persona già partecipante di questo evento.");
    }
    private static void Confirm(bool confirmed)
    { if (!confirmed) throw new LocalValidationException("Conferma esplicitamente l'operazione contabile."); }
}
