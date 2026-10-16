using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Accounts;
using TRobot.Application.Attendance;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class AccessLifecycleTests
{
    private const string UserName = "lifecycle-organizer";
    private const string Password = "Synthetic-Lifecycle-Password1!";

    [Fact]
    public async Task EventRevocationPreservesHistoryAndOtherEventsWithExistingCookie()
    {
        await using var factory = new LocalWebFactory();
        using var admin = Client(factory);
        var personId = await CreateNormal(factory);
        Guid otherEventId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var ev = new Event("Altro evento ancora autorizzato", DateTime.UtcNow);
            db.Add(ev);
            await db.SaveChangesAsync();
            otherEventId = ev.Id;
            await scope.ServiceProvider.GetRequiredService<AccountService>().SetAssignmentAsync(factory.OrganizerPersonId, personId, ev.Id, true, false);
        }
        await Login(admin, LocalWebFactory.UserName, LocalWebFactory.Password);
        using var normal = Client(factory);
        await Login(normal, UserName, Password);
        var dashboard = await normal.GetStringAsync("/?eventId=" + factory.EventId);
        var submitted = await normal.PostAsync("/Home/Simulate", Form(("Input.EventId", factory.EventId.ToString()),
            ("Input.SenderId", "synthetic-http-sender"), ("Input.MessageId", "before-revoke"), ("Input.Text", "!presenza si"),
            ("__RequestVerificationToken", Token(dashboard))));
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        var token = Token(await admin.GetStringAsync("/Organizers"));
        var missingCsrf = await admin.PostAsync("/Organizers/SetAssignment", Form(("PersonId", personId.ToString()), ("EventId", factory.EventId.ToString()), ("Active", "false"), ("Confirmed", "true")));
        Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
        var unconfirmed = await admin.PostAsync("/Organizers/SetAssignment", Assignment(personId, factory.EventId, false, false, token));
        Assert.Equal(HttpStatusCode.OK, unconfirmed.StatusCode);
        Assert.Contains("Conferma esplicitamente", await unconfirmed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await normal.GetAsync("/?eventId=" + factory.EventId)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Organizers/SetAssignment", Assignment(personId, factory.EventId, false, true, token))).StatusCode);
        var forbidden = await normal.GetAsync("/?eventId=" + factory.EventId);
        Assert.Contains("/Access/Denied", forbidden.Headers.Location!.ToString());
        var deniedWrite = await normal.PostAsync("/Home/Simulate", Form(("Input.EventId", factory.EventId.ToString()),
            ("Input.SenderId", "synthetic-http-sender"), ("Input.MessageId", "after-revoke"), ("Input.Text", "!presenza no"),
            ("__RequestVerificationToken", Token(dashboard))));
        Assert.Contains("/Access/Denied", deniedWrite.Headers.Location!.ToString());
        var remaining = await normal.GetStringAsync("/");
        Assert.Contains("Altro evento ancora autorizzato", remaining);
        Assert.DoesNotContain("Evento test HTTP", remaining);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Organizers/SetAssignment", Assignment(personId, factory.EventId, true, false, token))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await normal.GetAsync("/?eventId=" + factory.EventId)).StatusCode);
        using var verify = factory.Services.CreateScope();
        var context = verify.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(2, await context.EventOrganizers.CountAsync(x => x.PersonId == personId));
        Assert.Equal(1, await context.ParticipationReceipts.CountAsync());
        Assert.Equal(personId, (await context.ParticipationReceipts.SingleAsync()).OrganizerPersonId);
        Assert.Equal(ParticipationStatus.Confirmed, (await context.EventParticipants.SingleAsync()).Status);
        var audit = await context.AccountAudits.SingleAsync(x => x.Action == "EventRevoked");
        Assert.Equal(factory.EventId, audit.EventId);
        Assert.Equal(factory.OrganizerPersonId, audit.ActorId);
        Assert.Equal(4, await context.AccountAudits.CountAsync());
    }

    [Fact]
    public async Task AccountDisableBlocksLoginAndOldCookieRemainsInvalidAfterReactivation()
    {
        await using var factory = new LocalWebFactory();
        using var admin = Client(factory);
        var personId = await CreateNormal(factory);
        await Login(admin, LocalWebFactory.UserName, LocalWebFactory.Password);
        using var normal = Client(factory);
        var oldCookie = await Login(normal, UserName, Password);
        var token = Token(await admin.GetStringAsync("/Organizers"));
        var withoutConfirmation = await admin.PostAsync("/Organizers/SetEnabled", Enabled(personId, false, false, token));
        Assert.Equal(HttpStatusCode.OK, withoutConfirmation.StatusCode);
        Assert.Contains("Conferma esplicitamente", await withoutConfirmation.Content.ReadAsStringAsync());
        var noCsrf = await admin.PostAsync("/Organizers/SetEnabled", Form(("PersonId", personId.ToString()), ("Enabled", "false"), ("Confirmed", "true")));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Organizers/SetEnabled", Enabled(personId, false, true, token))).StatusCode);
        Assert.Contains("/Access/Login", (await normal.GetAsync("/")).Headers.Location!.ToString());
        var loginToken = Token(await normal.GetStringAsync("/Access/Login"));
        var blocked = await normal.PostAsync("/Access/Login", Form(("UserName", UserName), ("Password", Password), ("__RequestVerificationToken", loginToken)));
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        Assert.Contains("Credenziali non valide", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/Organizers/SetEnabled", Enabled(personId, true, false, token))).StatusCode);
        using var stale = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        using var staleRequest = new HttpRequestMessage(HttpMethod.Get, "/");
        staleRequest.Headers.Add("Cookie", oldCookie);
        Assert.Contains("/Access/Login", (await stale.SendAsync(staleRequest)).Headers.Location!.ToString());
        await Login(normal, UserName, Password);
        Assert.Equal(HttpStatusCode.OK, (await normal.GetAsync("/")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.True((await db.Users.SingleAsync(x => x.PersonId == personId)).IsEnabled);
        Assert.True((await db.EventOrganizers.SingleAsync(x => x.PersonId == personId)).IsActive);
        Assert.Equal(1, await db.AccountAudits.CountAsync(x => x.Action == "AccountDisabled"));
        Assert.Equal(1, await db.AccountAudits.CountAsync(x => x.Action == "AccountEnabled"));
    }

    [Fact]
    public async Task NormalOrganizerCannotManageAccessAndAdministratorCannotBeDisabled()
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        var personId = await CreateNormal(factory);
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        await Assert.ThrowsAsync<AccountAccessException>(() => accounts.SetAssignmentAsync(personId, personId, factory.EventId, false, true));
        await Assert.ThrowsAsync<AccountAccessException>(() => accounts.SetEnabledAsync(personId, personId, false, true));
        await Assert.ThrowsAsync<AccountValidationException>(() => accounts.SetEnabledAsync(factory.OrganizerPersonId, factory.OrganizerPersonId, false, true));
        await Login(client, UserName, Password);
        var token = Token(await client.GetStringAsync("/"));
        var forgedGrant = await client.PostAsync("/Organizers/SetAssignment", Assignment(personId, factory.EventId, true, true, token));
        var forgedDisable = await client.PostAsync("/Organizers/SetEnabled", Enabled(factory.OrganizerPersonId, false, true, token));
        Assert.Contains("/Access/Denied", forgedGrant.Headers.Location!.ToString());
        Assert.Contains("/Access/Denied", forgedDisable.Headers.Location!.ToString());
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(1, await db.AccountAudits.CountAsync());
        Assert.True((await db.Users.SingleAsync(x => x.IsSystemAdministrator)).IsEnabled);
    }

    [Theory]
    [InlineData("missing-person")]
    [InlineData("missing-event")]
    [InlineData("empty-person")]
    [InlineData("empty-event")]
    [InlineData("missing-account")]
    public async Task InvalidTargetsDoNotChangeAccessOrAudit(string invalid)
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        var personId = await CreateNormal(factory);
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        if (invalid == "missing-account")
            await Assert.ThrowsAsync<AccountValidationException>(() => accounts.SetEnabledAsync(factory.OrganizerPersonId, Guid.NewGuid(), false, true));
        else
            await Assert.ThrowsAsync<AccountValidationException>(() => accounts.SetAssignmentAsync(factory.OrganizerPersonId,
                invalid == "missing-person" ? Guid.NewGuid() : invalid == "empty-person" ? Guid.Empty : personId,
                invalid == "missing-event" ? Guid.NewGuid() : invalid == "empty-event" ? Guid.Empty : factory.EventId, true, false));
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(1, await db.AccountAudits.CountAsync());
        Assert.True((await db.EventOrganizers.SingleAsync(x => x.PersonId == personId)).IsActive);
    }

    [Fact]
    public async Task RepeatedChangesAreNoOpsAndRegrantReusesMembership()
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        var personId = await CreateNormal(factory);
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var createdAt = (await db.EventOrganizers.SingleAsync(x => x.PersonId == personId)).CreatedAtUtc;
        Assert.False(await accounts.SetAssignmentAsync(factory.OrganizerPersonId, personId, factory.EventId, true, false));
        Assert.False(await accounts.SetEnabledAsync(factory.OrganizerPersonId, personId, true, false));
        Assert.True(await accounts.SetAssignmentAsync(factory.OrganizerPersonId, personId, factory.EventId, false, true));
        Assert.False(await accounts.SetAssignmentAsync(factory.OrganizerPersonId, personId, factory.EventId, false, true));
        Assert.True(await accounts.SetAssignmentAsync(factory.OrganizerPersonId, personId, factory.EventId, true, false));
        Assert.False(await accounts.SetAssignmentAsync(factory.OrganizerPersonId, personId, factory.EventId, true, false));
        Assert.True(await accounts.SetEnabledAsync(factory.OrganizerPersonId, personId, false, true));
        Assert.False(await accounts.SetEnabledAsync(factory.OrganizerPersonId, personId, false, true));
        Assert.True(await accounts.SetEnabledAsync(factory.OrganizerPersonId, personId, true, false));
        Assert.False(await accounts.SetEnabledAsync(factory.OrganizerPersonId, personId, true, false));
        Assert.Equal(5, await db.AccountAudits.CountAsync());
        var membership = await db.EventOrganizers.SingleAsync(x => x.PersonId == personId);
        Assert.Equal(createdAt, membership.CreatedAtUtc);
        Assert.Equal(OrganizerRole.Organizer, membership.Role);
    }

    [Fact]
    public async Task DisabledAccountCannotChangePasswordReceiveAssignmentsOrUseAttendanceService()
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        var personId = await CreateNormal(factory);
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        await accounts.SetEnabledAsync(factory.OrganizerPersonId, personId, false, true);
        await Assert.ThrowsAsync<AccountValidationException>(() => accounts.SetAssignmentAsync(factory.OrganizerPersonId, personId, factory.EventId, true, false));
        await Assert.ThrowsAsync<AccountAccessException>(() => accounts.ChangePasswordAsync(personId, Password, "Synthetic-New-Password2!"));
        var attendance = scope.ServiceProvider.GetRequiredService<AttendanceService>();
        Assert.Empty(await attendance.GetEventsAsync(personId));
        await Assert.ThrowsAsync<AttendanceAccessException>(() => attendance.GetDashboardAsync(personId, factory.EventId));
        await Assert.ThrowsAsync<AttendanceAccessException>(() => attendance.SimulateAsync(personId, new(factory.EventId, "synthetic-http-sender", "disabled-message", "!presenza no")));
    }

    [Fact]
    public async Task AuditFailureRollsBackAccountSecurityStampAndEventRevocation()
    {
        var interceptor = new FailingAuditInterceptor();
        await using var factory = new LocalWebFactory { SaveInterceptor = interceptor };
        using var client = Client(factory);
        var personId = await CreateNormal(factory);
        using var scope = factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var originalStamp = (await db.Users.SingleAsync(x => x.PersonId == personId)).SecurityStamp;
        interceptor.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.SetEnabledAsync(factory.OrganizerPersonId, personId, false, true));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.SetAssignmentAsync(factory.OrganizerPersonId, personId, factory.EventId, false, true));
        using var verify = factory.Services.CreateScope();
        var otherDb = verify.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var user = await otherDb.Users.SingleAsync(x => x.PersonId == personId);
        Assert.True(user.IsEnabled);
        Assert.Equal(originalStamp, user.SecurityStamp);
        Assert.True((await otherDb.EventOrganizers.SingleAsync(x => x.PersonId == personId)).IsActive);
        Assert.Equal(1, await otherDb.AccountAudits.CountAsync());
        interceptor.Fail = false;
        Assert.True(await accounts.SetEnabledAsync(factory.OrganizerPersonId, personId, false, true));
    }

    private static async Task<Guid> CreateNormal(LocalWebFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AccountService>().CreateAsync(factory.OrganizerPersonId, factory.EventId, UserName, "Organizzatore lifecycle", Password);
        return await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Users.Where(x => x.UserName == UserName).Select(x => x.PersonId).SingleAsync();
    }
    private static HttpClient Client(LocalWebFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task<string> Login(HttpClient client, string userName, string password)
    {
        var token = Token(await client.GetStringAsync("/Access/Login"));
        var response = await client.PostAsync("/Access/Login", Form(("UserName", userName), ("Password", password), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith(".TRobot.Identity=")).Split(';')[0];
    }
    private static FormUrlEncodedContent Assignment(Guid personId, Guid eventId, bool active, bool confirmed, string token) => Form(
        ("PersonId", personId.ToString()), ("EventId", eventId.ToString()), ("Active", active.ToString()), ("Confirmed", confirmed.ToString()), ("__RequestVerificationToken", token));
    private static FormUrlEncodedContent Enabled(Guid personId, bool enabled, bool confirmed, string token) => Form(
        ("PersonId", personId.ToString()), ("Enabled", enabled.ToString()), ("Confirmed", confirmed.ToString()), ("__RequestVerificationToken", token));
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) => new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private sealed class FailingAuditInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Fail && eventData.Context!.ChangeTracker.Entries<AccountAudit>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Errore di test durante l'audit.");
            return ValueTask.FromResult(result);
        }
    }
}
