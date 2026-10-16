using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Accounts;
using TRobot.Application.Attendance;
using TRobot.Application.Events;
using TRobot.Application.History;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class HistoryTests
{
    [Fact]
    public async Task HttpHistoryCombinesOnlyEventRecordsAndUsesCurrentNamesAndEventTimeZone()
    {
        await using var factory = new LocalWebFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var attendance = scope.ServiceProvider.GetRequiredService<AttendanceService>();
            await attendance.SimulateAsync(factory.OrganizerPersonId, new(factory.EventId, "synthetic-http-sender", "private-message-one", "!presenza si"));
            await attendance.SimulateAsync(factory.OrganizerPersonId, new(factory.EventId, "synthetic-http-sender", "private-message-two", "!presenza si"));
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var personId = (await db.EventParticipants.SingleAsync()).PersonId;
            await scope.ServiceProvider.GetRequiredService<ParticipantNameService>().RenameAsync(factory.OrganizerPersonId,
                factory.EventId, personId, 0, "Nome aggiornato nello storico", false);
            var other = new Event("Evento privato non assegnato", DateTime.UtcNow);
            db.Add(other);
            db.AddRange(new EventAudit(other.Id, factory.OrganizerPersonId, EventAuditAction.EventCreated, DateTime.UtcNow),
                new AccountAudit(factory.OrganizerPersonId, factory.OrganizerPersonId, "PasswordChanged", DateTime.UtcNow),
                new AccountAudit(factory.OrganizerPersonId, factory.OrganizerPersonId, "PasswordChanged", DateTime.UtcNow, factory.EventId),
                new AccountAudit(factory.OrganizerPersonId, factory.OrganizerPersonId, "EventGranted", new DateTime(2026, 12, 31, 19, 0, 0, DateTimeKind.Utc), factory.EventId));
            await db.SaveChangesAsync();
            var page = await scope.ServiceProvider.GetRequiredService<EventHistoryService>().GetAsync(factory.OrganizerPersonId, factory.EventId);
            Assert.Equal(4, page.Entries.Count);
            Assert.Equal(2, page.Entries.Count(x => x.Category == HistoryCategory.Attendance));
            Assert.Single(page.Entries, x => x.Category == HistoryCategory.Event);
            Assert.Single(page.Entries, x => x.Category == HistoryCategory.Access);
            Assert.Equal(EventAuditAction.ParticipantRenamed, page.Entries.Single(x => x.Category == HistoryCategory.Event).EventAction);
            Assert.Contains(page.Entries, x => x.PreviousStatus == ParticipationStatus.NoResponse && x.Status == ParticipationStatus.Confirmed);
            Assert.False(page.HasNext);
        }
        using var client = Client(factory);
        var url = "/History?eventId=" + factory.EventId;
        Assert.Contains("/Access/Login", (await client.GetAsync(url)).Headers.Location!.ToString());
        await Login(client);
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Nome aggiornato nello storico", html);
        Assert.Contains("Nome partecipante corretto", html);
        Assert.Contains("31/12/2026 20:00:00 +01:00", html);
        Assert.Contains("stato invariato", html);
        Assert.DoesNotContain("private-message", html);
        Assert.DoesNotContain("synthetic-http-sender", html);
        Assert.DoesNotContain("PasswordChanged", html);
        Assert.DoesNotContain("Evento privato non assegnato", html);
        var filtered = WebUtility.HtmlDecode(await client.GetStringAsync(url + "&category=Access"));
        Assert.Contains("Accesso evento assegnato", filtered);
        Assert.DoesNotContain("Nome partecipante corretto", filtered);
        Assert.DoesNotContain("→", filtered);
        using var verify = factory.Services.CreateScope();
        Assert.Equal(2, await verify.ServiceProvider.GetRequiredService<TRobotDbContext>().ParticipationReceipts.CountAsync());
    }

    [Fact]
    public async Task PaginationHasStableTieOrderingAcrossSourcesAndBoundedResults()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var personId = (await db.EventParticipants.SingleAsync()).PersonId;
        var now = new DateTime(2026, 7, 1, 18, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 51; i++) db.Add(new ParticipationReceipt(factory.EventId, personId, factory.OrganizerPersonId,
            "page-" + i, ParticipationStatus.NoResponse, ParticipationStatus.NoResponse, now));
        db.AddRange(new EventAudit(factory.EventId, factory.OrganizerPersonId, EventAuditAction.EventUpdated, now),
            new AccountAudit(factory.OrganizerPersonId, factory.OrganizerPersonId, "EventGranted", now, factory.EventId));
        await db.SaveChangesAsync();
        var service = scope.ServiceProvider.GetRequiredService<EventHistoryService>();
        var first = await service.GetAsync(factory.OrganizerPersonId, factory.EventId);
        var second = await service.GetAsync(factory.OrganizerPersonId, factory.EventId, page: 2);
        var third = await service.GetAsync(factory.OrganizerPersonId, factory.EventId, page: 3);
        Assert.Equal(EventHistoryService.PageSize, first.Entries.Count);
        Assert.Equal(25, second.Entries.Count);
        Assert.Equal(3, third.Entries.Count);
        Assert.True(first.HasNext);
        Assert.True(second.HasNext);
        Assert.False(third.HasNext);
        Assert.Equal(53, first.Entries.Concat(second.Entries).Concat(third.Entries).Select(x => x.Id).Distinct().Count());
        Assert.Equal(first.Entries.Select(x => x.Id), (await service.GetAsync(factory.OrganizerPersonId, factory.EventId)).Entries.Select(x => x.Id));
        Assert.Empty((await service.GetAsync(factory.OrganizerPersonId, factory.EventId, page: 4)).Entries);
        Assert.Single((await service.GetAsync(factory.OrganizerPersonId, factory.EventId, HistoryCategory.Event)).Entries);
        using var client = Client(factory);
        await Login(client);
        var html = await client.GetStringAsync($"/History?eventId={factory.EventId}&category=Attendance&page=2");
        Assert.Contains("category=Attendance", html);
        Assert.Contains("page=3", html);
        Assert.Contains("page=1", html);
        Assert.Contains("+02:00", WebUtility.HtmlDecode(html));
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=100001")]
    [InlineData("page=oops")]
    [InlineData("category=99")]
    [InlineData("category=unknown")]
    public async Task InvalidPagingAndFiltersAreRejected(string query)
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        await Login(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/History?eventId={factory.EventId}&{query}")).StatusCode);
    }

    [Fact]
    public async Task HistoryRequiresEnabledAccountAndActiveEventAssignmentEvenForAdministrator()
    {
        await using var factory = new LocalWebFactory();
        using var client = Client(factory);
        await Login(client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<EventHistoryService>();
        var other = new Event("Evento non assegnato", DateTime.UtcNow);
        db.Add(other);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<HistoryAccessException>(() => service.GetAsync(factory.OrganizerPersonId, other.Id));
        await Assert.ThrowsAsync<HistoryAccessException>(() => service.GetAsync(Guid.Empty, factory.EventId));
        await Assert.ThrowsAsync<HistoryAccessException>(() => service.GetAsync(factory.OrganizerPersonId, Guid.Empty));
        var url = "/History?eventId=" + factory.EventId;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        await scope.ServiceProvider.GetRequiredService<AccountService>().SetAssignmentAsync(factory.OrganizerPersonId,
            factory.OrganizerPersonId, factory.EventId, false, true);
        Assert.Contains("/Access/Denied", (await client.GetAsync(url)).Headers.Location!.ToString());
        await Assert.ThrowsAsync<HistoryAccessException>(() => service.GetAsync(factory.OrganizerPersonId, factory.EventId));
        await db.EventOrganizers.ExecuteUpdateAsync(x => x.SetProperty(o => o.IsActive, true));
        await db.Users.ExecuteUpdateAsync(x => x.SetProperty(u => u.IsEnabled, false));
        await Assert.ThrowsAsync<HistoryAccessException>(() => service.GetAsync(factory.OrganizerPersonId, factory.EventId));
    }

    private static HttpClient Client(LocalWebFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task Login(HttpClient client)
    {
        var token = Regex.Match(await client.GetStringAsync("/Access/Login"), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Access/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = LocalWebFactory.UserName, ["Password"] = LocalWebFactory.Password, ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token)
        }))).StatusCode);
    }
}
