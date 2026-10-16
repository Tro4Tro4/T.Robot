using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Accounts;
using TRobot.Application.Attendance;
using TRobot.Application.Events;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class EventManagementTests
{
    private static readonly EventInput Valid = new("Nuovo evento test", new DateTime(2026, 12, 31, 20, 0, 0), "Europe/Rome");

    [Fact]
    public async Task CreateEditEnrollAndSimulateWorkWithRealCookiesAndCsrf()
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        Assert.Contains("/Access/Login", (await client.GetAsync("/Events")).Headers.Location!.ToString());
        await Login(client);
        var createHtml = await client.GetStringAsync("/Events/Create");
        Assert.Contains("name=\"Input.LocalStart\"", createHtml);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Events/Create", EventForm(null))).StatusCode);
        var created = await client.PostAsync("/Events/Create", EventForm(Token(createHtml)));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var page = await client.GetStringAsync(created.Headers.Location);
        var token = Token(page);
        var operationId = Field(page, "Participant.OperationId");
        Guid eventId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var ev = await db.Events.SingleAsync(x => x.Name == Valid.Name);
            eventId = ev.Id;
            Assert.Equal(new DateTime(2026, 12, 31, 19, 0, 0, DateTimeKind.Utc), ev.StartsAtUtc);
            Assert.Equal(factory.OrganizerPersonId, (await db.EventOrganizers.SingleAsync(x => x.EventId == eventId)).PersonId);
            Assert.Empty(await db.EventParticipants.Where(x => x.EventId == eventId).ToListAsync());
        }
        var url = "/Events/AddParticipant/" + eventId;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(url, ParticipantForm(operationId, null))).StatusCode);
        var posted = await client.PostAsync(url, ParticipantForm(operationId, token));
        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(url, ParticipantForm(operationId, token))).StatusCode);
        var details = await client.GetStringAsync(posted.Headers.Location);
        Assert.Contains("<td>Nuova persona test</td><td>Senza risposta</td>", details);
        var dashboard = await client.GetStringAsync("/?eventId=" + eventId);
        Assert.Contains("id=\"no-response-count\">1", dashboard);
        Assert.Contains("id=\"confirmed-count\">0", dashboard);
        string sender;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var person = await db.People.SingleAsync(x => x.DisplayName == "Nuova persona test");
            sender = person.WhatsAppSenderId!;
            Assert.StartsWith("sim-", sender);
            Assert.Equal(1, await db.EventParticipants.CountAsync(x => x.EventId == eventId));
            Assert.Equal(1, await db.Users.CountAsync()); // Forged fields must never create an account.
            Assert.Equal(2, await db.EventAudits.CountAsync());
        }
        var simulated = await client.PostAsync("/Home/Simulate", Form(("Input.EventId", eventId.ToString()),
            ("Input.SenderId", sender), ("Input.MessageId", "new-person-confirmed"), ("Input.Text", "!presenza si"),
            ("__RequestVerificationToken", Token(dashboard))));
        Assert.Equal(HttpStatusCode.Redirect, simulated.StatusCode);
        Assert.Contains("id=\"confirmed-count\">1", await client.GetStringAsync(simulated.Headers.Location));
        var editUrl = "/Events/Update/" + eventId;
        var edit = await client.PostAsync(editUrl, EventForm(token, "Nome aggiornato", "2027-01-01T21:30:00", 0));
        Assert.Equal(HttpStatusCode.Redirect, edit.StatusCode);
        var noChange = await client.PostAsync(editUrl, EventForm(token, "Nome aggiornato", "2027-01-01T21:30:00", 1));
        Assert.Equal(HttpStatusCode.Redirect, noChange.StatusCode);
        Assert.Contains("Nessuna modifica", await client.GetStringAsync(noChange.Headers.Location));
        var stale = await client.PostAsync(editUrl, EventForm(token, "Sovrascrittura obsoleta", revision: 0));
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Contains("Ricarica la pagina", WebUtility.HtmlDecode(await stale.Content.ReadAsStringAsync()));
        using var verification = factory.Services.CreateScope();
        var context = verification.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal("Nome aggiornato", (await context.Events.SingleAsync(x => x.Id == eventId)).Name);
        Assert.Equal(3, await context.EventAudits.CountAsync());
        Assert.Equal(1, await context.ParticipationReceipts.CountAsync());
        Assert.Equal(ParticipationStatus.Confirmed, (await context.EventParticipants.SingleAsync(x => x.EventId == eventId)).Status);
    }

    [Fact]
    public async Task MissingRevisionAndOperationIdCannotSilentlyCreateOrOverwrite()
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        await Login(client);
        var token = Token(await client.GetStringAsync("/Events/Details/" + factory.EventId));
        var update = await client.PostAsync("/Events/Update/" + factory.EventId, Form(("Input.Name", "Forged"),
            ("Input.LocalStart", "2026-12-31T20:00"), ("Input.TimeZoneId", "Europe/Rome"), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var add = await client.PostAsync("/Events/AddParticipant/" + factory.EventId,
            Form(("Participant.DisplayName", "Forged"), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Empty(await db.EventAudits.ToListAsync());
        Assert.Equal(1, await db.EventParticipants.CountAsync());
        Assert.Equal("Evento test HTTP", (await db.Events.SingleAsync()).Name);
    }

    [Fact]
    public async Task NormalOrganizerCanManageOnlyAssignedEventsAndCannotCreate()
    {
        await using var factory = new LocalWebFactory();
        Guid organizerId, otherId;
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<AccountService>().CreateAsync(factory.OrganizerPersonId, factory.EventId,
                "event-editor", "Organizzatore eventi test", LocalWebFactory.Password);
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            organizerId = (await db.People.SingleAsync(x => x.DisplayName == "Organizzatore eventi test")).Id;
            var other = new Event("Evento riservato", DateTime.UtcNow);
            db.Events.Add(other);
            await db.SaveChangesAsync();
            otherId = other.Id;
            var service = scope.ServiceProvider.GetRequiredService<EventManagementService>();
            await Assert.ThrowsAsync<EventAccessException>(() => service.GetAsync(factory.OrganizerPersonId, otherId));
        }
        using var client = Client(factory);
        await Login(client, "event-editor");
        var index = await client.GetStringAsync("/Events");
        Assert.DoesNotContain("Crea evento", index);
        Assert.DoesNotContain("Evento riservato", index);
        var token = Token(await client.GetStringAsync("/Events/Details/" + factory.EventId));
        AssertDenied(await client.GetAsync("/Events/Create"));
        AssertDenied(await client.GetAsync("/Events/Details/" + otherId));
        AssertDenied(await client.PostAsync("/Events/Create", EventForm(token)));
        AssertDenied(await client.PostAsync("/Events/Update/" + otherId, EventForm(token)));
        AssertDenied(await client.PostAsync("/Events/AddParticipant/" + otherId, ParticipantForm(Guid.NewGuid().ToString(), token)));
        var added = await client.PostAsync("/Events/AddParticipant/" + factory.EventId, ParticipantForm(Guid.NewGuid().ToString(), token));
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        using var verify = factory.Services.CreateScope();
        var events = verify.ServiceProvider.GetRequiredService<EventManagementService>();
        await verify.ServiceProvider.GetRequiredService<AccountService>().SetAssignmentAsync(factory.OrganizerPersonId, organizerId, factory.EventId, false, true);
        AssertDenied(await client.GetAsync("/Events/Details/" + factory.EventId));
        AssertDenied(await client.PostAsync("/Events/Update/" + factory.EventId, EventForm(token)));
        await Assert.ThrowsAsync<EventAccessException>(() => events.AddParticipantAsync(organizerId, factory.EventId, Guid.NewGuid(), "Non autorizzato", true));
    }

    [Fact]
    public async Task DisabledAdministratorCannotCreateAndDisabledOrganizerCannotManage()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        await db.Users.Where(x => x.PersonId == factory.OrganizerPersonId).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsEnabled, false));
        var service = scope.ServiceProvider.GetRequiredService<EventManagementService>();
        Assert.False(await service.CanCreateAsync(factory.OrganizerPersonId));
        await Assert.ThrowsAsync<EventAccessException>(() => service.CreateAsync(factory.OrganizerPersonId, Valid));
        await Assert.ThrowsAsync<EventAccessException>(() => service.GetAsync(factory.OrganizerPersonId, factory.EventId));
        await Assert.ThrowsAsync<EventAccessException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, 0, Valid));
        await Assert.ThrowsAsync<EventAccessException>(() => service.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, Guid.NewGuid(), "Denied", true));
        Assert.Empty(await db.EventAudits.ToListAsync());
    }

    [Theory]
    [InlineData("2026-03-29T02:30:00", "Europe/Rome", "non esiste")]
    [InlineData("2026-10-25T02:30:00", "Europe/Rome", "ambigua")]
    [InlineData("2026-12-31T20:00:00", "Nonexistent/Zone", "non riconosciuto")]
    [InlineData("0001-01-01T00:00:00", "Europe/Rome", "data")]
    public async Task InvalidOrAmbiguousLocalDatesNeverCreateEvents(string date, string zone, string message)
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventManagementService>();
        var ex = await Assert.ThrowsAsync<EventValidationException>(() => service.CreateAsync(factory.OrganizerPersonId,
            new("Invalid time", DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture), zone)));
        Assert.Contains(message, ex.Message);
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(1, await db.Events.CountAsync());
        Assert.Empty(await db.EventAudits.ToListAsync());
    }

    [Theory]
    [InlineData("2026-07-01T20:00:00", "Europe/Rome", 18)]
    [InlineData("2026-12-31T20:00:00", "Europe/Rome", 19)]
    [InlineData("2026-12-31T20:00:00", "UTC", 20)]
    public async Task LocalTimeIsConvertedUsingEventTimeZone(string date, string zone, int hour)
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventManagementService>();
        var id = await service.CreateAsync(factory.OrganizerPersonId, new("Time conversion test",
            DateTime.Parse(date, System.Globalization.CultureInfo.InvariantCulture), zone));
        var details = await service.GetAsync(factory.OrganizerPersonId, id);
        Assert.Equal(hour, details.Event.StartsAtUtc.Hour);
        Assert.Equal(DateTimeKind.Utc, details.Event.StartsAtUtc.Kind);
        Assert.Empty(details.Participants);
    }

    [Fact]
    public async Task InvalidNamesKindsAndIdsAreValidatedInApplication()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventManagementService>();
        foreach (var name in new[] { " ", new string('x', 201) })
            await Assert.ThrowsAsync<EventValidationException>(() => service.CreateAsync(factory.OrganizerPersonId, Valid with { Name = name }));
        await Assert.ThrowsAsync<EventValidationException>(() => service.CreateAsync(factory.OrganizerPersonId, Valid with { LocalStart = DateTime.UtcNow }));
        await Assert.ThrowsAsync<EventValidationException>(() => service.CreateAsync(factory.OrganizerPersonId, Valid with { TimeZoneId = " " }));
        await Assert.ThrowsAsync<EventValidationException>(() => service.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, Guid.Empty, "Person", true));
        foreach (var name in new[] { " ", new string('x', 151) })
            await Assert.ThrowsAsync<EventValidationException>(() => service.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, Guid.NewGuid(), name, true));
        await Assert.ThrowsAsync<EventAccessException>(() => service.CreateAsync(Guid.Empty, Valid));
        await Assert.ThrowsAsync<EventAccessException>(() => service.GetAsync(factory.OrganizerPersonId, Guid.Empty));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().EventAudits.ToListAsync());
    }

    [Fact]
    public async Task PeopleWithoutSimulationAndHomonymsRemainSeparateAndReplaysAreChecked()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventManagementService>();
        var operation = Guid.NewGuid();
        var id = await service.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, operation, " Omonimo test ", false);
        Assert.Equal(id, await service.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, operation, "Omonimo test", false));
        await Assert.ThrowsAsync<EventConflictException>(() => service.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, operation, "Omonimo test", true));
        await Assert.ThrowsAsync<EventConflictException>(() => service.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, operation, "Altro nome", false));
        var sameName = await service.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, Guid.NewGuid(), "Omonimo test", false);
        Assert.NotEqual(id, sameName);
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Null((await db.People.SingleAsync(x => x.Id == id)).WhatsAppSenderId);
        Assert.Equal(2, await db.EventAudits.CountAsync());
        Assert.Single((await scope.ServiceProvider.GetRequiredService<AttendanceService>().GetDashboardAsync(factory.OrganizerPersonId, factory.EventId)).Senders);
        var otherEvent = await service.CreateAsync(factory.OrganizerPersonId, Valid);
        await Assert.ThrowsAsync<EventConflictException>(() => service.AddParticipantAsync(factory.OrganizerPersonId, otherEvent, operation, "Omonimo test", false));
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("participant")]
    public async Task AuditFailureAfterSqlWriteRollsBackWholeOperationAndAllowsRetry(string action)
    {
        var interceptor = new FailAfterEventSave();
        await using var factory = new LocalWebFactory { SaveInterceptor = interceptor };
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventManagementService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var operation = Guid.NewGuid();
        async Task Apply()
        {
            switch (action)
            {
                case "create": await service.CreateAsync(factory.OrganizerPersonId, Valid); break;
                case "update": await service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, 0, Valid); break;
                default: await service.AddParticipantAsync(factory.OrganizerPersonId, factory.EventId, operation, "Atomic test", true); break;
            }
        }
        interceptor.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(Apply);
        Assert.Equal(1, await db.Events.CountAsync());
        Assert.Equal(2, await db.People.CountAsync());
        Assert.Equal(1, await db.EventParticipants.CountAsync());
        Assert.Equal(1, await db.EventOrganizers.CountAsync());
        Assert.Empty(await db.EventAudits.ToListAsync());
        Assert.Equal("Evento test HTTP", (await db.Events.SingleAsync()).Name);
        Assert.Equal(0, (await db.Events.SingleAsync()).Revision);
        interceptor.Armed = false;
        await Apply();
        Assert.Equal(1, await db.EventAudits.CountAsync());
    }

    [Fact]
    public async Task UpgradeFromMilestoneFivePreservesExistingDataAndStartsRevisionAtZero()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261016101059_OrganizerAccessLifecycle");
        var ev = new Event("Evento prima della gestione", DateTime.UtcNow);
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Events (Id, Name, StartsAtUtc, TimeZoneId, CreatedAtUtc) VALUES ({ev.Id}, {ev.Name}, {ev.StartsAtUtc}, {ev.TimeZoneId}, {ev.CreatedAtUtc})");
        var person = new Person("Persona precedente", "synthetic-before-management");
        var member = new EventParticipant(ev.Id, person.Id);
        member.ChangeStatus(ParticipationStatus.Confirmed, DateTime.UtcNow);
        await MigrationUpgradeTests.InsertLegacyPerson(db, person);
        db.AddRange(member, new EventOrganizer(ev.Id, person.Id),
            new ParticipationReceipt(ev.Id, person.Id, person.Id, "preserved-management", ParticipationStatus.NoResponse, ParticipationStatus.Confirmed, DateTime.UtcNow));
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var loaded = await db.Events.SingleAsync();
        Assert.Equal(ev.Id, loaded.Id);
        Assert.Equal(ev.Name, loaded.Name);
        Assert.Equal(ev.StartsAtUtc, loaded.StartsAtUtc);
        Assert.Equal(0, loaded.Revision);
        Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync()).Status);
        Assert.True((await db.EventOrganizers.SingleAsync()).IsActive);
        Assert.Equal("preserved-management", (await db.ParticipationReceipts.SingleAsync()).MessageId);
        Assert.Empty(await db.EventAudits.ToListAsync());
    }

    [Fact]
    public void DomainEditsAreValidatedBeforeMutationAndIncreaseRevisionOnlyOnChange()
    {
        var ev = new Event("Original", DateTime.UtcNow);
        Assert.Throws<ArgumentException>(() => ev.UpdateDetails("Valid name", DateTime.Now, "Europe/Rome"));
        Assert.Throws<TimeZoneNotFoundException>(() => ev.UpdateDetails("Valid name", DateTime.UtcNow, "Unknown/Zone"));
        Assert.Equal("Original", ev.Name);
        Assert.Equal(0, ev.Revision);
        Assert.False(ev.UpdateDetails(" Original ", ev.StartsAtUtc, ev.TimeZoneId));
        Assert.True(ev.UpdateDetails("Updated", ev.StartsAtUtc, ev.TimeZoneId));
        Assert.Equal(1, ev.Revision);
    }

    private sealed class FailAfterEventSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<EventAudit>().Any())
                throw new InvalidOperationException("Synthetic failure after SQL writes.");
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
    private static void AssertDenied(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Access/Denied", response.Headers.Location!.ToString());
    }
    private static FormUrlEncodedContent EventForm(string? token, string name = "Nuovo evento test", string date = "2026-12-31T20:00:00", int revision = 0)
    {
        var values = new List<(string, string)> { ("Input.Name", name), ("Input.LocalStart", date), ("Input.TimeZoneId", "Europe/Rome"), ("Input.Revision", revision.ToString()) };
        if (token is not null) values.Add(("__RequestVerificationToken", token));
        return Form(values.ToArray());
    }
    private static FormUrlEncodedContent ParticipantForm(string operation, string? token)
    {
        var values = new List<(string, string)> { ("Participant.OperationId", operation), ("Participant.DisplayName", "Nuova persona test"),
            ("Participant.EnableSimulation", "true"), ("Participant.Status", "Confirmed"), ("Participant.IsSystemAdministrator", "true"), ("Participant.ActorId", Guid.NewGuid().ToString()) };
        if (token is not null) values.Add(("__RequestVerificationToken", token));
        return Form(values.ToArray());
    }
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) => new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private static string Token(string html) => Field(html, "__RequestVerificationToken");
    private static string Field(string html, string name)
    {
        var match = Regex.Match(html, "name=\"" + Regex.Escape(name) + "\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Campo assente: " + name);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
