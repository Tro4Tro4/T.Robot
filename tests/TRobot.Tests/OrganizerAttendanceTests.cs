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
using TRobot.Application.History;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class OrganizerAttendanceTests
{
    [Fact]
    public async Task HttpOrganizerCanRecordPersonWithoutSenderWithConfirmationCsrfAndAttributedHistory()
    {
        await using var factory = new LocalWebFactory();
        Guid personId;
        using (var setup = factory.Services.CreateScope())
            personId = await setup.ServiceProvider.GetRequiredService<EventManagementService>().AddParticipantAsync(factory.OrganizerPersonId,
                factory.EventId, Guid.NewGuid(), "Persona senza mittente test", false);
        using var client = Client(factory);
        var url = $"/Attendance/Edit?eventId={factory.EventId}&personId={personId}";
        Assert.Contains("/Access/Login", (await client.GetAsync(url)).Headers.Location!.ToString());
        await Login(client);
        var html = await client.GetStringAsync(url);
        Assert.Contains("Persona senza mittente test", html);
        var operation = Guid.Parse(Field(html, "Input.OperationId"));
        var version = Field(html, "Input.ExpectedVersion");
        var token = Field(html, "__RequestVerificationToken");
        var command = new OrganizerAttendanceCommand(factory.EventId, personId, operation, ParticipationStatus.Confirmed, version, true);
        var postUrl = "/Attendance/Set?eventId=" + factory.EventId;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(postUrl, CommandForm(command))).StatusCode);
        var unconfirmed = await client.PostAsync(postUrl, CommandForm(command with { Confirmed = false }, token));
        Assert.Equal(HttpStatusCode.OK, unconfirmed.StatusCode);
        Assert.Contains("Conferma esplicitamente", await unconfirmed.Content.ReadAsStringAsync());
        var submitted = await client.PostAsync(postUrl, CommandForm(command, token));
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        Assert.Contains("Conteggi aggiornati", await client.GetStringAsync(submitted.Headers.Location));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(postUrl, CommandForm(command, token))).StatusCode);
        var dashboard = await client.GetStringAsync("/");
        Assert.Contains("id=\"confirmed-count\">1", dashboard);
        Assert.Contains("id=\"total-count\">2", dashboard);
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Null((await db.People.SingleAsync(x => x.Id == personId)).WhatsAppSenderId);
        Assert.Single((await verify.ServiceProvider.GetRequiredService<AttendanceService>().GetDashboardAsync(factory.OrganizerPersonId, factory.EventId)).Senders);
        var receipt = await db.ParticipationReceipts.SingleAsync();
        Assert.Equal(AttendanceOrigin.Organizer, receipt.Origin);
        Assert.Equal(factory.OrganizerPersonId, receipt.OrganizerPersonId);
        Assert.Equal(personId, receipt.PersonId);
        Assert.Equal(ParticipationStatus.NoResponse, receipt.PreviousStatus);
        Assert.Equal(ParticipationStatus.Confirmed, receipt.Status);
        var page = await verify.ServiceProvider.GetRequiredService<EventHistoryService>().GetAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(AttendanceOrigin.Organizer, page.Entries.Single(x => x.Category == HistoryCategory.Attendance).Origin);
        var history = WebUtility.HtmlDecode(await client.GetStringAsync("/History?eventId=" + factory.EventId));
        Assert.Contains("Presenza registrata dall'organizzatore", history);
        Assert.Contains("Senza risposta → Confermato", history);
        Assert.DoesNotContain(receipt.MessageId, history);
    }

    [Theory]
    [InlineData(ParticipationStatus.NoResponse, ParticipationStatus.Confirmed)]
    [InlineData(ParticipationStatus.NoResponse, ParticipationStatus.Undecided)]
    [InlineData(ParticipationStatus.NoResponse, ParticipationStatus.Absent)]
    [InlineData(ParticipationStatus.Confirmed, ParticipationStatus.Confirmed)]
    [InlineData(ParticipationStatus.Confirmed, ParticipationStatus.Undecided)]
    [InlineData(ParticipationStatus.Confirmed, ParticipationStatus.Absent)]
    [InlineData(ParticipationStatus.Undecided, ParticipationStatus.Confirmed)]
    [InlineData(ParticipationStatus.Undecided, ParticipationStatus.Undecided)]
    [InlineData(ParticipationStatus.Undecided, ParticipationStatus.Absent)]
    [InlineData(ParticipationStatus.Absent, ParticipationStatus.Confirmed)]
    [InlineData(ParticipationStatus.Absent, ParticipationStatus.Undecided)]
    [InlineData(ParticipationStatus.Absent, ParticipationStatus.Absent)]
    public async Task AllowedTransitionsHaveAtomicReceiptsAndUnchangedStatesKeepTimestamp(ParticipationStatus before, ParticipationStatus after)
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var member = await db.EventParticipants.SingleAsync();
        member.ChangeStatus(before, DateTime.UtcNow.AddDays(-1));
        await db.SaveChangesAsync();
        var oldTimestamp = member.UpdatedAtUtc;
        var service = scope.ServiceProvider.GetRequiredService<OrganizerAttendanceService>();
        var snapshot = await service.GetAsync(factory.OrganizerPersonId, factory.EventId, member.PersonId);
        var result = await service.SetAsync(factory.OrganizerPersonId, new(factory.EventId, member.PersonId, Guid.NewGuid(), after, snapshot.Version, true));
        Assert.Equal(before != after, result.Changed);
        Assert.Equal(after, result.Status);
        Assert.False(result.Duplicate);
        db.ChangeTracker.Clear();
        var saved = await db.EventParticipants.SingleAsync();
        var receipt = await db.ParticipationReceipts.SingleAsync();
        Assert.Equal(before, receipt.PreviousStatus);
        Assert.Equal(after, receipt.Status);
        Assert.Equal(AttendanceOrigin.Organizer, receipt.Origin);
        Assert.Equal(after, saved.Status);
        if (before == after) Assert.Equal(oldTimestamp, saved.UpdatedAtUtc);
        else Assert.Equal(receipt.ProcessedAtUtc, saved.UpdatedAtUtc);
    }

    [Fact]
    public async Task StalePagesAndOldReplaysNeverRestoreSupersededChoicesIncludingReturnToSameStatus()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var service = new OrganizerAttendanceService(scope.ServiceProvider.GetRequiredService<IAttendanceStore>(), new AdvancingClock());
        var personId = (await db.EventParticipants.SingleAsync()).PersonId;
        var start = await service.GetAsync(factory.OrganizerPersonId, factory.EventId, personId);
        var first = new OrganizerAttendanceCommand(factory.EventId, personId, Guid.NewGuid(), ParticipationStatus.Confirmed, start.Version, true);
        await service.SetAsync(factory.OrganizerPersonId, first);
        var confirmed = await service.GetAsync(factory.OrganizerPersonId, factory.EventId, personId);
        var stale = first with { OperationId = Guid.NewGuid(), ExpectedVersion = confirmed.Version, Status = ParticipationStatus.Undecided };
        await service.SetAsync(factory.OrganizerPersonId, first with { OperationId = Guid.NewGuid(), ExpectedVersion = confirmed.Version, Status = ParticipationStatus.Absent });
        var absent = await service.GetAsync(factory.OrganizerPersonId, factory.EventId, personId);
        var duplicate = await service.SetAsync(factory.OrganizerPersonId, first);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(ParticipationStatus.Absent, duplicate.Status);
        await service.SetAsync(factory.OrganizerPersonId, first with { OperationId = Guid.NewGuid(), ExpectedVersion = absent.Version });
        Assert.Equal(ParticipationStatus.Confirmed, (await service.GetAsync(factory.OrganizerPersonId, factory.EventId, personId)).Status);
        await Assert.ThrowsAsync<AttendanceConflictException>(() => service.SetAsync(factory.OrganizerPersonId, stale));
        await Assert.ThrowsAsync<AttendanceConflictException>(() => service.SetAsync(factory.OrganizerPersonId, first with { Status = ParticipationStatus.Absent }));
        Assert.Equal(3, await db.ParticipationReceipts.CountAsync());
    }

    [Fact]
    public async Task ManualAndSimulatedOperationsCannotReuseEachOthersIdentifiers()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var manual = scope.ServiceProvider.GetRequiredService<OrganizerAttendanceService>();
        var simulation = scope.ServiceProvider.GetRequiredService<AttendanceService>();
        var personId = (await db.EventParticipants.SingleAsync()).PersonId;
        var snapshot = await manual.GetAsync(factory.OrganizerPersonId, factory.EventId, personId);
        var operation = Guid.NewGuid();
        await simulation.SimulateAsync(factory.OrganizerPersonId, new(factory.EventId, "synthetic-http-sender", "organizer-" + operation.ToString("N"), "!presenza si"));
        await Assert.ThrowsAsync<AttendanceConflictException>(() => manual.SetAsync(factory.OrganizerPersonId,
            new(factory.EventId, personId, operation, ParticipationStatus.Confirmed, snapshot.Version, true)));
        var current = await manual.GetAsync(factory.OrganizerPersonId, factory.EventId, personId);
        var nextOperation = Guid.NewGuid();
        await manual.SetAsync(factory.OrganizerPersonId, new(factory.EventId, personId, nextOperation, ParticipationStatus.Confirmed, current.Version, true));
        await Assert.ThrowsAsync<AttendanceConflictException>(() => simulation.SimulateAsync(factory.OrganizerPersonId,
            new(factory.EventId, "synthetic-http-sender", "organizer-" + nextOperation.ToString("N"), "!presenza si")));
        Assert.Equal(2, await db.ParticipationReceipts.CountAsync());
    }

    [Fact]
    public async Task NormalOrganizerUpdatesOnlyCurrentEventAndRevokedCookieCannotWrite()
    {
        await using var factory = new LocalWebFactory();
        Guid normalId, personId, otherEventId;
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AccountService>().CreateAsync(factory.OrganizerPersonId, factory.EventId, "attendance-editor", "Organizzatore presenze", LocalWebFactory.Password);
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            normalId = (await db.Users.SingleAsync(x => x.UserName == "attendance-editor")).PersonId;
            personId = (await db.EventParticipants.SingleAsync()).PersonId;
            var other = new Event("Altro evento riservato", DateTime.UtcNow);
            otherEventId = other.Id;
            db.AddRange(other, new EventParticipant(other.Id, personId));
            await db.SaveChangesAsync();
        }
        using var client = Client(factory);
        await Login(client, "attendance-editor");
        var page = await client.GetStringAsync($"/Attendance/Edit?eventId={factory.EventId}&personId={personId}");
        var command = new OrganizerAttendanceCommand(factory.EventId, personId, Guid.NewGuid(), ParticipationStatus.Absent, Field(page, "Input.ExpectedVersion"), true);
        var token = Field(page, "__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Attendance/Set?eventId=" + factory.EventId, CommandForm(command, token))).StatusCode);
        var denied = await client.GetAsync($"/Attendance/Edit?eventId={otherEventId}&personId={personId}");
        Assert.Contains("/Access/Denied", denied.Headers.Location!.ToString());
        using var verify = factory.Services.CreateScope();
        var dbVerify = verify.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(ParticipationStatus.NoResponse, (await dbVerify.EventParticipants.SingleAsync(x => x.EventId == otherEventId)).Status);
        var receipt = await dbVerify.ParticipationReceipts.SingleAsync();
        Assert.Equal(normalId, receipt.OrganizerPersonId);
        var adminManual = verify.ServiceProvider.GetRequiredService<OrganizerAttendanceService>();
        await Assert.ThrowsAsync<AttendanceConflictException>(() => adminManual.SetAsync(factory.OrganizerPersonId, command));
        await verify.ServiceProvider.GetRequiredService<AccountService>().SetAssignmentAsync(factory.OrganizerPersonId, normalId, factory.EventId, false, true);
        var revoked = await client.PostAsync("/Attendance/Set?eventId=" + factory.EventId, CommandForm(command, token));
        Assert.Contains("/Access/Denied", revoked.Headers.Location!.ToString());
        Assert.Equal(1, await dbVerify.ParticipationReceipts.CountAsync());
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("unassigned")]
    [InlineData("empty-actor")]
    [InlineData("unconfirmed")]
    [InlineData("no-response")]
    [InlineData("invalid-status")]
    [InlineData("empty-operation")]
    [InlineData("wrong-person")]
    [InlineData("empty-version")]
    public async Task InvalidOrUnauthorizedCommandsLeaveNoReceipt(string scenario)
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var personId = (await db.EventParticipants.SingleAsync()).PersonId;
        var actorId = factory.OrganizerPersonId;
        var command = new OrganizerAttendanceCommand(factory.EventId, personId, Guid.NewGuid(), ParticipationStatus.Confirmed, "0:-", true);
        switch (scenario)
        {
            case "disabled": await db.Users.ExecuteUpdateAsync(x => x.SetProperty(u => u.IsEnabled, false)); break;
            case "unassigned": await db.EventOrganizers.ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false)); break;
            case "empty-actor": actorId = Guid.Empty; break;
            case "unconfirmed": command = command with { Confirmed = false }; break;
            case "no-response": command = command with { Status = ParticipationStatus.NoResponse }; break;
            case "invalid-status": command = command with { Status = (ParticipationStatus)99 }; break;
            case "empty-operation": command = command with { OperationId = Guid.Empty }; break;
            case "wrong-person": command = command with { PersonId = Guid.NewGuid() }; break;
            default: command = command with { ExpectedVersion = "" }; break;
        }
        var service = scope.ServiceProvider.GetRequiredService<OrganizerAttendanceService>();
        if (scenario is "disabled" or "unassigned" or "empty-actor")
            await Assert.ThrowsAsync<AttendanceAccessException>(() => service.SetAsync(actorId, command));
        else await Assert.ThrowsAsync<AttendanceValidationException>(() => service.SetAsync(actorId, command));
        Assert.Empty(await db.ParticipationReceipts.ToListAsync());
        Assert.Equal(ParticipationStatus.NoResponse, (await db.EventParticipants.SingleAsync()).Status);
    }

    [Fact]
    public async Task FailureAfterSqlWriteRollsBackStateAndReceiptThenSameOperationCanRetry()
    {
        var interceptor = new FailAfterAttendanceSave();
        await using var factory = new LocalWebFactory { SaveInterceptor = interceptor };
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<OrganizerAttendanceService>();
        var personId = (await db.EventParticipants.SingleAsync()).PersonId;
        var command = new OrganizerAttendanceCommand(factory.EventId, personId, Guid.NewGuid(), ParticipationStatus.Confirmed, "0:-", true);
        interceptor.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SetAsync(factory.OrganizerPersonId, command));
        Assert.Empty(await db.ParticipationReceipts.ToListAsync());
        var member = await db.EventParticipants.SingleAsync();
        Assert.Equal(ParticipationStatus.NoResponse, member.Status);
        Assert.Null(member.UpdatedAtUtc);
        interceptor.Armed = false;
        Assert.True((await service.SetAsync(factory.OrganizerPersonId, command)).Changed);
        Assert.Equal(1, await db.ParticipationReceipts.CountAsync());
    }

    [Fact]
    public async Task UpgradePreservesExistingReceiptAsSimulationAndRejectsInvalidOrigin()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261016131112_ParticipantNames");
        var ev = new Event("Prima delle presenze organizzatore", DateTime.UtcNow);
        var actor = new Person("Organizzatore precedente");
        var person = new Person("Partecipante precedente", "synthetic-origin-upgrade");
        var member = new EventParticipant(ev.Id, person.Id);
        member.ChangeStatus(ParticipationStatus.Confirmed, DateTime.UtcNow);
        await MigrationUpgradeTests.InsertLegacyEvent(db, ev);
        db.AddRange(actor, person, member, new EventOrganizer(ev.Id, actor.Id));
        await db.SaveChangesAsync();
        var receipt = new ParticipationReceipt(ev.Id, person.Id, actor.Id, "before-origin", ParticipationStatus.NoResponse, ParticipationStatus.Confirmed, DateTime.UtcNow);
        await MigrationUpgradeTests.InsertLegacyReceipt(db, receipt);
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var saved = await db.ParticipationReceipts.SingleAsync();
        Assert.Equal(receipt.Id, saved.Id);
        Assert.Equal(receipt.ProcessedAtUtc, saved.ProcessedAtUtc);
        Assert.Equal(AttendanceOrigin.Simulation, saved.Origin);
        Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync()).Status);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlRawAsync("UPDATE ParticipationReceipts SET Origin = 'Invalid'"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParticipationReceipt(ev.Id, person.Id, actor.Id, "invalid-origin",
            ParticipationStatus.NoResponse, ParticipationStatus.Confirmed, DateTime.UtcNow, (AttendanceOrigin)99));
    }

    private sealed class AdvancingClock : TimeProvider
    {
        private DateTimeOffset current = new(2026, 12, 31, 19, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => current = current.AddSeconds(1);
    }
    private sealed class FailAfterAttendanceSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<ParticipationReceipt>().Any())
                throw new InvalidOperationException("Synthetic failure after attendance SQL.");
            return ValueTask.FromResult(result);
        }
    }
    private static HttpClient Client(LocalWebFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task Login(HttpClient client, string username = LocalWebFactory.UserName)
    {
        var page = await client.GetStringAsync("/Access/Login");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Access/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = username, ["Password"] = LocalWebFactory.Password, ["__RequestVerificationToken"] = Field(page, "__RequestVerificationToken")
        }))).StatusCode);
    }
    private static string Field(string html, string name)
    {
        var match = Regex.Match(html, "name=\"" + Regex.Escape(name) + "\"[^>]*value=\"([^\"]*)\"");
        Assert.True(match.Success, "Campo mancante: " + name);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static FormUrlEncodedContent CommandForm(OrganizerAttendanceCommand command, string? token = null)
    {
        var values = new Dictionary<string, string>
        {
            ["Input.PersonId"] = command.PersonId.ToString(), ["Input.OperationId"] = command.OperationId.ToString(),
            ["Input.ExpectedVersion"] = command.ExpectedVersion, ["Input.Status"] = command.Status.ToString(),
            ["Input.Confirmed"] = command.Confirmed.ToString(), ["Input.ActorId"] = Guid.NewGuid().ToString(),
            ["Input.Origin"] = "Simulation", ["Input.WhatsAppSenderId"] = "forged-sender"
        };
        if (token is not null) values["__RequestVerificationToken"] = token;
        return new(values);
    }
}
