using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Local;
using TRobot.Application.Messaging;
using TRobot.Application.Polls;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class PollTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTime Utc { get; set; } = new(2030, 12, 30, 11, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Utc);
    }
    private static PollService Service(IServiceProvider provider, Clock clock)
    { var store = provider.GetRequiredService<ILocalStore>(); return new(store, new LocalOperations(store, clock), clock); }
    private static PollEdit Draft(Clock clock) => new(Guid.NewGuid(), "Dolce della serata", Local(clock.Utc.AddDays(1)), ["Pandoro", "Panettone"], 0);
    private static DateTime Local(DateTime utc) => DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome")), DateTimeKind.Unspecified);
    private static async Task Send(IServiceProvider provider, Clock clock, LocalWebFactory factory)
    {
        var store = provider.GetRequiredService<ILocalStore>();
        await new MessageService(store, new LocalOperations(store, clock), clock).TickAsync(factory.OrganizerPersonId, factory.EventId, true);
    }

    [Fact]
    public void DomainValidatesBeforeMutatingAndProtectsFrozenDefinition()
    {
        var id = Guid.NewGuid(); var ev = Guid.NewGuid(); var clock = new Clock();
        var poll = new Poll(ev, id, " Titolo ", clock.Utc);
        Assert.Equal("Titolo", poll.Title);
        Assert.Throws<ArgumentException>(() => poll.Edit("Nuovo", DateTime.SpecifyKind(clock.Utc, DateTimeKind.Local)));
        Assert.Equal("Titolo", poll.Title); Assert.Equal(0, poll.Revision);
        Assert.False(poll.Edit("Titolo", clock.Utc));
        Assert.True(poll.Edit("Nuovo", clock.Utc.AddHours(1)));
        poll.Publish(Guid.NewGuid());
        Assert.Throws<InvalidOperationException>(() => poll.Edit("Altro", clock.Utc));
        Assert.Throws<ArgumentException>(() => new PollOption(ev, id, 11, "Opzione"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PollOption(ev, id, 1, new string('x', 151)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Poll(ev, id, new string('x', 201), clock.Utc));
    }

    [Fact]
    public async Task FullWorkflowPreservesOldVoteReceiptsAndPublishesOnlyApprovedAggregates()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var participant = await db.EventParticipants.SingleAsync(); participant.ChangeStatus(ParticipationStatus.Confirmed, clock.Utc); await db.SaveChangesAsync();
        var personId = participant.PersonId; var draft = Draft(clock);
        var id = await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, null, draft);
        Assert.Equal(id, await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, null, draft));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.SaveAsync(factory.OrganizerPersonId, factory.EventId, null, draft with { Title = "Altro" }));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.PublishAsync(factory.OrganizerPersonId, factory.EventId, id, 0, false));
        await service.PublishAsync(factory.OrganizerPersonId, factory.EventId, id, 0, true);
        var page = await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id);
        Assert.False(page.Open); Assert.Equal(OutboundMessageStatus.Queued, page.Publication!.Status);
        var vote1 = new PollBallot(Guid.NewGuid(), personId, page.Counts[0].Option.Id, page.Poll.Revision, true);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id, vote1));
        await Send(scope.ServiceProvider, clock, factory);
        page = await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id); Assert.True(page.Open);
        var snapshot = await scope.ServiceProvider.GetRequiredService<ILocalStore>().SnapshotAsync(factory.EventId, default);
        Assert.Equal(1, snapshot.OpenPolls);
        await service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id, vote1);
        var vote2 = vote1 with { OperationId = Guid.NewGuid(), OptionId = page.Counts[1].Option.Id, Revision = 2 };
        await service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id, vote2);
        await service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id, vote1);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id, vote1 with { OptionId = vote2.OptionId }));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.CloseAsync(factory.OrganizerPersonId, factory.EventId, id, 2, true, null));
        await service.CloseAsync(factory.OrganizerPersonId, factory.EventId, id, 3, true, vote2.OptionId);
        var closedSnapshot = await scope.ServiceProvider.GetRequiredService<ILocalStore>().SnapshotAsync(factory.EventId, default);
        Assert.Equal(0, closedSnapshot.OpenPolls); Assert.NotEqual(snapshot.ChangeToken, closedSnapshot.ChangeToken);
        Assert.Single(await db.Set<OutboundMessage>().Where(x => x.Kind == MessageKind.Operational).ToListAsync());
        await service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id, vote1);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id, vote2 with { OperationId = Guid.NewGuid(), Revision = 4 }));
        page = await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id);
        Assert.Equal(new[] { 0, 1 }, page.Counts.Select(x => x.Count).ToArray()); Assert.False(page.Open);
        Assert.Contains("Decisione organizzativa: Panettone", page.ResultPreview);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.QueueResultAsync(factory.OrganizerPersonId, factory.EventId, id, 4, false));
        await service.QueueResultAsync(factory.OrganizerPersonId, factory.EventId, id, 4, true);
        await Send(scope.ServiceProvider, clock, factory);
        page = await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id);
        var result = Assert.IsType<OutboundMessage>(page.Result);
        var publication = Assert.IsType<OutboundMessage>(page.Publication);
        Assert.Equal(OutboundMessageStatus.Sent, result.Status);
        Assert.Contains("Voti totali: 1", result.Body);
        foreach (var person in await db.People.ToListAsync()) { Assert.DoesNotContain(person.DisplayName, publication.Body); Assert.DoesNotContain(person.DisplayName, result.Body); }
        Assert.Single(await db.Set<PollVote>().ToListAsync()); Assert.Equal(2, await db.Set<PollVoteReceipt>().CountAsync());
        Assert.Empty(await db.ParticipationReceipts.ToListAsync());
    }

    [Theory]
    [InlineData(ParticipationStatus.NoResponse, false)]
    [InlineData(ParticipationStatus.Undecided, false)]
    [InlineData(ParticipationStatus.Absent, false)]
    [InlineData(ParticipationStatus.Confirmed, true)]
    public async Task OnlyCurrentlyConfirmedAdultsCanCastNewVotes(ParticipationStatus status, bool child)
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var participant = await db.EventParticipants.SingleAsync(); participant.ChangeStatus(status, clock.Utc);
        if (child)
        {
            var household = new Household(factory.EventId, "Nucleo test", Guid.NewGuid());
            db.AddRange(household, new HouseholdMember(factory.EventId, household.Id, participant.PersonId, HouseholdMemberKind.Child, null, Guid.NewGuid()));
        }
        await db.SaveChangesAsync();
        var id = await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, null, Draft(clock));
        await service.PublishAsync(factory.OrganizerPersonId, factory.EventId, id, 0, true); await Send(scope.ServiceProvider, clock, factory);
        var page = await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id,
            new(Guid.NewGuid(), participant.PersonId, page.Counts[0].Option.Id, 1, true)));
        Assert.Empty(await db.Set<PollVoteReceipt>().ToListAsync());
    }

    [Fact]
    public async Task DeadlineBoundaryRejectsVotesButAllowsExplicitClosure()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var participant = await db.EventParticipants.SingleAsync(); participant.ChangeStatus(ParticipationStatus.Confirmed, clock.Utc); await db.SaveChangesAsync();
        var id = await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, null, Draft(clock));
        await service.PublishAsync(factory.OrganizerPersonId, factory.EventId, id, 0, true); await Send(scope.ServiceProvider, clock, factory);
        var page = await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id); clock.Utc = page.Poll.DeadlineUtc;
        Assert.False((await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id)).Open);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id,
            new(Guid.NewGuid(), participant.PersonId, page.Counts[0].Option.Id, 1, true)));
        await service.CloseAsync(factory.OrganizerPersonId, factory.EventId, id, 1, true, null);
        Assert.Equal(PollStatus.Closed, (await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id)).Poll.Status);
    }

    [Fact]
    public async Task EditingUnchangedBrowserDeadlinePreservesSecondsAndFinerTicks()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var input = Draft(clock) with { DeadlineLocal = Local(clock.Utc.AddDays(1).AddSeconds(12).AddTicks(1234567)) };
        var id = await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, null, input);
        var rendered = new DateTime(input.DeadlineLocal.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Unspecified);
        await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, id, input with { DeadlineLocal = rendered });
        var saved = (await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id)).Poll;
        Assert.Equal(clock.Utc.AddDays(1).AddSeconds(12).AddTicks(1234567), saved.DeadlineUtc); Assert.Equal(0, saved.Revision);
        Assert.Equal(1, await db.Set<LocalAudit>().CountAsync(x => x.Module == "Polls"));
    }

    [Fact]
    public async Task CancellationBeforePublicationCancelsOutboxAndKeepsHistory()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var id = await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, null, Draft(clock));
        await service.PublishAsync(factory.OrganizerPersonId, factory.EventId, id, 0, true);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.CancelAsync(factory.OrganizerPersonId, factory.EventId, id, 1, false));
        await service.CancelAsync(factory.OrganizerPersonId, factory.EventId, id, 1, true);
        var page = await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id);
        Assert.Equal(PollStatus.Cancelled, page.Poll.Status); Assert.Equal(OutboundMessageStatus.Cancelled, page.Publication!.Status);
        Assert.Equal(2, page.Counts.Count); Assert.Empty(await db.Set<PollVoteReceipt>().ToListAsync());
    }

    [Fact]
    public async Task PublishedPollCannotBeCancelledAndForeignReferencesAndDisabledActorsAreRejected()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var participant = await db.EventParticipants.SingleAsync(); participant.ChangeStatus(ParticipationStatus.Confirmed, clock.Utc);
        var other = new Event("Altro evento", clock.Utc); var outsider = new Person("Partecipante esterno");
        db.AddRange(other, outsider, new EventParticipant(other.Id, outsider.Id), new EventOrganizer(other.Id, factory.OrganizerPersonId)); await db.SaveChangesAsync();
        var id = await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, null, Draft(clock));
        var otherId = await service.SaveAsync(factory.OrganizerPersonId, other.Id, null, Draft(clock));
        var foreignPage = await service.ReadAsync(factory.OrganizerPersonId, other.Id, otherId);
        await service.PublishAsync(factory.OrganizerPersonId, factory.EventId, id, 0, true); await Send(scope.ServiceProvider, clock, factory);
        var ex = await Assert.ThrowsAsync<LocalValidationException>(() => service.CancelAsync(factory.OrganizerPersonId, factory.EventId, id, 1, true));
        Assert.Contains("già pubblicato", ex.Message);
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ReadAsync(factory.OrganizerPersonId, other.Id, id));
        var page = await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id,
            new(Guid.NewGuid(), outsider.Id, page.Counts[0].Option.Id, 1, true)));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id,
            new(Guid.NewGuid(), participant.PersonId, foreignPage.Counts[0].Option.Id, 1, true)));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.CloseAsync(factory.OrganizerPersonId, factory.EventId, id, 1, true, foreignPage.Counts[0].Option.Id));
        var assignment = await db.EventOrganizers.SingleAsync(x => x.EventId == factory.EventId); assignment.SetActive(false); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.CloseAsync(factory.OrganizerPersonId, factory.EventId, id, 1, true, null));
        assignment = await db.EventOrganizers.SingleAsync(x => x.EventId == factory.EventId); assignment.SetActive(true);
        var user = await db.Users.SingleAsync(); user.IsEnabled = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.CloseAsync(factory.OrganizerPersonId, factory.EventId, id, 1, true, null));
        Assert.Equal(PollStatus.PublicationQueued, (await db.Set<Poll>().AsNoTracking().SingleAsync(x => x.Id == id)).Status);
    }

    [Theory]
    [InlineData("one")]
    [InlineData("duplicate")]
    [InlineData("expired")]
    [InlineData("dst")]
    public async Task InvalidDraftsDoNotPersistPartialOptions(string kind)
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var input = Draft(clock);
        input = kind switch { "one" => input with { Options = ["Uno"] }, "duplicate" => input with { Options = ["Uno", " uno "] },
            "expired" => input with { DeadlineLocal = Local(clock.Utc) }, _ => input with { DeadlineLocal = new DateTime(2031, 3, 30, 2, 30, 0) } };
        await Assert.ThrowsAsync<LocalValidationException>(() => service.SaveAsync(factory.OrganizerPersonId, factory.EventId, null, input));
        Assert.Empty(await db.Set<Poll>().ToListAsync()); Assert.Empty(await db.Set<PollOption>().ToListAsync());
    }

    [Fact]
    public async Task FailureAfterVoteSaveRollsBackVoteReceiptPollRevisionAndAudit()
    {
        var interceptor = new FailAfterSave();
        await using var factory = new LocalWebFactory { SaveInterceptor = interceptor }; using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var participant = await db.EventParticipants.SingleAsync(); participant.ChangeStatus(ParticipationStatus.Confirmed, clock.Utc); await db.SaveChangesAsync();
        var id = await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, null, Draft(clock));
        await service.PublishAsync(factory.OrganizerPersonId, factory.EventId, id, 0, true); await Send(scope.ServiceProvider, clock, factory);
        var page = await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id);
        var ballot = new PollBallot(Guid.NewGuid(), participant.PersonId, page.Counts[0].Option.Id, 1, true);
        interceptor.Armed = true;
        await Assert.ThrowsAsync<ApplicationException>(() => service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id, ballot));
        interceptor.Armed = false;
        Assert.Empty(await db.Set<PollVote>().AsNoTracking().ToListAsync()); Assert.Empty(await db.Set<PollVoteReceipt>().AsNoTracking().ToListAsync());
        Assert.Equal(1, (await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id)).Poll.Revision);
        Assert.Empty(await db.Set<LocalAudit>().Where(x => x.Module == "Polls" && x.Action == "VoteSimulated").ToListAsync());
        await service.VoteAsync(factory.OrganizerPersonId, factory.EventId, id, ballot);
        Assert.Single(await db.Set<PollVoteReceipt>().ToListAsync());
    }

    [Fact]
    public async Task UpgradeFromOutboxPreservesSentMessagesAndAttendanceHistory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await connection.OpenAsync();
        await using var db = new TRobotDbContext(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261016175052_SimulatedOutbox");
        var clock = new Clock(); var ev = new Event("Evento upgrade sintetico", clock.Utc); var person = new Person("Persona upgrade sintetica");
        var message = new OutboundMessage(ev.Id, Guid.NewGuid(), "Messaggio sintetico già inviato", MessageKind.Operational, clock.Utc);
        message.Queue(true); message.MarkSent(clock.Utc);
        var receipt = new ParticipationReceipt(ev.Id, person.Id, person.Id, "synthetic-upgrade-receipt", ParticipationStatus.NoResponse, ParticipationStatus.Confirmed, clock.Utc);
        var audit = new LocalAudit(ev.Id, person.Id, message.Id, "Messages", "SentSimulated", clock.Utc);
        db.AddRange(ev, person, new EventParticipant(ev.Id, person.Id), new EventOrganizer(ev.Id, person.Id), message, receipt, audit); await db.SaveChangesAsync();
        db.ChangeTracker.Clear(); await db.Database.MigrateAsync(); await db.Database.MigrateAsync();
        var retained = await db.Set<OutboundMessage>().SingleAsync();
        Assert.Equal(message.Id, retained.Id); Assert.Equal(message.Body, retained.Body); Assert.Equal(OutboundMessageStatus.Sent, retained.Status);
        Assert.Equal(message.Revision, retained.Revision); Assert.Equal(clock.Utc, retained.SentAtUtc);
        Assert.Equal(receipt.Id, (await db.ParticipationReceipts.SingleAsync()).Id); Assert.Equal(audit.Id, (await db.Set<LocalAudit>().SingleAsync()).Id);
        Assert.Empty(await db.Set<Poll>().ToListAsync());
        Assert.Equal(0L, await db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM pragma_foreign_key_check").SingleAsync());
    }

    [Fact]
    public async Task HttpRequiresAuthenticationCsrfRevisionAndEncodesApprovedText()
    {
        await using var factory = new LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var url = "/Polls?eventId=" + factory.EventId;
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(url)).StatusCode);
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        var create = await client.GetStringAsync("/Polls/Create?eventId=" + factory.EventId); token = Token(create);
        var id = Guid.NewGuid(); var form = new[] { ("EventId", factory.EventId.ToString()), ("OperationId", id.ToString()), ("Revision", "0"),
            ("Title", "<script>dolce</script>"), ("Options", "Pandoro\r\nPanettone"), ("DeadlineLocal", "2090-12-31T20:00") };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Polls/Save", Form(form))).StatusCode);
        var saved = await client.PostAsync("/Polls/Save", Form(form.Append(("__RequestVerificationToken", token)).ToArray()));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var details = await client.GetStringAsync(saved.Headers.Location);
        Assert.Contains("&lt;script&gt;dolce&lt;/script&gt;", details); Assert.DoesNotContain("<script>dolce</script>", details);
        var publish = new[] { ("EventId", factory.EventId.ToString()), ("Id", id.ToString()), ("Confirmed", "true"), ("__RequestVerificationToken", token) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Polls/Publish", Form(publish))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Polls/Publish", Form(publish.Append(("Revision", "0")).ToArray()))).StatusCode);
        var foreign = await client.GetAsync("/Polls/Details?eventId=" + Guid.NewGuid() + "&id=" + id);
        Assert.Equal(HttpStatusCode.Redirect, foreign.StatusCode); Assert.Contains("/Access/Denied", foreign.Headers.Location!.ToString());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(OutboundMessageStatus.Queued, (await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<OutboundMessage>().SingleAsync()).Status);
    }
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) => new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { if (Armed) throw new ApplicationException("Errore sintetico dopo salvataggio voto."); return ValueTask.FromResult(result); }
    }
}
