using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Accounts;
using TRobot.Domain;
using TRobot.Infrastructure.Identity;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class AccountTests
{
    private const string SecondUser = "second-organizer";
    private const string SecondPassword = "Synthetic-Second-Password2!";

    [Fact]
    public async Task AdministratorCreatesNormalAccountAndEventsRemainIsolated()
    {
        await using var factory = new LocalWebFactory();
        using var admin = Client(factory);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.GetAsync("/Organizers")).StatusCode);
        await Login(admin, LocalWebFactory.UserName, LocalWebFactory.Password);
        Guid otherEventId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var ev = new Event("Evento riservato al secondo", DateTime.UtcNow);
            var participant = new Person("Partecipante secondo", "synthetic-secondary");
            db.AddRange(ev, participant, new EventParticipant(ev.Id, participant.Id));
            await db.SaveChangesAsync();
            otherEventId = ev.Id;
        }
        var directory = await admin.GetStringAsync("/Organizers");
        var createValues = new (string, string)[]
        {
            ("Input.UserName", SecondUser), ("Input.DisplayName", "Secondo organizzatore"),
            ("Input.EventId", otherEventId.ToString()), ("Input.Password", SecondPassword), ("Input.ConfirmPassword", SecondPassword),
            ("Input.IsSystemAdministrator", "true") // Un campo non previsto non può promuovere il nuovo account.
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/Organizers/Create", Form(createValues))).StatusCode);
        var created = await admin.PostAsync("/Organizers/Create", Form(createValues.Append(("__RequestVerificationToken", Token(directory))).ToArray()));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var after = await admin.GetStringAsync("/Organizers");
        Assert.Contains(SecondUser, after);
        Assert.DoesNotContain(SecondPassword, after);
        using var organizer = Client(factory);
        await Login(organizer, SecondUser, SecondPassword);
        var ownDashboard = await organizer.GetStringAsync("/");
        Assert.Contains("Evento riservato al secondo", ownDashboard);
        Assert.DoesNotContain("Evento test HTTP", ownDashboard);
        Assert.Equal(HttpStatusCode.Redirect, (await organizer.GetAsync("/Organizers")).StatusCode);
        Assert.Contains("/Access/Denied", (await organizer.GetAsync("/Organizers")).Headers.Location!.ToString());
        var forged = await organizer.PostAsync("/Organizers/Create", Form(createValues.Append(("__RequestVerificationToken", Token(ownDashboard))).ToArray()));
        Assert.Equal(HttpStatusCode.Redirect, forged.StatusCode);
        Assert.Contains("/Access/Denied", forged.Headers.Location!.ToString());
        Assert.Contains("/Access/Denied", (await organizer.GetAsync("/?eventId=" + factory.EventId)).Headers.Location!.ToString());
        Assert.Contains("/Access/Denied", (await admin.GetAsync("/?eventId=" + otherEventId)).Headers.Location!.ToString());
        var simulated = await organizer.PostAsync("/Home/Simulate", Form(
            ("Input.EventId", otherEventId.ToString()), ("Input.SenderId", "synthetic-secondary"), ("Input.MessageId", "second-account-message"),
            ("Input.Text", "!presenza forse"), ("__RequestVerificationToken", Token(ownDashboard))));
        Assert.Equal(HttpStatusCode.Redirect, simulated.StatusCode);
        using var verify = factory.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var user = await context.Users.SingleAsync(x => x.UserName == SecondUser);
        Assert.False(user.IsSystemAdministrator);
        Assert.NotEqual(user.Id, user.PersonId);
        Assert.Equal(2, await context.Users.CountAsync());
        Assert.Single(await context.EventOrganizers.Where(x => x.PersonId == user.PersonId).ToListAsync());
        Assert.False(await context.EventParticipants.AnyAsync(x => x.PersonId == user.PersonId));
        Assert.Equal(user.PersonId, (await context.ParticipationReceipts.SingleAsync()).OrganizerPersonId);
        var audit = await context.AccountAudits.SingleAsync();
        Assert.Equal("OrganizerCreated", audit.Action);
        Assert.Equal(factory.OrganizerPersonId, audit.ActorId);
        Assert.Equal(user.PersonId, audit.PersonId);
        Assert.DoesNotContain(user.PasswordHash!, after);
    }

    [Fact]
    public async Task InvalidAndDuplicateAccountsDoNotLeaveOrphanPeopleOrAssignments()
    {
        await using var factory = new LocalWebFactory();
        using var unusedClient = Client(factory);
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        await Assert.ThrowsAsync<AccountAccessException>(() => accounts.CreateAsync(Guid.NewGuid(), factory.EventId, SecondUser, "Nome", SecondPassword));
        await Assert.ThrowsAsync<AccountValidationException>(() => accounts.CreateAsync(factory.OrganizerPersonId, factory.EventId, SecondUser, "Nome", "weak"));
        await Assert.ThrowsAsync<AccountValidationException>(() => accounts.CreateAsync(factory.OrganizerPersonId, Guid.NewGuid(), SecondUser, "Nome", SecondPassword));
        Assert.Equal(2, await db.People.CountAsync());
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(1, await db.EventOrganizers.CountAsync());
        Assert.Empty(await db.AccountAudits.ToListAsync());
        await accounts.CreateAsync(factory.OrganizerPersonId, factory.EventId, SecondUser, "Nome", SecondPassword);
        await Assert.ThrowsAsync<AccountValidationException>(() => accounts.CreateAsync(factory.OrganizerPersonId, factory.EventId, SecondUser.ToUpperInvariant(), "Duplicato", SecondPassword));
        Assert.Equal(3, await db.People.CountAsync());
        Assert.Equal(2, await db.Users.CountAsync());
        Assert.Equal(2, await db.EventOrganizers.CountAsync());
        Assert.Single(await db.AccountAudits.ToListAsync());
    }

    [Fact]
    public async Task FailureAfterUserCreationRollsBackUserPersonMembershipAndAudit()
    {
        await using var factory = new LocalWebFactory { SaveInterceptor = new FailAccountAuditInterceptor() };
        using var client = Client(factory);
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.CreateAsync(factory.OrganizerPersonId, factory.EventId, SecondUser, "Test rollback", SecondPassword));
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(1, await db.Users.CountAsync());
        Assert.Equal(2, await db.People.CountAsync());
        Assert.Equal(1, await db.EventOrganizers.CountAsync());
        Assert.Empty(await db.AccountAudits.ToListAsync());
    }

    [Fact]
    public async Task PasswordChangeInvalidatesOtherSessionsAndPreservesCurrentOne()
    {
        await using var factory = new LocalWebFactory();
        using var current = Client(factory);
        using var otherSession = Client(factory);
        await Login(current, LocalWebFactory.UserName, LocalWebFactory.Password);
        await Login(otherSession, LocalWebFactory.UserName, LocalWebFactory.Password);
        var token = Token(await current.GetStringAsync("/Access/ChangePassword"));
        var missingCsrf = await current.PostAsync("/Access/ChangePassword", Form(("CurrentPassword", LocalWebFactory.Password), ("NewPassword", SecondPassword), ("ConfirmPassword", SecondPassword)));
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        var wrong = await current.PostAsync("/Access/ChangePassword", Form(("CurrentPassword", "Wrong-current-password1!"),
            ("NewPassword", SecondPassword), ("ConfirmPassword", SecondPassword), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        var wrongHtml = await wrong.Content.ReadAsStringAsync();
        Assert.Contains("Password attuale non valida", wrongHtml);
        Assert.DoesNotContain(SecondPassword, wrongHtml);
        var changed = await current.PostAsync("/Access/ChangePassword", Form(("CurrentPassword", LocalWebFactory.Password),
            ("NewPassword", SecondPassword), ("ConfirmPassword", SecondPassword), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/")).StatusCode);
        var revoked = await otherSession.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode);
        Assert.Contains("/Access/Login", revoked.Headers.Location!.ToString());
        using var fresh = Client(factory);
        var loginToken = Token(await fresh.GetStringAsync("/Access/Login"));
        var oldPassword = await fresh.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", loginToken)));
        Assert.Equal(HttpStatusCode.OK, oldPassword.StatusCode);
        await Login(fresh, LocalWebFactory.UserName, SecondPassword);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal("PasswordChanged", (await db.AccountAudits.SingleAsync()).Action);
    }

    [Fact]
    public async Task IdentityLocksAccountAfterFiveFailedPasswordChecks()
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OrganizerUser>>();
        var signIn = scope.ServiceProvider.GetRequiredService<SignInManager<OrganizerUser>>();
        var user = (await users.FindByNameAsync(LocalWebFactory.UserName))!;
        for (var i = 0; i < 5; i++) await signIn.CheckPasswordSignInAsync(user, "Wrong-password1!", lockoutOnFailure: true);
        Assert.True(await users.IsLockedOutAsync(user));
        Assert.True((await signIn.CheckPasswordSignInAsync(user, LocalWebFactory.Password, lockoutOnFailure: true)).IsLockedOut);
        Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(14));
    }

    [Fact]
    public async Task BootstrapRequiresLocalAuthorizationAndDoesNotReplaceExistingAccount()
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        await Assert.ThrowsAsync<AccountAccessException>(() => accounts.BootstrapAsync(false, factory.OrganizerPersonId, "admin", SecondPassword));
        await Assert.ThrowsAsync<AccountValidationException>(() => accounts.BootstrapAsync(true, factory.OrganizerPersonId, "admin", SecondPassword));
        Assert.Equal(LocalWebFactory.UserName, (await db.Users.SingleAsync()).UserName);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OrganizerUser>>();
        Assert.True((await users.DeleteAsync(await db.Users.SingleAsync())).Succeeded);
        await accounts.BootstrapAsync(true, factory.OrganizerPersonId, "admin", SecondPassword);
        Assert.True((await db.Users.SingleAsync()).IsSystemAdministrator);
        Assert.Equal("AdministratorInitialized", (await db.AccountAudits.SingleAsync()).Action);
        Assert.Null((await db.AccountAudits.SingleAsync()).ActorId);
    }

    private static HttpClient Client(LocalWebFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task Login(HttpClient client, string username, string password)
    {
        var token = Token(await client.GetStringAsync("/Access/Login"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Access/Login", Form(("UserName", username), ("Password", password), ("__RequestVerificationToken", token)))).StatusCode);
    }
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) => new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private sealed class FailAccountAuditInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AccountAudit>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Errore di test prima dell'audit, dopo la creazione Identity.");
            return ValueTask.FromResult(result);
        }
    }
}
