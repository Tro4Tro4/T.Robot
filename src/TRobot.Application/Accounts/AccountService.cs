using TRobot.Application.Attendance;
using TRobot.Domain;

namespace TRobot.Application.Accounts;

public static class AccountClaims
{
    public const string PersonId = "trobot:person_id";
    public const string SystemAdministrator = "trobot:system_administrator";
}

public sealed record AccountSummary(Guid PersonId, string UserName, string DisplayName, bool SystemAdministrator,
    bool IsEnabled, IReadOnlyList<EventOption> Assignments);
public sealed record AccountDirectory(IReadOnlyList<AccountSummary> Accounts, IReadOnlyList<EventOption> Events);
public sealed class AccountValidationException(string message) : Exception(message);
public sealed class AccountAccessException : Exception;

public interface IOrganizerAccountStore
{
    Task<bool> IsAdministratorAsync(Guid actorId, CancellationToken ct);
    Task<AccountDirectory> GetDirectoryAsync(CancellationToken ct);
    Task BootstrapAsync(Guid personId, string userName, string password, CancellationToken ct);
    Task CreateAsync(Guid actorId, Guid eventId, Person person, string userName, string password, CancellationToken ct);
    Task ChangePasswordAsync(Guid actorId, string currentPassword, string newPassword, CancellationToken ct);
    Task<bool> SetAssignmentAsync(Guid actorId, Guid personId, Guid eventId, bool active, CancellationToken ct);
    Task<bool> SetEnabledAsync(Guid actorId, Guid personId, bool enabled, CancellationToken ct);
}

public sealed class AccountService(IOrganizerAccountStore store)
{
    public async Task<AccountDirectory> GetDirectoryAsync(Guid actorId, CancellationToken ct = default)
    {
        await AuthorizeAsync(actorId, ct);
        return await store.GetDirectoryAsync(ct);
    }

    public Task BootstrapAsync(bool localOperatorAuthorized, Guid personId, string userName, string password, CancellationToken ct = default)
    {
        if (!localOperatorAuthorized) throw new AccountAccessException();
        if (personId == Guid.Empty) throw new AccountValidationException("Organizzatore iniziale non valido.");
        return store.BootstrapAsync(personId, UserName(userName), Password(password), ct);
    }

    public async Task CreateAsync(Guid actorId, Guid eventId, string userName, string displayName, string password, CancellationToken ct = default)
    {
        await AuthorizeAsync(actorId, ct);
        if (eventId == Guid.Empty) throw new AccountValidationException("Seleziona un evento.");
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > 150)
            throw new AccountValidationException("Il nome deve contenere da 1 a 150 caratteri.");
        await store.CreateAsync(actorId, eventId, new Person(displayName), UserName(userName), Password(password), ct);
    }

    public Task ChangePasswordAsync(Guid actorId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        if (actorId == Guid.Empty) throw new AccountAccessException();
        return store.ChangePasswordAsync(actorId, Password(currentPassword), Password(newPassword), ct);
    }

    public async Task<bool> SetAssignmentAsync(Guid actorId, Guid personId, Guid eventId, bool active, bool confirmed, CancellationToken ct = default)
    {
        await AuthorizeAsync(actorId, ct);
        if (personId == Guid.Empty || eventId == Guid.Empty) throw new AccountValidationException("Seleziona account ed evento validi.");
        if (!active && !confirmed) throw new AccountValidationException("Conferma esplicitamente la revoca dell'accesso all'evento.");
        return await store.SetAssignmentAsync(actorId, personId, eventId, active, ct);
    }

    public async Task<bool> SetEnabledAsync(Guid actorId, Guid personId, bool enabled, bool confirmed, CancellationToken ct = default)
    {
        await AuthorizeAsync(actorId, ct);
        if (personId == Guid.Empty) throw new AccountValidationException("Seleziona un account valido.");
        if (!enabled && !confirmed) throw new AccountValidationException("Conferma esplicitamente la disabilitazione dell'account.");
        return await store.SetEnabledAsync(actorId, personId, enabled, ct);
    }

    private async Task AuthorizeAsync(Guid actorId, CancellationToken ct)
    {
        if (actorId == Guid.Empty || !await store.IsAdministratorAsync(actorId, ct)) throw new AccountAccessException();
    }

    private static string UserName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > 100)
            throw new AccountValidationException("Il nome utente deve contenere da 1 a 100 caratteri.");
        return value.Trim();
    }

    private static string Password(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128)
            throw new AccountValidationException("La password è obbligatoria e non può superare 128 caratteri.");
        return value;
    }
}
