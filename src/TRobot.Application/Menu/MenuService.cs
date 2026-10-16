using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Menu;

public sealed record MenuInput(Guid Id, string Name, MenuCategory Category, decimal Quantity, string Unit,
    Guid? ResponsiblePersonId, MenuAvailability Availability, string? Notes, int? Revision, bool ConfirmCancellation);
public sealed record MenuPage(Guid EventId, string EventName, IReadOnlyList<MenuItem> Items, IReadOnlyList<LocalPerson> People);

public sealed class MenuService(ILocalStore store, LocalOperations operations)
{
    public async Task<MenuPage> ReadAsync(Guid actorId, Guid eventId, CancellationToken ct = default)
    {
        var items = await operations.ReadAsync<MenuItem>(actorId, eventId, ct);
        var ev = await store.EventAsync(eventId, ct) ?? throw new LocalAccessException();
        return new(eventId, ev.Name, items.OrderBy(x => x.Category).ThenBy(x => x.Name).ToArray(), await store.PeopleAsync(eventId, ct));
    }

    public Task<Guid> SaveAsync(Guid actorId, Guid eventId, MenuInput input, bool create, CancellationToken ct = default) =>
        operations.ChangeAsync(actorId, eventId, async token =>
        {
            var candidate = new MenuItem(input.Id, eventId, input.Name, input.Category, input.Quantity, input.Unit,
                input.ResponsiblePersonId, input.Availability, input.Notes);
            await operations.CheckPersonAsync(eventId, input.ResponsiblePersonId, token);
            var current = await store.FindAsync<MenuItem>(eventId, input.Id, token);
            if (create && current is not null)
            {
                if (current.SameContent(candidate)) return current.Id;
                throw new LocalConflictException("Identificativo già utilizzato con dati diversi. Ricarica la pagina.");
            }
            if (!create && current is null) throw new LocalValidationException("Voce menu non trovata nell'evento.");
            if (current is not null)
            {
                if (!input.Revision.HasValue) throw new LocalValidationException("Revisione obbligatoria.");
                LocalOperations.Revision(current, input.Revision.Value);
            }
            if (input.Availability == MenuAvailability.Cancelled && current?.Availability != MenuAvailability.Cancelled && !input.ConfirmCancellation)
                throw new LocalValidationException("Conferma esplicitamente l'annullamento della voce.");
            if (create) store.Add(candidate);
            else if (!current!.Update(input.Name, input.Category, input.Quantity, input.Unit, input.ResponsiblePersonId, input.Availability, input.Notes)) return input.Id;
            operations.Audit(eventId, actorId, input.Id, "Menu", create ? "Created" : "Updated");
            await store.SaveAsync(token); return input.Id;
        }, ct);
}
