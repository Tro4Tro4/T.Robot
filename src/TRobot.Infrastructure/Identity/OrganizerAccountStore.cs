using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TRobot.Application.Accounts;
using TRobot.Application.Attendance;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Infrastructure.Identity;

public sealed class OrganizerAccountStore(TRobotDbContext db, UserManager<OrganizerUser> users, TimeProvider clock) : IOrganizerAccountStore
{
    public Task<bool> IsAdministratorAsync(Guid actorId, CancellationToken ct) =>
        db.Users.AnyAsync(x => x.PersonId == actorId && x.IsSystemAdministrator && x.IsEnabled, ct);

    public async Task<AccountDirectory> GetDirectoryAsync(CancellationToken ct)
    {
        var rows = await (from user in db.Users.AsNoTracking()
                              join person in db.People.AsNoTracking() on user.PersonId equals person.Id
                              orderby user.UserName
                              select new { user.PersonId, user.UserName, person.DisplayName, user.IsSystemAdministrator, user.IsEnabled }).ToListAsync(ct);
        var assignments = await (from organizer in db.EventOrganizers.AsNoTracking()
                                 join ev in db.Events.AsNoTracking() on organizer.EventId equals ev.Id
                                 where organizer.IsActive
                                 orderby ev.StartsAtUtc
                                 select new { organizer.PersonId, ev.Id, ev.Name }).ToListAsync(ct);
        var accounts = rows.Select(row => new AccountSummary(row.PersonId, row.UserName!, row.DisplayName,
            row.IsSystemAdministrator, row.IsEnabled, assignments.Where(x => x.PersonId == row.PersonId)
                .Select(x => new EventOption(x.Id, x.Name)).ToList())).ToList();
        var events = await db.Events.OrderBy(x => x.StartsAtUtc).Select(x => new EventOption(x.Id, x.Name)).ToListAsync(ct);
        return new(accounts, events);
    }

    public Task BootstrapAsync(Guid personId, string userName, string password, CancellationToken ct) => InTransactionAsync(async () =>
    {
        if (await db.Users.AnyAsync(ct)) throw new AccountValidationException("Esistono già account. L'amministratore iniziale non è stato modificato.");
        if (!await db.EventOrganizers.AnyAsync(x => x.PersonId == personId && x.IsActive, ct))
            throw new AccountValidationException("La persona iniziale deve essere già un organizzatore.");
        Ensure(await users.CreateAsync(NewUser(personId, userName, true), password));
        db.AccountAudits.Add(new(personId, null, "AdministratorInitialized", clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }, ct);

    public Task CreateAsync(Guid actorId, Guid eventId, Person person, string userName, string password, CancellationToken ct) => InTransactionAsync(async () =>
    {
        if (!await IsAdministratorAsync(actorId, ct)) throw new AccountAccessException();
        if (!await db.Events.AnyAsync(x => x.Id == eventId, ct)) throw new AccountValidationException("Evento non valido.");
        db.AddRange(person, new EventOrganizer(eventId, person.Id));
        Ensure(await users.CreateAsync(NewUser(person.Id, userName, false), password));
        db.AccountAudits.Add(new(person.Id, actorId, "OrganizerCreated", clock.GetUtcNow().UtcDateTime, eventId));
        await db.SaveChangesAsync(ct);
    }, ct);

    public Task ChangePasswordAsync(Guid actorId, string currentPassword, string newPassword, CancellationToken ct) => InTransactionAsync(async () =>
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.PersonId == actorId, ct) ?? throw new AccountAccessException();
        if (!user.IsEnabled) throw new AccountAccessException();
        if (await users.IsLockedOutAsync(user)) throw new AccountValidationException("Account temporaneamente bloccato. Riprova più tardi.");
        Ensure(await users.ChangePasswordAsync(user, currentPassword, newPassword));
        db.AccountAudits.Add(new(actorId, actorId, "PasswordChanged", clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
    }, ct);

    public Task<bool> SetAssignmentAsync(Guid actorId, Guid personId, Guid eventId, bool active, CancellationToken ct) => InTransactionAsync(async () =>
    {
        if (!await IsAdministratorAsync(actorId, ct)) throw new AccountAccessException();
        var user = await db.Users.SingleOrDefaultAsync(x => x.PersonId == personId, ct)
            ?? throw new AccountValidationException("Account non trovato.");
        if (!await db.Events.AnyAsync(x => x.Id == eventId, ct)) throw new AccountValidationException("Evento non trovato.");
        if (active && !user.IsEnabled) throw new AccountValidationException("Riattiva l'account prima di assegnare un evento.");
        var assignment = await db.EventOrganizers.SingleOrDefaultAsync(x => x.PersonId == personId && x.EventId == eventId, ct);
        if (assignment is null)
        {
            if (!active) return false;
            db.EventOrganizers.Add(new(eventId, personId));
        }
        else if (!assignment.SetActive(active)) return false;
        db.AccountAudits.Add(new(personId, actorId, active ? "EventGranted" : "EventRevoked", clock.GetUtcNow().UtcDateTime, eventId));
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    public Task<bool> SetEnabledAsync(Guid actorId, Guid personId, bool enabled, CancellationToken ct) => InTransactionAsync(async () =>
    {
        if (!await IsAdministratorAsync(actorId, ct)) throw new AccountAccessException();
        var user = await db.Users.SingleOrDefaultAsync(x => x.PersonId == personId, ct)
            ?? throw new AccountValidationException("Account non trovato.");
        if (!enabled && user.IsSystemAdministrator)
            throw new AccountValidationException("L'amministratore globale non può essere disabilitato da questa pagina.");
        if (user.IsEnabled == enabled) return false;
        user.IsEnabled = enabled;
        Ensure(await users.UpdateSecurityStampAsync(user));
        db.AccountAudits.Add(new(personId, actorId, enabled ? "AccountEnabled" : "AccountDisabled", clock.GetUtcNow().UtcDateTime));
        await db.SaveChangesAsync(ct);
        return true;
    }, ct);

    private static OrganizerUser NewUser(Guid personId, string userName, bool administrator) => new()
    {
        Id = Guid.NewGuid(), PersonId = personId, UserName = userName,
        IsSystemAdministrator = administrator, LockoutEnabled = true
    };

    private static void Ensure(IdentityResult result)
    {
        if (result.Succeeded) return;
        var message = result.Errors.Any(x => x.Code == "PasswordMismatch") ? "Password attuale non valida."
            : result.Errors.Any(x => x.Code == "DuplicateUserName") ? "Nome utente già in uso."
            : "Dati account non validi. Usa un nome utente semplice e una password di almeno 12 caratteri con maiuscole, minuscole, cifre e simboli.";
        throw new AccountValidationException(message);
    }

    private Task InTransactionAsync(Func<Task> action, CancellationToken ct) =>
        InTransactionAsync(async () => { await action(); return true; }, ct);

    private async Task<T> InTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await action();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (ex is DbUpdateException || ex is SqliteException { SqliteErrorCode: 5 or 6 or 19 })
        {
            db.ChangeTracker.Clear();
            throw new AccountValidationException("Operazione concorrente o dati account già presenti. Aggiorna la pagina e riprova.");
        }
        catch { db.ChangeTracker.Clear(); throw; }
    }
}
