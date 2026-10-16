using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Accounts;
using TRobot.Application.Attendance;
using TRobot.Application.Events;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class ParticipantNameTests
{
    [Fact]
    public async Task HttpRenamePreservesIdentityStatusReceiptsAndRejectsCsrfOverpostingAndStaleRevision()
    {
        await using var factory = new LocalWebFactory();
        Guid personId;
        DateTime? lastStatusChange;
        using (var scope = factory.Services.CreateScope())
        {
            var attendance = scope.ServiceProvider.GetRequiredService<AttendanceService>();
            await attendance.SimulateAsync(factory.OrganizerPersonId, new(factory.EventId, "synthetic-http-sender", "before-rename", "!presenza si"));
            var member = await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().EventParticipants.SingleAsync();
            personId = member.PersonId;
            lastStatusChange = member.UpdatedAtUtc;
        }
        using var client = Client(factory);
        var url = $"/Events/EditParticipant/{factory.EventId}?personId={personId}";
        Assert.Contains("/Access/Login", (await client.GetAsync(url)).Headers.Location!.ToString());
        await Login(client);
        var details = await client.GetStringAsync("/Events/Details/" + factory.EventId);
        Assert.Contains("Correggi nome", details);
        var edit = await client.GetStringAsync(url);
        Assert.Contains("value=\"0\"", edit);
        Assert.DoesNotContain("Input.ConfirmedShared", edit);
        var token = Token(edit);
        var updateUrl = "/Events/UpdateParticipant/" + factory.EventId;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(updateUrl, RenameForm(personId, "Nome corretto test", 0))).StatusCode);
        var missingRevision = await client.PostAsync(updateUrl, Form(("Input.PersonId", personId.ToString()),
            ("Input.DisplayName", "Nome senza revisione"), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.OK, missingRevision.StatusCode);
        Assert.Contains("Revision", await missingRevision.Content.ReadAsStringAsync());
        var updated = await client.PostAsync(updateUrl, RenameForm(personId, " Nome corretto test ", 0, token));
        Assert.Equal(HttpStatusCode.Redirect, updated.StatusCode);
        Assert.Contains("Nome corretto test", await client.GetStringAsync(updated.Headers.Location));
        var dashboard = await client.GetStringAsync("/");
        Assert.Contains("Nome corretto test", dashboard);
        Assert.Contains("id=\"confirmed-count\">1", dashboard);
        var unchanged = await client.PostAsync(updateUrl, RenameForm(personId, "Nome corretto test", 1, token));
        Assert.Equal(HttpStatusCode.Redirect, unchanged.StatusCode);
        var stale = await client.PostAsync(updateUrl, RenameForm(personId, "Nome obsoleto", 0, token));
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Contains("Ricarica la pagina", await stale.Content.ReadAsStringAsync());
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var person = await db.People.SingleAsync(x => x.Id == personId);
        Assert.Equal("Nome corretto test", person.DisplayName);
        Assert.Equal("synthetic-http-sender", person.WhatsAppSenderId);
        Assert.Equal(1, person.Revision);
        var memberAfter = await db.EventParticipants.SingleAsync();
        Assert.Equal(lastStatusChange, memberAfter.UpdatedAtUtc);
        Assert.Equal(ParticipationStatus.Confirmed, memberAfter.Status);
        var receipt = await db.ParticipationReceipts.SingleAsync();
        Assert.Equal(personId, receipt.PersonId);
        Assert.Equal("before-rename", receipt.MessageId);
        var audit = await db.EventAudits.SingleAsync();
        Assert.Equal(EventAuditAction.ParticipantRenamed, audit.Action);
        Assert.Equal(factory.OrganizerPersonId, audit.ActorId);
        Assert.Equal(factory.EventId, audit.EventId);
        Assert.Equal(personId, audit.PersonId);
        Assert.Null(audit.RegistrationFingerprint);
        Assert.Equal(1, await db.Users.CountAsync());
        var replay = await verify.ServiceProvider.GetRequiredService<AttendanceService>().SimulateAsync(factory.OrganizerPersonId,
            new(factory.EventId, "synthetic-http-sender", "before-rename", "!presenza si"));
        Assert.True(replay.Duplicate);
    }

    [Fact]
    public async Task SharedPersonRequiresEveryEventAndConfirmationThenAuditsBothEvents()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var person = await db.People.SingleAsync(x => x.WhatsAppSenderId == "synthetic-http-sender");
        var otherEvent = new Event("Evento condiviso test", DateTime.UtcNow);
        db.AddRange(otherEvent, new EventParticipant(otherEvent.Id, person.Id));
        await db.SaveChangesAsync();
        var names = scope.ServiceProvider.GetRequiredService<ParticipantNameService>();
        await Assert.ThrowsAsync<EventAccessException>(() => names.GetAsync(factory.OrganizerPersonId, factory.EventId, person.Id));
        await Assert.ThrowsAsync<EventAccessException>(() => names.RenameAsync(factory.OrganizerPersonId, factory.EventId, person.Id, 0, "Negato", true));
        using var client = Client(factory);
        await Login(client);
        var details = await client.GetStringAsync("/Events/Details/" + factory.EventId);
        Assert.DoesNotContain("Correggi nome", details);
        Assert.DoesNotContain("Evento condiviso test", details);
        AssertDenied(await client.GetAsync($"/Events/EditParticipant/{factory.EventId}?personId={person.Id}"));
        await scope.ServiceProvider.GetRequiredService<AccountService>().SetAssignmentAsync(factory.OrganizerPersonId,
            factory.OrganizerPersonId, otherEvent.Id, true, false);
        var editor = await names.GetAsync(factory.OrganizerPersonId, factory.EventId, person.Id);
        Assert.True(editor.IsShared);
        var edit = await client.GetStringAsync($"/Events/EditParticipant/{factory.EventId}?personId={person.Id}");
        Assert.Contains("Input.ConfirmedShared", edit);
        var token = Token(edit);
        var unconfirmed = await client.PostAsync("/Events/UpdateParticipant/" + factory.EventId, RenameForm(person.Id, "Condiviso corretto", 0, token));
        Assert.Equal(HttpStatusCode.OK, unconfirmed.StatusCode);
        Assert.Contains("Conferma la correzione", await unconfirmed.Content.ReadAsStringAsync());
        Assert.Empty(await db.EventAudits.ToListAsync());
        var changed = await client.PostAsync("/Events/UpdateParticipant/" + factory.EventId, RenameForm(person.Id, "Condiviso corretto", 0, token, true));
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        db.ChangeTracker.Clear();
        Assert.Equal("Condiviso corretto", (await db.People.SingleAsync(x => x.Id == person.Id)).DisplayName);
        Assert.Equal(1, (await db.People.SingleAsync(x => x.Id == person.Id)).Revision);
        var audits = await db.EventAudits.ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.Contains(audits, x => x.EventId == factory.EventId);
        Assert.Contains(audits, x => x.EventId == otherEvent.Id);
        Assert.All(audits, x => Assert.Equal(EventAuditAction.ParticipantRenamed, x.Action));
        Assert.Single(audits.Select(x => x.OccurredAtUtc).Distinct());
        Assert.False(await names.RenameAsync(factory.OrganizerPersonId, factory.EventId, person.Id, 1, " Condiviso corretto ", false));
        Assert.Equal(2, await db.EventAudits.CountAsync());
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("revoked")]
    [InlineData("other-event")]
    [InlineData("other-person")]
    [InlineData("missing-event")]
    [InlineData("missing-person")]
    [InlineData("empty-actor")]
    public async Task InvalidPermissionsOrTargetsCannotRename(string scenario)
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var personId = (await db.EventParticipants.SingleAsync()).PersonId;
        var actorId = factory.OrganizerPersonId;
        var eventId = factory.EventId;
        switch (scenario)
        {
            case "disabled": await db.Users.ExecuteUpdateAsync(x => x.SetProperty(u => u.IsEnabled, false)); break;
            case "revoked": await db.EventOrganizers.ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false)); break;
            case "other-event": eventId = Guid.NewGuid(); db.Add(new Event("Altro", DateTime.UtcNow)); await db.SaveChangesAsync(); break;
            case "other-person":
                var other = new Person("Persona fuori evento"); db.Add(other); await db.SaveChangesAsync(); personId = other.Id; break;
            case "missing-event": eventId = Guid.Empty; break;
            case "missing-person": personId = Guid.Empty; break;
            default: actorId = Guid.Empty; break;
        }
        var service = scope.ServiceProvider.GetRequiredService<ParticipantNameService>();
        await Assert.ThrowsAsync<EventAccessException>(() => service.GetAsync(actorId, eventId, personId));
        await Assert.ThrowsAsync<EventAccessException>(() => service.RenameAsync(actorId, eventId, personId, 0, "Non autorizzato", true));
        Assert.Empty(await db.EventAudits.ToListAsync());
        Assert.Equal("Partecipante test HTTP", (await db.People.SingleAsync(x => x.WhatsAppSenderId == "synthetic-http-sender")).DisplayName);
    }

    [Fact]
    public async Task NormalOrganizerCanRenameButLosesPermissionOnOtherSharedEventRevocation()
    {
        await using var factory = new LocalWebFactory();
        Guid normalId, personId;
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AccountService>().CreateAsync(factory.OrganizerPersonId, factory.EventId,
                "name-editor", "Organizzatore correzioni", LocalWebFactory.Password);
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            normalId = (await db.Users.SingleAsync(x => x.UserName == "name-editor")).PersonId;
            personId = (await db.EventParticipants.SingleAsync()).PersonId;
        }
        using var client = Client(factory);
        await Login(client, "name-editor");
        var token = Token(await client.GetStringAsync($"/Events/EditParticipant/{factory.EventId}?personId={personId}"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Events/UpdateParticipant/" + factory.EventId,
            RenameForm(personId, "Corretto dall'organizzatore", 0, token))).StatusCode);
        using var verify = factory.Services.CreateScope();
        var dbVerify = verify.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var second = new Event("Secondo evento test", DateTime.UtcNow);
        dbVerify.AddRange(second, new EventParticipant(second.Id, personId), new EventOrganizer(second.Id, normalId));
        await dbVerify.SaveChangesAsync();
        await verify.ServiceProvider.GetRequiredService<AccountService>().SetAssignmentAsync(factory.OrganizerPersonId, normalId, second.Id, false, true);
        AssertDenied(await client.PostAsync("/Events/UpdateParticipant/" + factory.EventId, RenameForm(personId, "Non autorizzato", 1, token, true)));
        dbVerify.ChangeTracker.Clear();
        Assert.Equal("Corretto dall'organizzatore", (await dbVerify.People.SingleAsync(x => x.Id == personId)).DisplayName);
        Assert.Equal(1, await dbVerify.EventAudits.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrganizerAndAccountIdentitiesCannotBeRenamedThroughParticipantForm(bool hasAccount)
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var target = new Person(hasAccount ? "Account senza assegnazioni" : "Organizzatore storico");
        db.Add(target);
        if (!hasAccount)
        {
            var role = new EventOrganizer(factory.EventId, target.Id);
            role.SetActive(false);
            db.Add(role);
        }
        db.Add(new EventParticipant(factory.EventId, target.Id));
        await db.SaveChangesAsync();
        if (hasAccount)
        {
            // No event role: this checks the account guard independently of organizer memberships.
            db.Users.Add(new TRobot.Infrastructure.Identity.OrganizerUser
            {
                Id = Guid.NewGuid(), PersonId = target.Id, UserName = "target-with-account", IsEnabled = true
            });
            await db.SaveChangesAsync();
        }
        var names = scope.ServiceProvider.GetRequiredService<ParticipantNameService>();
        await Assert.ThrowsAsync<EventAccessException>(() => names.GetAsync(factory.OrganizerPersonId, factory.EventId, target.Id));
        await Assert.ThrowsAsync<EventAccessException>(() => names.RenameAsync(factory.OrganizerPersonId, factory.EventId, target.Id, 0, "Account alterato", true));
        Assert.Empty(await db.EventAudits.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalRegistrationReplaySurvivesRenameAndRejectsChangedPayload(bool legacyRegistration)
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<EventManagementService>();
        var names = scope.ServiceProvider.GetRequiredService<ParticipantNameService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var operation = Guid.NewGuid();
        var personId = await events.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, operation, "Nome originale", true);
        if (legacyRegistration)
            await db.EventAudits.Where(x => x.Id == operation).ExecuteUpdateAsync(x => x.SetProperty(a => a.RegistrationFingerprint, (string?)null));
        Assert.True(await names.RenameAsync(factory.OrganizerPersonId, factory.EventId, personId, 0, "Nome corretto", false));
        Assert.Equal(personId, await events.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, operation, " Nome originale ", true));
        await Assert.ThrowsAsync<EventConflictException>(() => events.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, operation, "Nome corretto", true));
        await Assert.ThrowsAsync<EventConflictException>(() => events.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, operation, "Nome originale", false));
        Assert.True(await names.RenameAsync(factory.OrganizerPersonId, factory.EventId, personId, 1, "Seconda correzione", false));
        Assert.Equal(personId, await events.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, operation, "Nome originale", true));
        Assert.Equal(3, await db.EventAudits.CountAsync());
        Assert.Equal(2, await db.EventParticipants.CountAsync());
        Assert.Equal("Seconda correzione", (await db.People.SingleAsync(x => x.Id == personId)).DisplayName);
        Assert.Equal(64, (await db.EventAudits.SingleAsync(x => x.Id == operation)).RegistrationFingerprint!.Length);
    }

    [Fact]
    public async Task FailureAfterSqlWriteRollsBackNameRevisionFingerprintAndAllSharedAudits()
    {
        var interceptor = new FailAfterRenameSave();
        await using var factory = new LocalWebFactory { SaveInterceptor = interceptor };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var events = scope.ServiceProvider.GetRequiredService<EventManagementService>();
        var personId = await events.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, Guid.NewGuid(), "Nome atomico", false);
        await db.EventAudits.ExecuteUpdateAsync(x => x.SetProperty(a => a.RegistrationFingerprint, (string?)null));
        var second = new Event("Condivisione atomica", DateTime.UtcNow);
        db.AddRange(second, new EventParticipant(second.Id, personId), new EventOrganizer(second.Id, factory.OrganizerPersonId));
        await db.SaveChangesAsync();
        var names = scope.ServiceProvider.GetRequiredService<ParticipantNameService>();
        interceptor.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => names.RenameAsync(factory.OrganizerPersonId, factory.EventId, personId, 0, "Non salvato", true));
        Assert.Equal("Nome atomico", (await db.People.SingleAsync(x => x.Id == personId)).DisplayName);
        Assert.Equal(0, (await db.People.SingleAsync(x => x.Id == personId)).Revision);
        Assert.Null((await db.EventAudits.SingleAsync()).RegistrationFingerprint);
        Assert.Equal(1, await db.EventAudits.CountAsync());
        interceptor.Armed = false;
        Assert.True(await names.RenameAsync(factory.OrganizerPersonId, factory.EventId, personId, 0, "Salvato dopo retry", true));
        Assert.Equal(3, await db.EventAudits.CountAsync());
        Assert.NotNull((await db.EventAudits.SingleAsync(x => x.Action == EventAuditAction.ParticipantAdded)).RegistrationFingerprint);
    }

    [Fact]
    public async Task EfRevisionRejectsActualConcurrentContextUpdates()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var options = new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(db.Database.GetDbConnection()).Options;
        await using var other = new TRobotDbContext(options);
        var person = await db.People.SingleAsync(x => x.WhatsAppSenderId == "synthetic-http-sender");
        var stale = await other.People.SingleAsync(x => x.Id == person.Id);
        person.Rename("Prima modifica");
        await db.SaveChangesAsync();
        stale.Rename("Modifica concorrente");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal("Prima modifica", (await db.People.SingleAsync(x => x.Id == person.Id)).DisplayName);
    }

    [Fact]
    public async Task UpgradeFromMilestoneSixPreservesLegacyRegistrationAndSupportsRenameAndReplay()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261016124840_EventManagement");
        var ev = new Event("Prima della correzione", DateTime.UtcNow);
        var actor = new Person("Organizzatore prima della correzione");
        var participant = new Person("Nome precedente test", "synthetic-before-name-edit");
        await MigrationUpgradeTests.InsertLegacyPerson(db, actor);
        await MigrationUpgradeTests.InsertLegacyPerson(db, participant);
        db.AddRange(ev, new EventOrganizer(ev.Id, actor.Id), new EventParticipant(ev.Id, participant.Id));
        await db.SaveChangesAsync();
        var operation = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO EventAudits (Id, EventId, ActorId, PersonId, Action, OccurredAtUtc) VALUES ({operation}, {ev.Id}, {actor.Id}, {participant.Id}, 'ParticipantAdded', {now})");
        var userId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO AspNetUsers (Id, PersonId, IsEnabled, IsSystemAdministrator, EmailConfirmed, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount) VALUES ({userId}, {actor.Id}, 1, 1, 0, 0, 0, 0, 0)");
        await MigrationUpgradeTests.InsertLegacyReceipt(db, new ParticipationReceipt(ev.Id, participant.Id, actor.Id, "before-name-edit", ParticipationStatus.NoResponse, ParticipationStatus.NoResponse, now));
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(0, (await db.People.SingleAsync(x => x.Id == participant.Id)).Revision);
        var originalAudit = await db.EventAudits.SingleAsync();
        Assert.Equal(operation, originalAudit.Id);
        Assert.Equal(now, originalAudit.OccurredAtUtc);
        Assert.Null(originalAudit.RegistrationFingerprint);
        var store = new EventManagementStore(db);
        Assert.True(await new ParticipantNameService(store, TimeProvider.System).RenameAsync(actor.Id, ev.Id, participant.Id, 0, "Nome aggiornato test", false));
        Assert.Equal(participant.Id, await new EventManagementService(store, TimeProvider.System).AddParticipantAsync(actor.Id, ev.Id, operation, "Nome precedente test", true));
        Assert.Equal("before-name-edit", (await db.ParticipationReceipts.SingleAsync()).MessageId);
        Assert.Equal(2, await db.EventAudits.CountAsync());
        Assert.Equal(2, await db.People.CountAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task InvalidNamesDoNotMutatePerson(string? name)
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var personId = (await db.EventParticipants.SingleAsync()).PersonId;
        await Assert.ThrowsAsync<EventValidationException>(() => scope.ServiceProvider.GetRequiredService<ParticipantNameService>()
            .RenameAsync(factory.OrganizerPersonId, factory.EventId, personId, 0, name!, false));
        Assert.Empty(await db.EventAudits.ToListAsync());
        Assert.Equal(0, (await db.People.SingleAsync(x => x.Id == personId)).Revision);
    }

    [Fact]
    public void DomainRenameAndAuditFingerprintKeepTheirInvariants()
    {
        var person = new Person(" Nome iniziale ", "synthetic-identity");
        var originalId = person.Id;
        Assert.False(person.Rename("Nome iniziale"));
        Assert.Throws<ArgumentOutOfRangeException>(() => person.Rename(new string('a', 151)));
        Assert.Equal("Nome iniziale", person.DisplayName);
        Assert.Equal(0, person.Revision);
        Assert.True(person.Rename(" Nome corretto "));
        Assert.Equal(1, person.Revision);
        Assert.Equal(originalId, person.Id);
        Assert.Equal("synthetic-identity", person.WhatsAppSenderId);
        var audit = new EventAudit(Guid.NewGuid(), Guid.NewGuid(), EventAuditAction.ParticipantAdded, DateTime.UtcNow, person.Id);
        Assert.Throws<ArgumentException>(() => audit.CaptureRegistrationFingerprint("invalid"));
        audit.CaptureRegistrationFingerprint(new string('A', 64));
        audit.CaptureRegistrationFingerprint(new string('A', 64));
        Assert.Throws<InvalidOperationException>(() => audit.CaptureRegistrationFingerprint(new string('B', 64)));
        Assert.Throws<ArgumentException>(() => new EventAudit(Guid.NewGuid(), Guid.NewGuid(), EventAuditAction.ParticipantRenamed, DateTime.UtcNow));
    }

    private sealed class FailAfterRenameSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<EventAudit>().Any(x => x.Entity.Action == EventAuditAction.ParticipantRenamed))
                throw new InvalidOperationException("Synthetic failure after rename SQL.");
            return ValueTask.FromResult(result);
        }
    }
    private static HttpClient Client(LocalWebFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task Login(HttpClient client, string username = LocalWebFactory.UserName)
    {
        var token = Token(await client.GetStringAsync("/Access/Login"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Access/Login", Form(("UserName", username),
            ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)))).StatusCode);
    }
    private static FormUrlEncodedContent RenameForm(Guid personId, string name, int revision, string? token = null, bool confirmed = false)
    {
        var values = new List<(string, string)> { ("Input.PersonId", personId.ToString()), ("Input.DisplayName", name),
            ("Input.Revision", revision.ToString()), ("Input.ConfirmedShared", confirmed.ToString()),
            ("Input.WhatsAppSenderId", "forged-sender"), ("Input.Status", "Absent"), ("Input.IsSystemAdministrator", "true"), ("Input.ActorId", Guid.NewGuid().ToString()) };
        if (token is not null) values.Add(("__RequestVerificationToken", token));
        return Form(values.ToArray());
    }
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) => new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static void AssertDenied(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Access/Denied", response.Headers.Location!.ToString());
    }
}
