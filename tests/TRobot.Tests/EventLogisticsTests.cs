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
using TRobot.Application.Events;
using TRobot.Domain;
using TRobot.Infrastructure.Identity;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class EventLogisticsTests
{
    private static readonly EventInput Valid = new("Evento logistica test", new DateTime(2026, 12, 31, 20, 0, 0),
        "Europe/Rome", "Sala dimostrativa", "20:00 Accoglienza\n00:00 Brindisi");

    [Fact]
    public async Task HttpCreateEditDisplayAndClearKeepAuditAndRevisionAndEncodePlainText()
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        await Login(client);
        var create = await client.GetStringAsync("/Events/Create");
        Assert.Contains("name=\"Input.Location\"", create);
        Assert.Contains("name=\"Input.Program\"", create);
        var token = Token(create);
        var original = Valid with { Location = " Sala <test> ", Program = "20:00 Accoglienza\r\n<script>window.fake=true;</script>" };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Events/Create", EventForm(original, 0))).StatusCode);
        var posted = await client.PostAsync("/Events/Create", EventForm(original, 0, token));
        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        Guid eventId;
        using (var scope = factory.Services.CreateScope())
        {
            var ev = await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Events.SingleAsync(x => x.Name == Valid.Name);
            eventId = ev.Id;
            Assert.Equal("Sala <test>", ev.Location);
            Assert.Equal(original.Program!.ReplaceLineEndings("\n"), ev.Program);
            Assert.Equal(0, ev.Revision);
        }
        var details = await client.GetStringAsync(posted.Headers.Location);
        Assert.Contains("&lt;script&gt;window.fake=true;&lt;/script&gt;", details);
        Assert.DoesNotContain("<script>window.fake=true;</script>", details);
        var dashboard = await client.GetStringAsync("/?eventId=" + eventId);
        Assert.Contains("Sala &lt;test&gt;", dashboard);
        Assert.Contains("&lt;script&gt;window.fake=true;&lt;/script&gt;", dashboard);
        Assert.DoesNotContain("<script>window.fake=true;</script>", dashboard);
        Assert.Contains("white-space: pre-wrap", dashboard);
        var updateUrl = "/Events/Update/" + eventId;
        var updated = Valid with { Location = "Luogo corretto test", Program = "21:00 Cena\n00:00 Brindisi" };
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(updateUrl, EventForm(updated, 0, token))).StatusCode);
        var noChange = await client.PostAsync(updateUrl, EventForm(updated with { Program = updated.Program!.Replace("\n", "\r\n") }, 1, token));
        Assert.Equal(HttpStatusCode.Redirect, noChange.StatusCode);
        Assert.Contains("Nessuna modifica", await client.GetStringAsync(noChange.Headers.Location));
        var stale = await client.PostAsync(updateUrl, EventForm(original, 0, token));
        Assert.Equal(HttpStatusCode.OK, stale.StatusCode);
        Assert.Contains("Ricarica la pagina", await stale.Content.ReadAsStringAsync());
        var cleared = await client.PostAsync(updateUrl, EventForm(updated with { Location = "   ", Program = "\r\n " }, 1, token));
        Assert.Equal(HttpStatusCode.Redirect, cleared.StatusCode);
        var emptyDashboard = await client.GetStringAsync("/?eventId=" + eventId);
        Assert.Contains("Da definire", emptyDashboard);
        using var verify = factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var saved = await db.Events.SingleAsync(x => x.Id == eventId);
        Assert.Null(saved.Location);
        Assert.Null(saved.Program);
        Assert.Equal(2, saved.Revision);
        Assert.Equal(3, await db.EventAudits.CountAsync(x => x.EventId == eventId));
        Assert.Equal(1, await db.EventParticipants.CountAsync());
        Assert.Empty(await db.ParticipationReceipts.ToListAsync());
        var history = await client.GetStringAsync("/History?eventId=" + eventId);
        Assert.Contains("Dati evento modificati", history);
        Assert.DoesNotContain("window.fake", history); // Audit stores metadata, not copies of event content.
    }

    [Theory]
    [InlineData("location")]
    [InlineData("program")]
    public async Task LengthLimitsAreValidatedInApplicationAndHttpWithoutPartialWrites(string field)
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        await Login(client);
        var token = Token(await client.GetStringAsync("/Events/Details/" + factory.EventId));
        var invalid = field == "location" ? Valid with { Location = new string('a', 301) } : Valid with { Program = new string('a', 4001) };
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventManagementService>();
        await Assert.ThrowsAsync<EventValidationException>(() => service.CreateAsync(factory.OrganizerPersonId, invalid));
        await Assert.ThrowsAsync<EventValidationException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, 0, invalid));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/Events/Create", EventForm(invalid, 0, token))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/Events/Update/" + factory.EventId, EventForm(invalid, 0, token))).StatusCode);
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var saved = await db.Events.SingleAsync();
        Assert.Equal("Evento test HTTP", saved.Name);
        Assert.Null(saved.Location);
        Assert.Null(saved.Program);
        Assert.Equal(0, saved.Revision);
        Assert.Empty(await db.EventAudits.ToListAsync());
    }

    [Fact]
    public async Task BrowserLineEndingsAtNormalizedLengthBoundaryAreAccepted()
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        await Login(client);
        var token = Token(await client.GetStringAsync("/Events/Create"));
        var input = Valid with { Location = new string('L', 300), Program = new string('P', 3998) + "\r\nZ" };
        var response = await client.PostAsync("/Events/Create", EventForm(input, 0, token));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Events.SingleAsync(x => x.Name == Valid.Name);
        Assert.Equal(300, saved.Location!.Length);
        Assert.Equal(4000, saved.Program!.Length);
        Assert.EndsWith("\nZ", saved.Program);
    }

    [Fact]
    public async Task NormalOrganizerCanEditOnlyAssignedEventsAndRevokedCookieCannotWrite()
    {
        await using var factory = new LocalWebFactory();
        Guid normalId, otherEventId;
        using (var setup = factory.Services.CreateScope())
        {
            await setup.ServiceProvider.GetRequiredService<AccountService>().CreateAsync(factory.OrganizerPersonId,
                factory.EventId, "logistics-editor", "Organizzatore logistica test", LocalWebFactory.Password);
            var db = setup.ServiceProvider.GetRequiredService<TRobotDbContext>();
            normalId = (await db.Users.SingleAsync(x => x.UserName == "logistics-editor")).PersonId;
            var other = new Event("Evento non assegnato test", DateTime.UtcNow, location: "Luogo riservato test");
            db.Add(other);
            await db.SaveChangesAsync();
            otherEventId = other.Id;
            await Assert.ThrowsAsync<EventAccessException>(() => setup.ServiceProvider.GetRequiredService<EventManagementService>()
                .UpdateAsync(factory.OrganizerPersonId, other.Id, 0, Valid));
        }
        using var client = Client(factory);
        await Login(client, "logistics-editor");
        var token = Token(await client.GetStringAsync("/Events/Details/" + factory.EventId));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Events/Update/" + factory.EventId, EventForm(Valid, 0, token))).StatusCode);
        var denied = await client.PostAsync("/Events/Update/" + otherEventId, EventForm(Valid, 0, token));
        Assert.Contains("/Access/Denied", denied.Headers.Location!.ToString());
        using var verify = factory.Services.CreateScope();
        await verify.ServiceProvider.GetRequiredService<AccountService>().SetAssignmentAsync(factory.OrganizerPersonId, normalId, factory.EventId, false, true);
        var revoked = await client.PostAsync("/Events/Update/" + factory.EventId, EventForm(Valid with { Location = "Revoca ignorata" }, 1, token));
        Assert.Contains("/Access/Denied", revoked.Headers.Location!.ToString());
        var dbVerify = verify.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(Valid.Location, (await dbVerify.Events.SingleAsync(x => x.Id == factory.EventId)).Location);
        Assert.Equal("Luogo riservato test", (await dbVerify.Events.SingleAsync(x => x.Id == otherEventId)).Location);
        Assert.Equal(normalId, (await dbVerify.EventAudits.SingleAsync()).ActorId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailureAfterSqlWriteRollsBackLogisticsRevisionAndAudit(bool create)
    {
        var interceptor = new FailAfterEventSave();
        await using var factory = new LocalWebFactory { SaveInterceptor = interceptor };
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<EventManagementService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        async Task Apply()
        {
            if (create) await service.CreateAsync(factory.OrganizerPersonId, Valid);
            else await service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, 0, Valid);
        }
        interceptor.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(Apply);
        var original = await db.Events.SingleAsync();
        Assert.Null(original.Location);
        Assert.Null(original.Program);
        Assert.Equal(0, original.Revision);
        Assert.Empty(await db.EventAudits.ToListAsync());
        Assert.Equal(1, await db.EventOrganizers.CountAsync());
        interceptor.Armed = false;
        await Apply();
        Assert.Equal(1, await db.EventAudits.CountAsync());
        Assert.Equal(Valid.Program, (await db.Events.SingleAsync(x => x.Name == Valid.Name)).Program);
    }

    [Fact]
    public async Task EfEventRevisionProtectsConcurrentLogisticsChanges()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        await using var other = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(db.Database.GetDbConnection()).Options);
        var first = await db.Events.SingleAsync();
        var stale = await other.Events.SingleAsync();
        first.UpdateDetails(first.Name, first.StartsAtUtc, first.TimeZoneId, "Luogo vincente", "Programma vincente");
        await db.SaveChangesAsync();
        stale.UpdateDetails(stale.Name, stale.StartsAtUtc, stale.TimeZoneId, "Luogo obsoleto", "Programma obsoleto");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal("Luogo vincente", (await db.Events.SingleAsync()).Location);
        Assert.Equal("Programma vincente", (await db.Events.SingleAsync()).Program);
    }

    [Fact]
    public async Task UpgradeFromMilestoneNinePreservesEventAccountsMembershipsReceiptsAndAudit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261016135707_OrganizerAttendance");
        var ev = new Event("Evento precedente logistica", DateTime.UtcNow);
        await MigrationUpgradeTests.InsertLegacyEvent(db, ev);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Events SET Revision = 3 WHERE Id = {ev.Id}");
        var actor = new Person("Organizzatore precedente logistica");
        var person = new Person("Partecipante precedente logistica");
        var member = new EventParticipant(ev.Id, person.Id);
        member.ChangeStatus(ParticipationStatus.Confirmed, DateTime.UtcNow);
        var receipt = new ParticipationReceipt(ev.Id, person.Id, actor.Id, "before-logistics", ParticipationStatus.NoResponse, ParticipationStatus.Confirmed, DateTime.UtcNow, AttendanceOrigin.Organizer);
        var audit = new EventAudit(ev.Id, actor.Id, EventAuditAction.EventUpdated, DateTime.UtcNow);
        db.AddRange(actor, person, member, new EventOrganizer(ev.Id, actor.Id), receipt, audit,
            new OrganizerUser { Id = Guid.NewGuid(), PersonId = actor.Id, UserName = "upgrade-logistics", PasswordHash = "synthetic-preserved-hash", SecurityStamp = "synthetic-preserved-stamp" });
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var saved = await db.Events.SingleAsync();
        Assert.Equal(ev.Id, saved.Id);
        Assert.Equal(ev.Name, saved.Name);
        Assert.Equal(ev.StartsAtUtc, saved.StartsAtUtc);
        Assert.Equal(ev.CreatedAtUtc, saved.CreatedAtUtc);
        Assert.Equal(3, saved.Revision);
        Assert.Null(saved.Location);
        Assert.Null(saved.Program);
        Assert.Equal("synthetic-preserved-hash", (await db.Users.SingleAsync()).PasswordHash);
        Assert.True((await db.EventOrganizers.SingleAsync()).IsActive);
        Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync()).Status);
        Assert.Equal(AttendanceOrigin.Organizer, (await db.ParticipationReceipts.SingleAsync()).Origin);
        Assert.Equal(receipt.Id, (await db.ParticipationReceipts.SingleAsync()).Id);
        Assert.Equal(audit.Id, (await db.EventAudits.SingleAsync()).Id);
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Events SET Location = {new string('x', 301)}"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Events SET Program = {new string('x', 4001)}"));
    }

    [Fact]
    public void DomainNormalizesOptionalTextAndValidatesBeforeMutatingAnyField()
    {
        var ev = new Event("Evento test", DateTime.UtcNow, location: " Sala test ", program: "Prima\r\nSeconda\rTerza");
        Assert.Equal("Sala test", ev.Location);
        Assert.Equal("Prima\nSeconda\nTerza", ev.Program);
        Assert.False(ev.UpdateDetails(ev.Name, ev.StartsAtUtc, ev.TimeZoneId, " Sala test ", "Prima\r\nSeconda\r\nTerza"));
        Assert.Equal(0, ev.Revision);
        Assert.Throws<ArgumentOutOfRangeException>(() => ev.UpdateDetails("Nome da non salvare", ev.StartsAtUtc, ev.TimeZoneId, "Altro luogo", new string('p', 4001)));
        Assert.Equal("Evento test", ev.Name);
        Assert.Equal("Sala test", ev.Location);
        Assert.Equal("Prima\nSeconda\nTerza", ev.Program);
        Assert.Equal(0, ev.Revision);
        Assert.True(ev.UpdateDetails(ev.Name, ev.StartsAtUtc, ev.TimeZoneId, "\t ", "\r\n "));
        Assert.Null(ev.Location);
        Assert.Null(ev.Program);
        Assert.Equal(1, ev.Revision);
    }

    private sealed class FailAfterEventSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<EventAudit>().Any()) throw new InvalidOperationException("Synthetic failure after logistics SQL.");
            return ValueTask.FromResult(result);
        }
    }
    private static HttpClient Client(LocalWebFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task Login(HttpClient client, string username = LocalWebFactory.UserName)
    {
        var token = Token(await client.GetStringAsync("/Access/Login"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Access/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = username, ["Password"] = LocalWebFactory.Password, ["__RequestVerificationToken"] = token
        }))).StatusCode);
    }
    private static FormUrlEncodedContent EventForm(EventInput input, int revision, string? token = null)
    {
        var values = new Dictionary<string, string>
        {
            ["Input.Name"] = input.Name, ["Input.LocalStart"] = input.LocalStart.ToString("yyyy-MM-ddTHH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["Input.TimeZoneId"] = input.TimeZoneId, ["Input.Revision"] = revision.ToString(),
            ["Input.Location"] = input.Location ?? "", ["Input.Program"] = input.Program ?? ""
        };
        if (token is not null) values["__RequestVerificationToken"] = token;
        return new(values);
    }
    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
