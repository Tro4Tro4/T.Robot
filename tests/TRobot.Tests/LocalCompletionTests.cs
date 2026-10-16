using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Accounts;
using TRobot.Application.Accounting;
using TRobot.Application.Attendance;
using TRobot.Application.Events;
using TRobot.Application.Games;
using TRobot.Application.History;
using TRobot.Application.Households;
using TRobot.Application.Local;
using TRobot.Application.Menu;
using TRobot.Application.Messaging;
using TRobot.Application.Polls;
using TRobot.Application.Tasks;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class LocalCompletionTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTime Utc { get; set; } = new(2032, 3, 28, 0, 30, 0, DateTimeKind.Utc);
        public override DateTimeOffset GetUtcNow() => new(Utc);
    }
    private static DateTime Local(DateTime utc) => DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome")), DateTimeKind.Unspecified);
    private static LocalDashboardService Overview(IServiceProvider p, Clock clock) => new(p.GetRequiredService<ILocalStore>(),
        p.GetRequiredService<ILocalDashboardStore>(), new(p.GetRequiredService<ILocalStore>(), clock), clock);

    [Fact]
    public async Task AllModulesIntegrateThroughValidatedServicesAndAuthenticatedPages()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope(); var p = scope.ServiceProvider;
        var db = p.GetRequiredService<TRobotDbContext>(); var actor = factory.OrganizerPersonId; var eventId = factory.EventId;
        var clock = new Clock { Utc = new DateTime(2032, 3, 28, 11, 0, 0, DateTimeKind.Utc) };
        var store = p.GetRequiredService<ILocalStore>(); var operations = new LocalOperations(store, clock);
        var attendance = p.GetRequiredService<AttendanceService>();
        var attendanceInput = new SimulatedMessage(eventId, "synthetic-http-sender", "synthetic-completion-presence", "!presenza si");
        await attendance.SimulateAsync(actor, attendanceInput); Assert.True((await attendance.SimulateAsync(actor, attendanceInput)).Duplicate);
        var personId = (await db.EventParticipants.AsNoTracking().SingleAsync()).PersonId;
        var secondId = await p.GetRequiredService<EventManagementService>().AddParticipantAsync(actor, eventId, Guid.NewGuid(), "Secondo partecipante sintetico", false);
        var organizerAttendance = p.GetRequiredService<OrganizerAttendanceService>(); var second = await organizerAttendance.GetAsync(actor, eventId, secondId);
        await organizerAttendance.SetAsync(actor, new(eventId, secondId, Guid.NewGuid(), ParticipationStatus.Confirmed, second.Version, true));
        var households = p.GetRequiredService<HouseholdService>(); var householdId = await households.CreateAsync(actor, eventId, "Nucleo sintetico", Guid.NewGuid());
        await households.AddAsync(actor, eventId, householdId, new(personId, HouseholdMemberKind.Adult, null, 0, 0, true, Guid.NewGuid()));
        await new TaskService(store, operations).CreateAsync(actor, eventId, new("Preparare tavoli", null, TaskPriority.Normal, OrganizationTaskStatus.Todo,
            personId, Local(clock.Utc.AddHours(-1)), 0, false, Guid.NewGuid()));
        await new MenuService(store, operations).SaveAsync(actor, eventId, new(Guid.NewGuid(), "Acqua", MenuCategory.Drink, 2, "litri", null, MenuAvailability.Ready, null, null, false), true);
        var accounting = new AccountingService(store, operations, clock);
        var expenseInput = new ExpenseDeclaration(Guid.NewGuid(), personId, "Spesa sintetica riservata", 10.01m, true);
        var expenseId = await accounting.DeclareExpenseAsync(actor, eventId, expenseInput);
        await accounting.ExpenseTransitionAsync(actor, eventId, expenseId, 0, ExpenseStatus.Approved, true);
        Assert.Equal(expenseId, await accounting.DeclareExpenseAsync(actor, eventId, expenseInput));
        await accounting.DeclareExpenseAsync(actor, eventId, new(Guid.NewGuid(), secondId, "Da valutare", 2m, true));
        var paymentId = await accounting.DeclarePaymentAsync(actor, eventId, new(Guid.NewGuid(), secondId, personId, 1m, true));
        var polls = new PollService(store, operations, clock); var games = new GameService(store, operations, clock); var messages = new MessageService(store, operations, clock);
        var pollId = await polls.SaveAsync(actor, eventId, null, new(Guid.NewGuid(), "Dolce", Local(clock.Utc.AddDays(1)), ["Pandoro", "Panettone"], 0));
        await polls.PublishAsync(actor, eventId, pollId, 0, true);
        await games.MembershipAsync(actor, eventId, personId, 0, true, true);
        var quizId = await games.CreateAsync(actor, eventId, new(Guid.NewGuid(), "Quanto fa due più due?", Local(clock.Utc.AddDays(1)), ["Quattro", "Cinque"], 1));
        await games.PublishAsync(actor, eventId, quizId, 0, true);
        await messages.TickAsync(actor, eventId, true);
        var poll = await polls.ReadAsync(actor, eventId, pollId); var quiz = await games.ReadAsync(actor, eventId, quizId);
        var ballot = new PollBallot(Guid.NewGuid(), personId, poll.Counts[0].Option.Id, poll.Poll.Revision, true);
        await polls.VoteAsync(actor, eventId, pollId, ballot);
        var answer = new QuizBallot(Guid.NewGuid(), personId, quiz.Options[0].Id, quiz.Quiz.Revision, true);
        await games.AnswerAsync(actor, eventId, quizId, answer);
        var overview = await Overview(p, clock).ReadAsync(actor, eventId);
        Assert.Equal(1, overview.Counts.HouseholdMembers); Assert.Equal(1, overview.Counts.MenuReady); Assert.Equal(1, overview.Counts.OverdueTasks);
        Assert.Equal(1, overview.Counts.OpenPolls); Assert.Equal(1, overview.Counts.OpenQuizzes); Assert.Equal(10.01m, overview.Counts.ApprovedExpenses);
        Assert.Equal(1, overview.Counts.PendingExpenses); Assert.Equal(1, overview.Counts.DeclaredPayments);
        await polls.CloseAsync(actor, eventId, pollId, 2, true, ballot.OptionId); await polls.VoteAsync(actor, eventId, pollId, ballot);
        await polls.QueueResultAsync(actor, eventId, pollId, 3, true); await messages.TickAsync(actor, eventId, true);
        await games.CloseAsync(actor, eventId, quizId, 2, true); await games.AnswerAsync(actor, eventId, quizId, answer);
        await accounting.PaymentTransitionAsync(actor, eventId, paymentId, 0, PaymentStatus.Confirmed, true);
        var balances = await accounting.ListAsync(actor, eventId); Assert.Equal(0m, balances.Balances.Sum(x => x.Balance));
        Assert.Equal(1, Assert.Single((await games.ListAsync(actor, eventId)).Scores).Points);
        Assert.Single(await db.Set<PollVoteReceipt>().ToListAsync()); Assert.Single(await db.Set<QuizAnswer>().ToListAsync());
        foreach (var message in await db.Set<OutboundMessage>().ToListAsync())
        {
            Assert.DoesNotContain("Spesa sintetica riservata", message.Body);
            foreach (var person in await db.People.ToListAsync()) Assert.DoesNotContain(person.DisplayName, message.Body);
        }
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }); await Login(client);
        var html = await client.GetStringAsync("/?eventId=" + eventId);
        Assert.Contains("Riepilogo organizzativo", html); Assert.Contains("id=\"confirmed-count\">2", html); Assert.Contains("event-countdown", html);
        foreach (var module in new[] { "Households", "Tasks", "Menu", "Polls", "Accounting", "Games", "Messages", "History" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/" + module + "?eventId=" + eventId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/History?eventId={eventId}&category=Local")).StatusCode);
    }

    [Fact]
    public async Task AggregateSqlUsesExactCentsDeadlineBoundariesAndOnlySentPublications()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope(); var p = scope.ServiceProvider;
        var db = p.GetRequiredService<TRobotDbContext>(); var clock = new Clock(); var actor = factory.OrganizerPersonId; var eventId = factory.EventId;
        var ev = await db.Events.SingleAsync(); ev.UpdateDetails(ev.Name, clock.Utc.AddHours(2), "Europe/Rome");
        var personId = (await db.EventParticipants.SingleAsync()).PersonId;
        foreach (var amount in new[] { 0.1m, 0.2m, 1000000m })
        {
            var expense = new Expense(eventId, Guid.NewGuid(), personId, actor, "Sintetica", amount, clock.Utc); expense.Transition(ExpenseStatus.Approved, true, clock.Utc); db.Add(expense);
        }
        var cancelled = new Expense(eventId, Guid.NewGuid(), personId, actor, "Annullata", 999m, clock.Utc); cancelled.Transition(ExpenseStatus.Cancelled, true, clock.Utc); db.Add(cancelled);
        db.Add(new OrganizationTask(eventId, "Scaduta", null, TaskPriority.Normal, null, clock.Utc.AddTicks(-1)));
        db.Add(new OrganizationTask(eventId, "Al confine", null, TaskPriority.Normal, null, clock.Utc));
        var completed = new OrganizationTask(eventId, "Finita", null, TaskPriority.Normal, null, clock.Utc.AddDays(-1));
        completed.Update(completed.Title, null, TaskPriority.Normal, OrganizationTaskStatus.Completed, null, completed.DueAtUtc); db.Add(completed);
        foreach (var sent in new[] { true, false })
        {
            var message = new OutboundMessage(eventId, Guid.NewGuid(), "Pubblicazione sintetica", MessageKind.Operational, clock.Utc.AddDays(-1)); message.Queue(true); if (sent) message.MarkSent(clock.Utc.AddDays(-1));
            var poll = new Poll(eventId, Guid.NewGuid(), "Scadenza sondaggio", clock.Utc); poll.Publish(message.Id);
            var quizMessage = new OutboundMessage(eventId, Guid.NewGuid(), "Quiz sintetico", MessageKind.Operational, clock.Utc.AddDays(-1)); quizMessage.Queue(true); if (sent) quizMessage.MarkSent(clock.Utc.AddDays(-1));
            var quiz = new Quiz(eventId, Guid.NewGuid(), "Domanda", clock.Utc, 1); quiz.Publish(quizMessage.Id);
            db.AddRange(message, poll, quizMessage, quiz);
        }
        var other = new Event("Evento estraneo", clock.Utc); var outsiderExpense = new Expense(other.Id, Guid.NewGuid(), personId, actor, "Estranea", 500m, clock.Utc);
        outsiderExpense.Transition(ExpenseStatus.Approved, true, clock.Utc); db.AddRange(other, new EventParticipant(other.Id, personId), outsiderExpense);
        await db.SaveChangesAsync();
        var result = await Overview(p, clock).ReadAsync(actor, eventId);
        Assert.Equal(1000000.3m, result.Counts.ApprovedExpenses); Assert.Equal(2, result.Counts.OpenTasks); Assert.Equal(1, result.Counts.OverdueTasks);
        Assert.Equal(0, result.Counts.OpenPolls); Assert.Equal(1, result.Counts.ExpiredPolls); Assert.Equal(0, result.Counts.OpenQuizzes); Assert.Equal(1, result.Counts.ExpiredQuizzes);
        Assert.Equal(2, result.Counts.QueuedMessages); Assert.Equal(TimeSpan.FromHours(2), result.Remaining);
        var start = TimeZoneInfo.ConvertTime(new DateTimeOffset(result.StartsAtUtc), TimeZoneInfo.FindSystemTimeZoneById(result.TimeZoneId));
        Assert.Equal(4, start.Hour); Assert.Equal(TimeSpan.FromHours(2), start.Offset);
        clock.Utc = result.StartsAtUtc; Assert.Equal(TimeSpan.Zero, (await Overview(p, clock).ReadAsync(actor, eventId)).Remaining);
    }

    [Fact]
    public async Task LocalHistorySharesSqlPaginationAndKeepsPayloadsAndForeignEventsPrivate()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope(); var p = scope.ServiceProvider;
        var db = p.GetRequiredService<TRobotDbContext>(); var actor = factory.OrganizerPersonId; var eventId = factory.EventId; var clock = new Clock();
        var opaque = Guid.NewGuid();
        for (var i = 0; i < 26; i++) db.Add(new LocalAudit(eventId, actor, opaque, "Menu", "Created", clock.Utc));
        var personId = (await db.EventParticipants.SingleAsync()).PersonId;
        db.Add(new ParticipationReceipt(eventId, personId, actor, "synthetic-private-message-id", ParticipationStatus.NoResponse, ParticipationStatus.Confirmed, clock.Utc));
        var other = new Event("Estraneo", clock.Utc); db.AddRange(other, new LocalAudit(other.Id, actor, opaque, "Accounting", "ExpenseDeclared", clock.Utc));
        await db.SaveChangesAsync(); var history = p.GetRequiredService<EventHistoryService>();
        var first = await history.GetAsync(actor, eventId); var second = await history.GetAsync(actor, eventId, page: 2);
        Assert.Equal(25, first.Entries.Count); Assert.True(first.HasNext); Assert.Equal(2, second.Entries.Count); Assert.False(second.HasNext);
        Assert.Equal(27, first.Entries.Concat(second.Entries).Select(x => x.Id).Distinct().Count());
        Assert.Equal(first.Entries.Select(x => x.Id), (await history.GetAsync(actor, eventId)).Entries.Select(x => x.Id));
        var local = await history.GetAsync(actor, eventId, HistoryCategory.Local); Assert.Equal(25, local.Entries.Count);
        Assert.All(local.Entries, x => { Assert.Equal("Menu", x.LocalModule); Assert.Null(x.PersonName); });
        Assert.Single((await history.GetAsync(actor, eventId, HistoryCategory.Local, 2)).Entries);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }); await Login(client);
        var html = await client.GetStringAsync($"/History?eventId={eventId}&category=Local");
        Assert.Contains("Menu: Creato", html); Assert.DoesNotContain(opaque.ToString(), html); Assert.DoesNotContain("synthetic-private-message-id", html);
        Assert.DoesNotContain("Spesa dichiarata", html);
        await Assert.ThrowsAsync<HistoryAccessException>(() => history.GetAsync(actor, other.Id, HistoryCategory.Local));
    }

    [Fact]
    public async Task DashboardAndLocalHistoryRejectForeignRevokedAndDisabledActors()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope(); var p = scope.ServiceProvider;
        var db = p.GetRequiredService<TRobotDbContext>(); var actor = factory.OrganizerPersonId; var eventId = factory.EventId; var clock = new Clock();
        var service = Overview(p, clock); var history = p.GetRequiredService<EventHistoryService>();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ReadAsync(actor, Guid.NewGuid()));
        var assignment = await db.EventOrganizers.SingleAsync(); assignment.SetActive(false); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ReadAsync(actor, eventId));
        await Assert.ThrowsAsync<HistoryAccessException>(() => history.GetAsync(actor, eventId, HistoryCategory.Local));
        assignment = await db.EventOrganizers.SingleAsync(); assignment.SetActive(true); var user = await db.Users.SingleAsync(); user.IsEnabled = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ReadAsync(actor, eventId));
        await Assert.ThrowsAsync<HistoryAccessException>(() => history.GetAsync(actor, eventId, HistoryCategory.Local));
    }
    private static async Task Login(HttpClient client)
    {
        var html = await client.GetStringAsync("/Access/Login");
        var token = WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
        var response = await client.PostAsync("/Access/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["UserName"] = LocalWebFactory.UserName, ["Password"] = LocalWebFactory.Password, ["__RequestVerificationToken"] = token }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
