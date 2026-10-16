using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Attendance;
using TRobot.Application.Local;
using TRobot.Application.Polls;
using TRobot.Application.Simulation;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class SimulationCommandTests
{
    private static SimulatedMessage Message(LocalWebFactory f, string text, string? id = null) =>
        new(f.EventId, "synthetic-http-sender", id ?? Guid.NewGuid().ToString("N"), text);

    private static async Task<Guid> PublishedPoll(IServiceProvider p, LocalWebFactory f)
    {
        await p.GetRequiredService<AttendanceService>().SimulateAsync(f.OrganizerPersonId, Message(f, "!presenza si"));
        var polls = p.GetRequiredService<PollService>();
        var id = await polls.SaveAsync(f.OrganizerPersonId, f.EventId, null,
            new(Guid.NewGuid(), "Dolce sintetico", new DateTime(2090, 12, 31, 20, 0, 0), ["Uno", "Due"], 0));
        await polls.PublishAsync(f.OrganizerPersonId, f.EventId, id, 0, true);
        // Fixture transport acknowledgement; no real transport is involved.
        var db = p.GetRequiredService<TRobotDbContext>();
        var publicationId = (await db.Set<Poll>().SingleAsync(q => q.Id == id)).PublicationMessageId;
        (await db.Set<OutboundMessage>().SingleAsync(x => x.Id == publicationId)).MarkSent(DateTime.UtcNow);
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task AggregateCommandsExcludeNamesNotesTitlesAndCancelledItemsAndDoNotWrite()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var db = p.GetRequiredService<TRobotDbContext>(); var person = (await db.EventParticipants.SingleAsync()).PersonId;
        db.Add(new MenuItem(Guid.NewGuid(), f.EventId, "Nome privato menu", MenuCategory.Drink, 2, "litri", person, MenuAvailability.Ready, "Nota privata"));
        db.Add(new MenuItem(Guid.NewGuid(), f.EventId, "Annullata", MenuCategory.Drink, 1, "litri", null, MenuAvailability.Cancelled, null));
        db.Add(new OrganizationTask(f.EventId, "Titolo privato", "Dettagli privati", TaskPriority.High, person, null));
        await db.SaveChangesAsync();
        var service = p.GetRequiredService<SimulationCommandService>();
        var menu = await service.ExecuteAsync(f.OrganizerPersonId, Message(f, "  !MENU  "));
        var todo = await service.ExecuteAsync(f.OrganizerPersonId, Message(f, "!todo"));
        Assert.Equal("Menu: 1 voci attive; 1 pronte, 0 assegnate, 0 da organizzare.", menu.Reply);
        Assert.Equal("Attività: 1 da iniziare, 0 in corso, 0 completate.", todo.Reply);
        foreach (var name in await db.People.Select(x => x.DisplayName).ToListAsync())
        { Assert.DoesNotContain(name, menu.Reply); Assert.DoesNotContain(name, todo.Reply); }
        Assert.Contains("!vota", (await service.ExecuteAsync(f.OrganizerPersonId, Message(f, "!aiuto"))).Reply);
        Assert.Empty(await db.Set<LocalAudit>().ToListAsync());
        Assert.Empty(await db.ParticipationReceipts.ToListAsync());
        Assert.Empty(await db.Set<PollVoteReceipt>().ToListAsync());
    }

    [Theory]
    [InlineData("!menu extra")]
    [InlineData("!todo extra")]
    [InlineData("!vota abc 1")]
    [InlineData("!vota 00000000-0000-0000-0000-000000000000 1")]
    [InlineData("!ignoto")]
    public async Task MalformedCommandsDoNotWrite(string text)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope();
        await Assert.ThrowsAsync<LocalValidationException>(() => scope.ServiceProvider.GetRequiredService<SimulationCommandService>()
            .ExecuteAsync(f.OrganizerPersonId, Message(f, text), true));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<PollVoteReceipt>().ToListAsync());
    }

    [Fact]
    public async Task VoteRequiresConfirmationDeduplicatesOldReplayAndRejectsChangedPayloadAndCrossCommandIds()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var id = await PublishedPoll(p, f); var service = p.GetRequiredService<SimulationCommandService>();
        var message = Message(f, $"!vota {id} 1", "vote-one");
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, message));
        Assert.False((await service.ExecuteAsync(f.OrganizerPersonId, message, true)).Duplicate);
        Assert.True((await service.ExecuteAsync(f.OrganizerPersonId, message, true)).Duplicate);
        await service.ExecuteAsync(f.OrganizerPersonId, message with { Text = $"!vota {id} 2", MessageId = "vote-two" }, true);
        Assert.True((await service.ExecuteAsync(f.OrganizerPersonId, message, true)).Duplicate);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { Text = $"!vota {id} 2" }, true));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { Text = "!presenza no" }, true));
        var presence = Message(f, "!presenza si", "presence-id"); await service.ExecuteAsync(f.OrganizerPersonId, presence);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, presence with { Text = $"!vota {id} 1" }, true));
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Equal(2, await db.Set<PollVoteReceipt>().CountAsync());
        var page = await p.GetRequiredService<PollService>().ReadAsync(f.OrganizerPersonId, f.EventId, id);
        Assert.Equal(new[] { 0, 1 }, page.Counts.Select(x => x.Count));
        await p.GetRequiredService<PollService>().CloseAsync(f.OrganizerPersonId, f.EventId, id, page.Poll.Revision, true, null);
        Assert.True((await service.ExecuteAsync(f.OrganizerPersonId, message, true)).Duplicate);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { MessageId = "late-vote" }, true));
    }

    [Fact]
    public async Task VotesRevalidateSenderIdentityAndActorAccessEvenOnReplay()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var id = await PublishedPoll(p, f); var service = p.GetRequiredService<SimulationCommandService>();
        var message = Message(f, $"!vota {id} 1"); await service.ExecuteAsync(f.OrganizerPersonId, message, true);
        var db = p.GetRequiredService<TRobotDbContext>(); var second = new Person("Seconda persona sintetica", "synthetic-second-sender");
        db.AddRange(second, new EventParticipant(f.EventId, second.Id)); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { SenderId = "synthetic-second-sender" }, true));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { SenderId = "unknown" }, true));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { EventId = Guid.NewGuid() }, true));
        var assignment = await db.EventOrganizers.SingleAsync(); assignment.SetActive(false); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ExecuteAsync(f.OrganizerPersonId, message, true));
        assignment.SetActive(true); (await db.Users.SingleAsync()).IsEnabled = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ExecuteAsync(f.OrganizerPersonId, message, true));
    }

    [Fact]
    public async Task ConcurrentPresenceAndVoteWithSameIdAllowOnlyOneWriteAcrossScopes()
    {
        await using var f = new LocalWebFactory(); Guid pollId;
        using (var setup = f.Services.CreateScope()) pollId = await PublishedPoll(setup.ServiceProvider, f);
        using var first = f.Services.CreateScope(); using var second = f.Services.CreateScope();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Run(IServiceProvider p, string command)
        {
            await start.Task;
            return await Record.ExceptionAsync(() => p.GetRequiredService<SimulationCommandService>()
                .ExecuteAsync(f.OrganizerPersonId, Message(f, command, "concurrent-id"), true));
        }
        var presence = Run(first.ServiceProvider, "!presenza no");
        var vote = Run(second.ServiceProvider, $"!vota {pollId} 1");
        start.SetResult(); var results = await Task.WhenAll(presence, vote);
        Assert.Single(results, x => x is null); Assert.IsType<LocalConflictException>(Assert.Single(results, x => x is not null));
        using var check = f.Services.CreateScope(); var db = check.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(1, await db.ParticipationReceipts.CountAsync(x => x.MessageId == "concurrent-id") + await db.Set<PollVoteReceipt>().CountAsync());
    }

    [Theory]
    [InlineData("unpublished")]
    [InlineData("ineligible")]
    [InlineData("child")]
    [InlineData("option")]
    public async Task RouterPreservesPollPublicationEligibilityAndOptionRules(string kind)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var id = await PublishedPoll(p, f); var db = p.GetRequiredService<TRobotDbContext>();
        if (kind == "unpublished")
            id = await p.GetRequiredService<PollService>().SaveAsync(f.OrganizerPersonId, f.EventId, null,
                new(Guid.NewGuid(), "Bozza", new DateTime(2090, 12, 31, 20, 0, 0), ["Uno", "Due"], 0));
        if (kind == "ineligible")
        { (await db.EventParticipants.SingleAsync()).ChangeStatus(ParticipationStatus.Undecided, DateTime.UtcNow); await db.SaveChangesAsync(); }
        if (kind == "child")
        {
            var household = new Household(f.EventId, "Nucleo sintetico", Guid.NewGuid());
            db.AddRange(household, new HouseholdMember(f.EventId, household.Id, (await db.EventParticipants.SingleAsync()).PersonId,
                HouseholdMemberKind.Child, null, Guid.NewGuid()));
            await db.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<LocalValidationException>(() => p.GetRequiredService<SimulationCommandService>()
            .ExecuteAsync(f.OrganizerPersonId, Message(f, $"!vota {id} {(kind == "option" ? 99 : 1)}"), true));
        Assert.Empty(await db.Set<PollVote>().ToListAsync()); Assert.Empty(await db.Set<PollVoteReceipt>().ToListAsync());
    }

    [Fact]
    public async Task HttpCommandsRequireAuthenticationCsrfAndVoteConfirmationAndRenderAggregateReply()
    {
        await using var f = new LocalWebFactory();
        using var client = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Home/Simulate", Form(("Input.Text", "!menu")))).StatusCode);
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        var html = await client.GetStringAsync("/?eventId=" + f.EventId); token = Token(html);
        Assert.Contains("Input.ConfirmVote", html);
        var values = new[] { ("Input.EventId", f.EventId.ToString()), ("Input.SenderId", "synthetic-http-sender"),
            ("Input.MessageId", "http-command"), ("Input.Text", "!menu") };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Home/Simulate", Form(values))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Home/Simulate", Form(values.Append(("__RequestVerificationToken", token)).ToArray()))).StatusCode);
        Assert.Contains("Menu: 0 voci attive", await client.GetStringAsync("/?eventId=" + f.EventId));
        Guid id; using (var scope = f.Services.CreateScope()) id = await PublishedPoll(scope.ServiceProvider, f);
        var vote = values.Select(x => x.Item1 == "Input.Text" ? (x.Item1, $"!vota {id} 1") : x).Append(("__RequestVerificationToken", token)).ToArray();
        var unconfirmed = await client.PostAsync("/Home/Simulate", Form(vote));
        Assert.Equal(HttpStatusCode.OK, unconfirmed.StatusCode); Assert.Contains("Conferma esplicitamente", await unconfirmed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Home/Simulate", Form(vote.Append(("Input.ConfirmVote", "true")).ToArray()))).StatusCode);
        using var verify = f.Services.CreateScope(); Assert.Single(await verify.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<PollVoteReceipt>().ToListAsync());
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) => new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
}
