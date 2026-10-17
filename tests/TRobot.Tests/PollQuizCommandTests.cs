using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Attendance;
using TRobot.Application.Games;
using TRobot.Application.Local;
using TRobot.Application.Polls;
using TRobot.Application.Simulation;
using TRobot.Domain;
using TRobot.Infrastructure.Identity;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class PollQuizCommandTests
{
    private static SimulatedMessage Message(LocalWebFactory f, string text, string? id = null) =>
        new(f.EventId, "synthetic-http-sender", id ?? Guid.NewGuid().ToString("N"), text);

    private static async Task<(Guid Poll, Guid Quiz, Guid Person)> Seed(IServiceProvider p, LocalWebFactory f,
        string state = "open", bool member = true, Guid? pollId = null, Guid? quizId = null, string suffix = "")
    {
        var db = p.GetRequiredService<TRobotDbContext>(); var participant = await db.EventParticipants.SingleAsync();
        participant.ChangeStatus(ParticipationStatus.Confirmed, DateTime.UtcNow);
        if (member && !await db.Set<GameMembership>().AnyAsync()) db.Add(new GameMembership(f.EventId, participant.PersonId));
        var deadline = state == "expired" ? DateTime.UtcNow.AddDays(-1) : new DateTime(2090, 12, 31, 18, 0, 0, DateTimeKind.Utc);
        var poll = new Poll(f.EventId, pollId ?? Guid.NewGuid(), "Sondaggio sintetico " + suffix, deadline);
        var quiz = new Quiz(f.EventId, quizId ?? Guid.NewGuid(), "Domanda sintetica " + suffix, deadline, 2);
        db.AddRange(poll, quiz,
            new PollOption(f.EventId, poll.Id, 1, "Scelta A"), new PollOption(f.EventId, poll.Id, 2, "Scelta B"),
            new QuizOption(f.EventId, quiz.Id, 1, "Risposta A"), new QuizOption(f.EventId, quiz.Id, 2, "Risposta B"));
        if (state != "draft")
        {
            var publication = new OutboundMessage(f.EventId, Guid.NewGuid(), "Pubblicazione sintetica sondaggio", MessageKind.Operational, DateTime.UtcNow.AddDays(-2));
            var quizPublication = new OutboundMessage(f.EventId, Guid.NewGuid(), "Pubblicazione sintetica quiz", MessageKind.Entertainment, DateTime.UtcNow.AddDays(-2));
            publication.Queue(true); quizPublication.Queue(true); poll.Publish(publication.Id); quiz.Publish(quizPublication.Id);
            if (state is "open" or "closed" or "expired") { publication.MarkSent(DateTime.UtcNow.AddDays(-2)); quizPublication.MarkSent(DateTime.UtcNow.AddDays(-2)); }
            if (state == "closed") { poll.Close(DateTime.UtcNow, null); quiz.Close(DateTime.UtcNow); }
            if (state == "cancelled") { poll.Cancel(); quiz.Cancel(); publication.Cancel(true); quizPublication.Cancel(true); }
            db.AddRange(publication, quizPublication);
        }
        await db.SaveChangesAsync(); return (poll.Id, quiz.Id, participant.PersonId);
    }

    [Theory]
    [InlineData("open", true, true)]
    [InlineData("closed", false, true)]
    [InlineData("expired", false, true)]
    [InlineData("draft", false, false)]
    [InlineData("queued", false, false)]
    [InlineData("cancelled", false, false)]
    public async Task ListsOnlyOpenSentContentAndDetailsRequirePublicationWithoutRevealingQuizSolution(string state, bool listed, bool readable)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var rows = await Seed(p, f, state); var service = p.GetRequiredService<SimulationCommandService>();
        var polls = (await service.ExecuteAsync(f.OrganizerPersonId, Message(f, "!sondaggi"))).Reply;
        var quizzes = (await service.ExecuteAsync(f.OrganizerPersonId, Message(f, "!quiz"))).Reply;
        Assert.Equal(listed, polls.Contains("Sondaggio sintetico")); Assert.Equal(listed, quizzes.Contains("Domanda sintetica"));
        foreach (var command in new[] { "!sondaggio " + rows.Poll, "!quiz " + rows.Quiz })
        {
            if (!readable) { await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, Message(f, command))); continue; }
            var reply = (await service.ExecuteAsync(f.OrganizerPersonId, Message(f, command))).Reply;
            Assert.Contains("Scadenza:", reply); Assert.Contains("Europe/Rome", reply);
            Assert.Contains("1. ", reply); Assert.Contains("2. ", reply);
            Assert.DoesNotContain("soluzione", reply, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("CorrectPosition", reply); Assert.DoesNotContain("punti", reply, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Risposte registrate", reply); Assert.DoesNotContain("Voti totali", reply);
            foreach (var name in await p.GetRequiredService<TRobotDbContext>().People.Select(x => x.DisplayName).ToListAsync()) Assert.DoesNotContain(name, reply);
        }
        var db = p.GetRequiredService<TRobotDbContext>(); Assert.Empty(await db.Set<QuizAnswer>().ToListAsync());
        Assert.Empty(await db.Set<PollVoteReceipt>().ToListAsync()); Assert.Empty(await db.Set<LocalAudit>().ToListAsync());
        Assert.Equal(state == "draft" ? 0 : 2, await db.Set<OutboundMessage>().CountAsync());
    }

    [Fact]
    public async Task BothListsPaginateAndResolveShortReferencesAcrossAllRowsWithoutFilteringAmbiguities()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        for (var i = 0; i < 7; i++) await Seed(p, f, suffix: i.ToString());
        var service = p.GetRequiredService<SimulationCommandService>();
        foreach (var command in new[] { "!sondaggi", "!quiz lista" })
        {
            var one = (await service.ExecuteAsync(f.OrganizerPersonId, Message(f, command))).Reply;
            var two = (await service.ExecuteAsync(f.OrganizerPersonId, Message(f, command + " 2"))).Reply;
            Assert.Contains("Pagina 1/2; 7 elementi", one); Assert.Contains("Pagina 2/2; 7 elementi", two);
            Assert.Equal(5, one.Split('\n').Count(x => x.StartsWith('['))); Assert.Equal(2, two.Split('\n').Count(x => x.StartsWith('[')));
            Assert.Contains(command + " 2", one);
        }
        var first = await Seed(p, f, pollId: Guid.Parse("abcdef12-0000-0000-0000-000000000001"), quizId: Guid.Parse("abcdef13-0000-0000-0000-000000000001"));
        await Seed(p, f, "draft", pollId: Guid.Parse("abcdef12-0000-0000-0000-000000000002"), quizId: Guid.Parse("abcdef13-0000-0000-0000-000000000002"));
        foreach (var command in new[] { "!sondaggio abcdef12", "!vota abcdef12 1", "!quiz abcdef13", "!rispondi abcdef13 1" })
            await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, Message(f, command), true));
        Assert.Contains("Sondaggio", (await service.ExecuteAsync(f.OrganizerPersonId, Message(f, "!sondaggio " + first.Poll))).Reply);
        await service.ExecuteAsync(f.OrganizerPersonId, Message(f, "!vota " + first.Poll + " 1"), true);
        await service.ExecuteAsync(f.OrganizerPersonId, Message(f, "!rispondi " + first.Quiz + " 1"), true);
    }

    [Fact]
    public async Task ShortVotesAndAnswersPreserveReplaysAfterClosureAndWithdrawalWithoutRestoringOrOptingIn()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var rows = await Seed(p, f); var service = p.GetRequiredService<SimulationCommandService>();
        var vote = Message(f, "!vota " + SimulationReferences.Short(rows.Poll) + " 1", "short-vote");
        var answer = Message(f, "!rispondi " + SimulationReferences.Short(rows.Quiz) + " 1", "short-answer");
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, answer));
        await service.ExecuteAsync(f.OrganizerPersonId, vote, true); await service.ExecuteAsync(f.OrganizerPersonId, answer, true);
        Assert.True((await service.ExecuteAsync(f.OrganizerPersonId, answer with { Text = "!rispondi " + rows.Quiz + " 1" }, true)).Duplicate);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, answer with { MessageId = "second-answer" }, true));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, answer with { Text = "!rispondi " + rows.Quiz + " 2" }, true));
        var polls = p.GetRequiredService<PollService>(); var games = p.GetRequiredService<GameService>();
        var poll = await polls.ReadAsync(f.OrganizerPersonId, f.EventId, rows.Poll); await polls.CloseAsync(f.OrganizerPersonId, f.EventId, rows.Poll, poll.Poll.Revision, true, null);
        var quiz = await games.ReadAsync(f.OrganizerPersonId, f.EventId, rows.Quiz); await games.CloseAsync(f.OrganizerPersonId, f.EventId, rows.Quiz, quiz.Quiz.Revision, true);
        await games.MembershipAsync(f.OrganizerPersonId, f.EventId, rows.Person, 0, false, true);
        Assert.True((await service.ExecuteAsync(f.OrganizerPersonId, answer, true)).Duplicate);
        Assert.True((await service.ExecuteAsync(f.OrganizerPersonId, vote, true)).Duplicate);
        var db = p.GetRequiredService<TRobotDbContext>(); Assert.False((await db.Set<GameMembership>().SingleAsync()).IsActive);
        Assert.Single(await db.Set<QuizAnswer>().ToListAsync()); Assert.Single(await db.Set<PollVoteReceipt>().ToListAsync());
        Assert.Equal(2, await db.Set<OutboundMessage>().CountAsync());
    }

    [Theory]
    [InlineData("no-membership")]
    [InlineData("withdrawn")]
    [InlineData("draft")]
    [InlineData("queued")]
    [InlineData("expired")]
    [InlineData("closed")]
    public async Task NewAnswersRequireVoluntaryMembershipAndOpenPublishedQuiz(string kind)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var rows = await Seed(p, f, kind is "no-membership" or "withdrawn" ? "open" : kind, kind != "no-membership");
        if (kind == "withdrawn") await p.GetRequiredService<GameService>().MembershipAsync(f.OrganizerPersonId, f.EventId, rows.Person, 0, false, true);
        await Assert.ThrowsAsync<LocalValidationException>(() => p.GetRequiredService<SimulationCommandService>()
            .ExecuteAsync(f.OrganizerPersonId, Message(f, "!rispondi " + rows.Quiz + " 1"), true));
        var db = p.GetRequiredService<TRobotDbContext>(); Assert.Empty(await db.Set<QuizAnswer>().ToListAsync());
        Assert.Equal(kind == "no-membership" ? 0 : 1, await db.Set<GameMembership>().CountAsync());
    }

    [Theory]
    [InlineData("!sondaggi 0")]
    [InlineData("!sondaggi -1")]
    [InlineData("!sondaggi 100001")]
    [InlineData("!sondaggio")]
    [InlineData("!quiz lista 0")]
    [InlineData("!quiz lista x")]
    [InlineData("!quiz lista 1 extra")]
    [InlineData("!quiz nope")]
    [InlineData("!rispondi nope 1")]
    [InlineData("!rispondi nope 11")]
    [InlineData("!rispondi nope -1")]
    [InlineData("!rispondi nope 1 extra")]
    public async Task InvalidSyntaxDoesNotMutate(string text)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        await Assert.ThrowsAsync<LocalValidationException>(() => p.GetRequiredService<SimulationCommandService>().ExecuteAsync(f.OrganizerPersonId, Message(f, text), true));
        Assert.Empty(await p.GetRequiredService<TRobotDbContext>().Set<QuizAnswer>().ToListAsync());
    }

    [Fact]
    public async Task AnswerReceiptRejectsCrossCommandCollisionsInBothDirectionsAndRevalidatesActorAndSender()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var rows = await Seed(p, f); var db = p.GetRequiredService<TRobotDbContext>(); var service = p.GetRequiredService<SimulationCommandService>();
        var menu = new MenuItem(Guid.NewGuid(), f.EventId, "Menu sintetico", MenuCategory.Other, 1, "pezzi");
        var task = new OrganizationTask(f.EventId, "Attività sintetica", null, TaskPriority.Normal, rows.Person, null);
        db.AddRange(menu, task); await db.SaveChangesAsync();
        var answer = Message(f, "!rispondi " + rows.Quiz + " 1", "answer-collision"); await service.ExecuteAsync(f.OrganizerPersonId, answer, true);
        foreach (var text in new[] { "!presenza si", "!vota " + rows.Poll + " 1", "!porto " + menu.Id, "!prendo " + task.Id, "!fatto " + task.Id })
            await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, answer with { Text = text }, true));
        var commands = new[] { "!presenza si", "!vota " + rows.Poll + " 1", "!porto " + menu.Id, "!prendo " + task.Id, "!fatto " + task.Id };
        for (var i = 0; i < commands.Length; i++)
        {
            var message = Message(f, commands[i], "other-type-" + i); await service.ExecuteAsync(f.OrganizerPersonId, message, true);
            await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, message with { Text = answer.Text }, true));
        }
        var secondPerson = new Person("Seconda persona", "second-quiz-sender");
        var secondActor = new Person("Secondo organizzatore");
        db.AddRange(secondPerson, new EventParticipant(f.EventId, secondPerson.Id), secondActor, new EventOrganizer(f.EventId, secondActor.Id),
            new OrganizerUser { Id = Guid.NewGuid(), PersonId = secondActor.Id, UserName = "quiz-second-organizer", NormalizedUserName = "QUIZ-SECOND-ORGANIZER" });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(f.OrganizerPersonId, answer with { SenderId = "second-quiz-sender" }, true));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.ExecuteAsync(secondActor.Id, answer, true));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, answer with { SenderId = "unknown" }, true));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ExecuteAsync(f.OrganizerPersonId, answer with { EventId = Guid.NewGuid() }, true));
        (await db.EventOrganizers.SingleAsync(x => x.PersonId == f.OrganizerPersonId)).SetActive(false); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ExecuteAsync(f.OrganizerPersonId, answer, true));
        (await db.EventOrganizers.SingleAsync(x => x.PersonId == f.OrganizerPersonId)).SetActive(true);
        (await db.Users.SingleAsync(x => x.PersonId == f.OrganizerPersonId)).IsEnabled = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ExecuteAsync(f.OrganizerPersonId, answer, true));
        Assert.Single(await db.Set<QuizAnswer>().ToListAsync());
    }

    [Fact]
    public async Task ListsExcludeDeadlineBoundaryAndNewAnswersFailAtExactDeadline()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var rows = await Seed(p, f); var db = p.GetRequiredService<TRobotDbContext>();
        var clock = new FixedClock((await db.Set<Quiz>().SingleAsync()).DeadlineUtc);
        var store = p.GetRequiredService<ILocalStore>(); var operations = new LocalOperations(store, clock);
        var service = new PollQuizCommandService(store, p.GetRequiredService<IAttendanceStore>(), operations,
            new PollService(store, operations, clock), new GameService(store, operations, clock), clock);
        foreach (var text in new[] { "!sondaggi", "!quiz" })
        {
            var reply = await service.ExecuteAsync(f.OrganizerPersonId, Message(f, text), text.Split(' '), false);
            Assert.Contains("aperti: 0.", reply.Reply);
        }
        var answer = "!rispondi " + rows.Quiz + " 1";
        await Assert.ThrowsAsync<LocalValidationException>(() => service.ExecuteAsync(f.OrganizerPersonId, Message(f, answer), answer.Split(' '), true));
        var detail = "!quiz " + rows.Quiz;
        Assert.Contains("Stato: Scaduto", (await service.ExecuteAsync(f.OrganizerPersonId, Message(f, detail), detail.Split(' '), false)).Reply);
        Assert.Empty(await db.Set<QuizAnswer>().ToListAsync());
    }

    [Fact]
    public async Task QuizAnswerSaveFailureRollsBackReceiptRevisionAndAudit()
    {
        var interceptor = new FailAfterSave(); await using var f = new LocalWebFactory { SaveInterceptor = interceptor };
        using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider; var rows = await Seed(p, f);
        var message = Message(f, "!rispondi " + rows.Quiz + " 1"); var service = p.GetRequiredService<SimulationCommandService>();
        interceptor.Armed = true; await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(f.OrganizerPersonId, message, true)); interceptor.Armed = false;
        var db = p.GetRequiredService<TRobotDbContext>(); Assert.Empty(await db.Set<QuizAnswer>().ToListAsync()); Assert.Empty(await db.Set<LocalAudit>().ToListAsync());
        Assert.Equal(1, (await db.Set<Quiz>().SingleAsync()).Revision);
        await service.ExecuteAsync(f.OrganizerPersonId, message, true); Assert.Single(await db.Set<QuizAnswer>().ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentQuizAnswerAndAnotherCommandCannotShareMessageId(bool vote)
    {
        await using var f = new LocalWebFactory(); (Guid Poll, Guid Quiz, Guid Person) rows;
        using (var setup = f.Services.CreateScope()) rows = await Seed(setup.ServiceProvider, f);
        using var first = f.Services.CreateScope(); using var second = f.Services.CreateScope();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Run(IServiceProvider p, string command)
        {
            await start.Task;
            return await Record.ExceptionAsync(() => p.GetRequiredService<SimulationCommandService>()
                .ExecuteAsync(f.OrganizerPersonId, Message(f, command, "concurrent-quiz-id"), true));
        }
        var answer = Run(first.ServiceProvider, "!rispondi " + rows.Quiz + " 1");
        var other = Run(second.ServiceProvider, vote ? "!vota " + rows.Poll + " 1" : "!presenza si");
        start.SetResult(); var results = await Task.WhenAll(answer, other);
        Assert.Single(results, x => x is null); Assert.IsType<LocalConflictException>(Assert.Single(results, x => x is not null));
        using var check = f.Services.CreateScope(); var db = check.ServiceProvider.GetRequiredService<TRobotDbContext>();
        Assert.Equal(1, await db.Set<QuizAnswer>().CountAsync() + await db.Set<PollVoteReceipt>().CountAsync()
            + await db.ParticipationReceipts.CountAsync(x => x.MessageId == "concurrent-quiz-id"));
    }

    [Fact]
    public async Task HttpQuizCommandsRequireCsrfConfirmationAndRenderEncodedSafeReplies()
    {
        await using var f = new LocalWebFactory(); (Guid Poll, Guid Quiz, Guid Person) rows;
        using (var setup = f.Services.CreateScope()) rows = await Seed(setup.ServiceProvider, f, suffix: "<script>quiz</script>");
        using var client = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        token = Token(await client.GetStringAsync("/?eventId=" + f.EventId));
        var fields = new[] { ("Input.EventId", f.EventId.ToString()), ("Input.SenderId", "synthetic-http-sender"), ("Input.MessageId", "http-answer"),
            ("Input.Text", "!rispondi " + SimulationReferences.Short(rows.Quiz) + " 1") };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Home/Simulate", Form(fields))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/Home/Simulate", Form(fields.Append(("__RequestVerificationToken", token)).ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Home/Simulate", Form(fields.Append(("__RequestVerificationToken", token)).Append(("Input.ConfirmVote", "true")).ToArray()))).StatusCode);
        var detail = fields.Select(x => x.Item1 == "Input.MessageId" ? (x.Item1, "http-quiz-detail") : x.Item1 == "Input.Text" ? (x.Item1, "!quiz " + SimulationReferences.Short(rows.Quiz)) : x)
            .Append(("__RequestVerificationToken", token)).ToArray();
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Home/Simulate", Form(detail))).StatusCode);
        var html = await client.GetStringAsync("/?eventId=" + f.EventId);
        Assert.Contains("&lt;script&gt;quiz&lt;/script&gt;", html); Assert.DoesNotContain("<script>quiz</script>", html);
        Assert.DoesNotContain("Soluzione riservata", html); Assert.DoesNotContain("Risposte registrate:", html);
        using var verify = f.Services.CreateScope(); Assert.Single(await verify.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<QuizAnswer>().ToListAsync());
    }

    [Fact]
    public async Task MaximumUnicodeQuizDetailRoundTripsThroughBoundedTempDataCookies()
    {
        await using var f = new LocalWebFactory(); Guid quizId;
        using (var setup = f.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var quiz = new Quiz(f.EventId, Guid.NewGuid(), new string('界', 200), new DateTime(2090, 12, 31, 18, 0, 0, DateTimeKind.Utc), 10);
            quizId = quiz.Id;
            var message = new OutboundMessage(f.EventId, Guid.NewGuid(), "Pubblicazione sintetica", MessageKind.Entertainment, DateTime.UtcNow);
            message.Queue(true); message.MarkSent(DateTime.UtcNow); quiz.Publish(message.Id); db.AddRange(quiz, message);
            for (var i = 1; i <= 10; i++)
            { var prefix = i + ":"; db.Add(new QuizOption(f.EventId, quiz.Id, i, prefix + new string('漢', 150 - prefix.Length))); }
            await db.SaveChangesAsync();
        }
        using var client = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        token = Token(await client.GetStringAsync("/?eventId=" + f.EventId));
        var response = await client.PostAsync("/Home/Simulate", Form(("Input.EventId", f.EventId.ToString()), ("Input.SenderId", "synthetic-http-sender"),
            ("Input.MessageId", "max-unicode-detail"), ("Input.Text", "!quiz " + quizId), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var cookies = response.Headers.GetValues("Set-Cookie").ToArray(); var size = cookies.Sum(x => System.Text.Encoding.UTF8.GetByteCount(x));
        Assert.True(size < 16384, $"Cookie header troppo grande: {size} byte.");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/?eventId=" + f.EventId));
        Assert.Contains(new string('界', 200), html); Assert.Contains("10. 10:", html);
        Assert.DoesNotContain("Soluzione", html); Assert.DoesNotContain("CorrectPosition", html); Assert.DoesNotContain("Risposte registrate", html);
    }

    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) => new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { if (Armed) throw new ApplicationException("Errore sintetico dopo risposta."); return ValueTask.FromResult(result); }
    }
    private sealed class FixedClock(DateTime utc) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(utc); }
}
