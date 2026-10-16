using TRobot.Domain;

namespace TRobot.Application.Demo;

public interface IDemoSetupStore
{
    Task<Guid> CreateIfEmptyAsync(Event ev, Person organizer, Person participant, CancellationToken ct);
}

// Bootstrap consentito soltanto al processo locale esplicito, mai richiamato da HTTP.
public sealed class DemoSetupService(IDemoSetupStore store)
{
    public Task<Guid> InitializeAsync(bool localSetupAuthorized, CancellationToken ct = default)
    {
        if (!localSetupAuthorized) throw new UnauthorizedAccessException();
        var ev = new Event("Evento dimostrativo — Capodanno 2027", new DateTime(2026, 12, 31, 19, 0, 0, DateTimeKind.Utc));
        var organizer = new Person("Organizzatore demo");
        var participant = new Person("Partecipante demo", "demo-participant");
        return store.CreateIfEmptyAsync(ev, organizer, participant, ct);
    }
}
