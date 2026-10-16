using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Games;
using TRobot.Application.Local;
using TRobot.Application.Messaging;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class GameTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTime Utc { get; set; } = new(2030, 12, 30, 11, 0, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Utc);
    }
    private static GameService Service(IServiceProvider provider, Clock clock)
    { var store = provider.GetRequiredService<ILocalStore>(); return new(store, new LocalOperations(store, clock), clock); }
    private static QuizDraft Draft(Clock clock) => new(Guid.NewGuid(), "Quanto fa due più due?", DateTime.SpecifyKind(clock.Utc.AddDays(1).AddHours(1), DateTimeKind.Unspecified), ["Tre", "Quattro"], 2);
    private static async Task Send(IServiceProvider provider, Clock clock, LocalWebFactory factory)
    {
        var store = provider.GetRequiredService<ILocalStore>();
        await new MessageService(store, new LocalOperations(store, clock), clock).TickAsync(factory.OrganizerPersonId, factory.EventId, true);
    }
    private static async Task<Guid> Person(IServiceProvider provider) => (await provider.GetRequiredService<TRobotDbContext>().EventParticipants.SingleAsync()).PersonId;
    private static async Task<QuizPage> Open(GameService service, IServiceProvider provider, Clock clock, LocalWebFactory factory)
    {
        var id = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, Draft(clock));
        await service.PublishAsync(factory.OrganizerPersonId, factory.EventId, id, 0, true); await Send(provider, clock, factory);
        return await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id);
    }

    [Fact]
    public void DomainProtectsImmutableDefinitionAndStateTransitions()
    {
        var clock = new Clock(); var ev = Guid.NewGuid(); var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new Quiz(ev, id, "Domanda", DateTime.Now, 1));
        Assert.Throws<ArgumentException>(() => new Quiz(ev, id, "Domanda", clock.Utc, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Quiz(ev, id, new string('x', 201), clock.Utc, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuizOption(ev, id, 1, new string('x', 151)));
        var quiz = new Quiz(ev, id, " Domanda ", clock.Utc, 1);
        Assert.Equal("Domanda", quiz.Question); Assert.Throws<InvalidOperationException>(() => quiz.Close(clock.Utc));
        quiz.Publish(Guid.NewGuid()); Assert.Throws<InvalidOperationException>(() => quiz.Publish(Guid.NewGuid()));
        quiz.Close(clock.Utc); Assert.Throws<InvalidOperationException>(() => quiz.Cancel());
        var member = new GameMembership(ev, Guid.NewGuid()); Assert.False(member.SetActive(true));
        Assert.True(member.SetActive(false)); Assert.True(member.SetActive(true)); Assert.Equal(2, member.Revision);
    }

    [Fact]
    public async Task VoluntaryWorkflowScoresOnlyClosedQuizzesAndPreservesReceiptAcrossWithdrawAndRejoin()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider; var clock = new Clock(); var service = Service(provider, clock); var person = await Person(provider);
        var page = await Open(service, provider, clock, factory); Assert.True(page.Open); Assert.Empty(page.Eligible);
        Assert.Empty((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Scores);
        var ballot = new QuizBallot(Guid.NewGuid(), person, page.Options[1].Id, 1, true);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, person, 0, true, false));
        await service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, person, 0, true, true);
        await service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot);
        Assert.Equal(0, Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Scores).Points);
        await service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, person, 0, false, true);
        await service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot);
        await service.CloseAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, 2, true);
        await service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot);
        Assert.Empty((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Scores);
        await service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, person, 1, true, true);
        Assert.Equal(1, Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Scores).Points);
        var db = provider.GetRequiredService<TRobotDbContext>(); Assert.Single(await db.Set<QuizAnswer>().ToListAsync());
        var message = await db.Set<OutboundMessage>().SingleAsync(); Assert.Equal(MessageKind.Entertainment, message.Kind);
        Assert.DoesNotContain("Soluzione", message.Body); foreach (var p in await db.People.ToListAsync()) Assert.DoesNotContain(p.DisplayName, message.Body);
        Assert.Empty(await db.ParticipationReceipts.ToListAsync());
    }

    [Fact]
    public async Task RejoinNeverAllowsSecondAnswerAndOperationCollisionDoesNotChangeAnswer()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider; var clock = new Clock(); var service = Service(provider, clock); var person = await Person(provider);
        await service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, person, 0, true, true);
        var page = await Open(service, provider, clock, factory);
        var ballot = new QuizBallot(Guid.NewGuid(), person, page.Options[0].Id, 1, true);
        await service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot);
        await service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, person, 0, false, true);
        await service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, person, 1, true, true);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot with { OptionId = page.Options[1].Id }));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot with { OperationId = Guid.NewGuid(), Revision = 2 }));
        Assert.Empty((await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id)).Eligible);
        await service.CloseAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, 2, true);
        Assert.Equal(0, Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Scores).Points);
    }

    [Fact]
    public async Task PublicationRespectsQuietHoursAndCapAndDeadlineBoundaryRejectsNewAnswers()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider; var db = provider.GetRequiredService<TRobotDbContext>(); var clock = new Clock();
        clock.Utc = new DateTime(2030, 12, 30, 23, 0, 0, DateTimeKind.Utc); var service = Service(provider, clock); var person = await Person(provider);
        await service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, person, 0, true, true);
        var page = await Open(service, provider, clock, factory); Assert.False(page.Open); Assert.Equal(OutboundMessageStatus.Queued, page.Publication!.Status);
        var ballot = new QuizBallot(Guid.NewGuid(), person, page.Options[1].Id, 1, true);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot));
        var settings = new BotSettings(factory.EventId); settings.Update(false, 23, 9, 0, 5); db.Add(settings); await db.SaveChangesAsync();
        clock.Utc = clock.Utc.Date.AddDays(1).AddHours(11); await Send(provider, clock, factory);
        Assert.False((await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id)).Open);
        settings = await db.Set<BotSettings>().SingleAsync(); settings.Update(false, 23, 9, 3, 5); await db.SaveChangesAsync();
        await Send(provider, clock, factory); Assert.True((await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id)).Open);
        clock.Utc = page.Quiz.DeadlineUtc;
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot));
        await service.CloseAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, 1, true);
    }

    [Fact]
    public async Task TiedScoresHaveSameRankAndWithdrawnPeopleAreExcluded()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider; var db = provider.GetRequiredService<TRobotDbContext>(); var clock = new Clock(); var service = Service(provider, clock);
        var person = await Person(provider); var second = new Person("Seconda persona"); db.AddRange(second, new EventParticipant(factory.EventId, second.Id)); await db.SaveChangesAsync();
        await service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, person, 0, true, true);
        await service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, second.Id, 0, true, true);
        var page = await Open(service, provider, clock, factory);
        await service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, new(Guid.NewGuid(), person, page.Options[1].Id, 1, true));
        await service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, new(Guid.NewGuid(), second.Id, page.Options[1].Id, 2, true));
        await service.CloseAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, 3, true);
        var scores = (await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Scores;
        Assert.Equal(2, scores.Count); Assert.All(scores, x => { Assert.Equal(1, x.Position); Assert.Equal(1, x.Points); });
        await service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, second.Id, 0, false, true);
        Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Scores);
    }

    [Fact]
    public async Task CancellingQueuedQuizCancelsMessageAndPreservesDefinition()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var clock = new Clock(); var service = Service(scope.ServiceProvider, clock); var input = Draft(clock);
        var id = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, input);
        Assert.Equal(id, await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, input));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.CreateAsync(factory.OrganizerPersonId, factory.EventId, input with { CorrectPosition = 1 }));
        await service.PublishAsync(factory.OrganizerPersonId, factory.EventId, id, 0, true);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.CancelAsync(factory.OrganizerPersonId, factory.EventId, id, 1, false));
        await service.CancelAsync(factory.OrganizerPersonId, factory.EventId, id, 1, true);
        var page = await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, id);
        Assert.Equal(QuizStatus.Cancelled, page.Quiz.Status); Assert.Equal(OutboundMessageStatus.Cancelled, page.Publication!.Status); Assert.Equal(2, page.Options.Count);
    }

    [Fact]
    public async Task AccessForeignReferencesStaleRevisionsAndDisabledActorAreRejectedEvenForReplay()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider; var db = provider.GetRequiredService<TRobotDbContext>(); var clock = new Clock(); var service = Service(provider, clock); var person = await Person(provider);
        var other = new Event("Altro evento", clock.Utc); var outsider = new Person("Esterno"); db.AddRange(other, outsider, new EventParticipant(other.Id, outsider.Id), new EventOrganizer(other.Id, factory.OrganizerPersonId)); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalValidationException>(() => service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, outsider.Id, 0, true, true));
        await service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, person, 0, true, true);
        var page = await Open(service, provider, clock, factory);
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ReadAsync(factory.OrganizerPersonId, other.Id, page.Quiz.Id));
        var foreign = await service.CreateAsync(factory.OrganizerPersonId, other.Id, Draft(clock)); var foreignPage = await service.ReadAsync(factory.OrganizerPersonId, other.Id, foreign);
        var ballot = new QuizBallot(Guid.NewGuid(), person, page.Options[1].Id, 1, true);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot with { OptionId = foreignPage.Options[0].Id }));
        await service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.CloseAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, 1, true));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.CancelAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, 2, true));
        var user = await db.Users.SingleAsync(); user.IsEnabled = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ListAsync(factory.OrganizerPersonId, factory.EventId));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot));
    }

    [Theory]
    [InlineData("one")]
    [InlineData("duplicate")]
    [InlineData("correct")]
    [InlineData("past")]
    public async Task InvalidDraftDoesNotPersistPartialData(string kind)
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope(); var clock = new Clock(); var service = Service(scope.ServiceProvider, clock);
        var input = Draft(clock); input = kind switch { "one" => input with { Options = ["Uno"] }, "duplicate" => input with { Options = ["Uno", " uno "] }, "correct" => input with { CorrectPosition = 3 }, _ => input with { DeadlineLocal = new DateTime(2000, 1, 1) } };
        await Assert.ThrowsAsync<LocalValidationException>(() => service.CreateAsync(factory.OrganizerPersonId, factory.EventId, input));
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); Assert.Empty(await db.Set<Quiz>().ToListAsync()); Assert.Empty(await db.Set<QuizOption>().ToListAsync());
    }

    [Fact]
    public async Task FailureAfterSaveRollsBackAnswerRevisionAndAudit()
    {
        var interceptor = new FailAfterSave(); await using var factory = new LocalWebFactory { SaveInterceptor = interceptor }; using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider; var clock = new Clock(); var service = Service(provider, clock); var person = await Person(provider);
        await service.MembershipAsync(factory.OrganizerPersonId, factory.EventId, person, 0, true, true); var page = await Open(service, provider, clock, factory);
        var ballot = new QuizBallot(Guid.NewGuid(), person, page.Options[0].Id, 1, true); interceptor.Armed = true;
        await Assert.ThrowsAsync<ApplicationException>(() => service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot)); interceptor.Armed = false;
        var db = provider.GetRequiredService<TRobotDbContext>(); Assert.Empty(await db.Set<QuizAnswer>().AsNoTracking().ToListAsync());
        Assert.Equal(1, (await service.ReadAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id)).Quiz.Revision);
        Assert.Empty(await db.Set<LocalAudit>().Where(x => x.Module == "Games" && x.Action == "AnswerSimulated").ToListAsync());
        await service.AnswerAsync(factory.OrganizerPersonId, factory.EventId, page.Quiz.Id, ballot); Assert.Single(await db.Set<QuizAnswer>().ToListAsync());
    }

    [Fact]
    public async Task HttpRequiresAuthenticationCsrfRevisionAndEncodesQuestion()
    {
        await using var factory = new LocalWebFactory(); using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Games?eventId=" + factory.EventId)).StatusCode);
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        token = Token(await client.GetStringAsync("/Games/Create?eventId=" + factory.EventId)); var id = Guid.NewGuid();
        var values = new[] { ("EventId", factory.EventId.ToString()), ("OperationId", id.ToString()), ("Question", "<script>quiz</script>"), ("Options", "Uno\nDue"), ("CorrectPosition", "2"), ("DeadlineLocal", "2090-12-31T20:00") };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Games/Create", Form(values))).StatusCode);
        var created = await client.PostAsync("/Games/Create", Form(values.Append(("__RequestVerificationToken", token)).ToArray())); Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var html = await client.GetStringAsync(created.Headers.Location); Assert.Contains("&lt;script&gt;quiz&lt;/script&gt;", html); Assert.DoesNotContain("<script>quiz</script>", html);
        var action = new[] { ("EventId", factory.EventId.ToString()), ("Id", id.ToString()), ("Confirmed", "true"), ("__RequestVerificationToken", token) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Games/Publish", Form(action))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Games/Publish", Form(action.Append(("Revision", "0")).ToArray()))).StatusCode);
    }
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) => new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { if (Armed) throw new ApplicationException("Errore sintetico dopo salvataggio risposta."); return ValueTask.FromResult(result); }
    }
}
