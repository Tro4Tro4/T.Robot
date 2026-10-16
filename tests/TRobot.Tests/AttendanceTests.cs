using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TRobot.Application.Attendance;
using TRobot.Application.Demo;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class AttendanceTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
    private readonly FailingSaveInterceptor interceptor = new();
    private readonly TRobotDbContext db;
    private readonly AttendanceService service;
    private readonly Event ev = new("Evento test", DateTime.UtcNow);
    private readonly Person organizer = new("Organizzatore test");
    private readonly Person sender = new("Partecipante test", "synthetic-sender");

    public AttendanceTests()
    {
        connection.Open();
        db = CreateContext();
        db.Database.Migrate();
        db.AddRange(ev, organizer, sender, new EventOrganizer(ev.Id, organizer.Id), new EventParticipant(ev.Id, sender.Id));
        db.SaveChanges();
        service = new(new AttendanceStore(db), new FixedClock());
    }

    private TRobotDbContext CreateContext() => new(new DbContextOptionsBuilder<TRobotDbContext>()
        .UseSqlite(connection).AddInterceptors(interceptor).Options);

    private SimulatedMessage Message(string id = "message-1", string text = "!presenza si", string senderId = "synthetic-sender") =>
        new(ev.Id, senderId, id, text);

    [Theory]
    [InlineData("!presenza si", ParticipationStatus.Confirmed)]
    [InlineData(" !PRESENZA SÌ  ", ParticipationStatus.Confirmed)]
    [InlineData("!presenza\tSi", ParticipationStatus.Confirmed)]
    [InlineData("!presenza forse", ParticipationStatus.Undecided)]
    [InlineData(" !PRESENZA\tFORSE ", ParticipationStatus.Undecided)]
    [InlineData("!presenza no", ParticipationStatus.Absent)]
    [InlineData(" !PRESENZA NO ", ParticipationStatus.Absent)]
    public void ParserAcceptsOnePersonStatus(string text, ParticipationStatus expected) =>
        Assert.Equal(expected, AttendanceService.Parse(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("!presenza no 2")]
    [InlineData("!presenza forse si")]
    [InlineData("!presenza si 2 adulti")]
    [InlineData("!presenza si forse")]
    [InlineData("confermo")]
    public void ParserRejectsUnsupportedOrAmbiguousInput(string? text) =>
        Assert.Throws<AttendanceValidationException>(() => AttendanceService.Parse(text));

    [Theory]
    [InlineData(ParticipationStatus.NoResponse, "si", ParticipationStatus.Confirmed)]
    [InlineData(ParticipationStatus.NoResponse, "forse", ParticipationStatus.Undecided)]
    [InlineData(ParticipationStatus.NoResponse, "no", ParticipationStatus.Absent)]
    [InlineData(ParticipationStatus.Confirmed, "si", ParticipationStatus.Confirmed)]
    [InlineData(ParticipationStatus.Confirmed, "forse", ParticipationStatus.Undecided)]
    [InlineData(ParticipationStatus.Confirmed, "no", ParticipationStatus.Absent)]
    [InlineData(ParticipationStatus.Undecided, "si", ParticipationStatus.Confirmed)]
    [InlineData(ParticipationStatus.Undecided, "forse", ParticipationStatus.Undecided)]
    [InlineData(ParticipationStatus.Undecided, "no", ParticipationStatus.Absent)]
    [InlineData(ParticipationStatus.Absent, "si", ParticipationStatus.Confirmed)]
    [InlineData(ParticipationStatus.Absent, "forse", ParticipationStatus.Undecided)]
    [InlineData(ParticipationStatus.Absent, "no", ParticipationStatus.Absent)]
    public async Task StatusTransitionsPreserveHistoryAndTotal(ParticipationStatus initial, string answer, ParticipationStatus expected)
    {
        var participant = await db.EventParticipants.SingleAsync();
        participant.ChangeStatus(initial, FixedClock.Now.AddMinutes(-1).UtcDateTime);
        await db.SaveChangesAsync();
        var previousTimestamp = participant.UpdatedAtUtc;
        var result = await service.SimulateAsync(organizer.Id, Message(text: "!presenza " + answer));
        Assert.Equal(initial != expected, result.Changed);
        Assert.Equal(expected, result.Status);
        Assert.False(result.Duplicate);
        db.ChangeTracker.Clear();
        var persisted = await db.EventParticipants.SingleAsync();
        Assert.Equal(expected, persisted.Status);
        Assert.Equal(initial != expected ? FixedClock.Now.UtcDateTime : previousTimestamp, persisted.UpdatedAtUtc);
        var receipt = await db.ParticipationReceipts.SingleAsync();
        Assert.Equal(initial, receipt.PreviousStatus);
        Assert.Equal(expected, receipt.Status);
        var counts = await service.GetDashboardAsync(organizer.Id, ev.Id);
        Assert.Equal(expected == ParticipationStatus.Confirmed ? 1 : 0, counts.Confirmed);
        Assert.Equal(expected == ParticipationStatus.Undecided ? 1 : 0, counts.Undecided);
        Assert.Equal(expected == ParticipationStatus.Absent ? 1 : 0, counts.Absent);
        Assert.Equal(0, counts.NoResponse);
        Assert.Equal(1, counts.Total);
    }

    [Theory]
    [InlineData("si", "forse", ParticipationStatus.Undecided)]
    [InlineData("si", "no", ParticipationStatus.Absent)]
    [InlineData("forse", "si", ParticipationStatus.Confirmed)]
    [InlineData("forse", "no", ParticipationStatus.Absent)]
    [InlineData("no", "si", ParticipationStatus.Confirmed)]
    [InlineData("no", "forse", ParticipationStatus.Undecided)]
    public async Task ReplayingOlderMessageAfterNewContextDoesNotRestorePreviousStatus(string first, string latest, ParticipationStatus expected)
    {
        var oldMessage = Message("old-message", "!presenza " + first);
        await service.SimulateAsync(organizer.Id, oldMessage);
        await service.SimulateAsync(organizer.Id, Message("new-message", "!presenza " + latest));
        await using var otherDb = CreateContext();
        var otherService = new AttendanceService(new AttendanceStore(otherDb), new FixedClock());
        var result = await otherService.SimulateAsync(organizer.Id, oldMessage);
        Assert.True(result.Duplicate);
        Assert.False(result.Changed);
        Assert.Equal(expected, result.Status);
        Assert.Equal(expected, (await otherDb.EventParticipants.SingleAsync()).Status);
        Assert.Equal(2, await otherDb.ParticipationReceipts.CountAsync());
    }

    [Theory]
    [InlineData("si", "forse", ParticipationStatus.Confirmed)]
    [InlineData("si", "no", ParticipationStatus.Confirmed)]
    [InlineData("forse", "si", ParticipationStatus.Undecided)]
    [InlineData("forse", "no", ParticipationStatus.Undecided)]
    [InlineData("no", "si", ParticipationStatus.Absent)]
    [InlineData("no", "forse", ParticipationStatus.Absent)]
    public async Task ReusingMessageIdWithDifferentStatusIsRejected(string first, string second, ParticipationStatus expected)
    {
        await service.SimulateAsync(organizer.Id, Message(text: "!presenza " + first));
        await Assert.ThrowsAsync<AttendanceConflictException>(() => service.SimulateAsync(organizer.Id, Message(text: "!presenza " + second)));
        Assert.Equal(expected, (await db.EventParticipants.SingleAsync()).Status);
        Assert.Single(await db.ParticipationReceipts.ToListAsync());
    }

    [Fact]
    public async Task ConfirmationPersistsHistoryAndUpdatesDashboard()
    {
        var result = await service.SimulateAsync(organizer.Id, Message());
        Assert.Equal(new SimulationResult(false, true, ParticipationStatus.Confirmed), result);
        db.ChangeTracker.Clear();
        var participant = await db.EventParticipants.SingleAsync();
        Assert.Equal(ParticipationStatus.Confirmed, participant.Status);
        Assert.Equal(FixedClock.Now.UtcDateTime, participant.UpdatedAtUtc);
        Assert.Equal(DateTimeKind.Utc, participant.UpdatedAtUtc!.Value.Kind);
        var receipt = await db.ParticipationReceipts.SingleAsync();
        Assert.Equal(ParticipationStatus.NoResponse, receipt.PreviousStatus);
        Assert.Equal(ParticipationStatus.Confirmed, receipt.Status);
        Assert.Equal(organizer.Id, receipt.OrganizerPersonId);
        Assert.Equal(DateTimeKind.Utc, receipt.ProcessedAtUtc.Kind);
        var counts = await service.GetDashboardAsync(organizer.Id, ev.Id);
        Assert.Equal(1, counts.Confirmed);
        Assert.Equal(0, counts.NoResponse);
        Assert.Equal(1, counts.Total);
    }

    [Fact]
    public async Task ReplayAfterNewContextDoesNotDuplicateHistory()
    {
        await service.SimulateAsync(organizer.Id, Message());
        await using var otherDb = CreateContext();
        var otherService = new AttendanceService(new AttendanceStore(otherDb), new FixedClock());
        Assert.Equal(new SimulationResult(true, false, ParticipationStatus.Confirmed), await otherService.SimulateAsync(organizer.Id, Message()));
        Assert.Equal(1, await otherDb.ParticipationReceipts.CountAsync());
    }

    [Fact]
    public async Task AnotherMessageFromSamePersonDoesNotIncreaseCounts()
    {
        await service.SimulateAsync(organizer.Id, Message());
        var result = await service.SimulateAsync(organizer.Id, Message("message-2"));
        Assert.Equal(new SimulationResult(false, false, ParticipationStatus.Confirmed), result);
        Assert.Equal(2, await db.ParticipationReceipts.CountAsync());
        Assert.Equal(1, (await service.GetDashboardAsync(organizer.Id, ev.Id)).Confirmed);
    }

    [Fact]
    public async Task ReusingMessageIdForAnotherSenderIsRejected()
    {
        var second = new Person("Secondo test", "synthetic-second");
        db.AddRange(second, new EventParticipant(ev.Id, second.Id));
        await db.SaveChangesAsync();
        await service.SimulateAsync(organizer.Id, Message());
        await Assert.ThrowsAsync<AttendanceConflictException>(() => service.SimulateAsync(organizer.Id, Message(senderId: "synthetic-second")));
        Assert.Equal(ParticipationStatus.NoResponse, (await db.EventParticipants.SingleAsync(x => x.PersonId == second.Id)).Status);
        Assert.Equal(1, await db.ParticipationReceipts.CountAsync());
    }

    [Theory]
    [InlineData("unknown-sender", "!presenza si")]
    [InlineData("synthetic-sender", "!presenza si 3")]
    public async Task InvalidCommandOrSenderChangesNothing(string senderId, string text)
    {
        await Assert.ThrowsAsync<AttendanceValidationException>(() => service.SimulateAsync(organizer.Id, Message(text: text, senderId: senderId)));
        Assert.Empty(await db.ParticipationReceipts.ToListAsync());
        Assert.Equal(ParticipationStatus.NoResponse, (await db.EventParticipants.SingleAsync()).Status);
    }

    [Fact]
    public async Task NonOrganizerCannotReadOrWrite()
    {
        await Assert.ThrowsAsync<AttendanceAccessException>(() => service.SimulateAsync(sender.Id, Message()));
        await Assert.ThrowsAsync<AttendanceAccessException>(() => service.GetDashboardAsync(sender.Id, ev.Id));
        Assert.Empty(await service.GetEventsAsync(sender.Id));
        Assert.Empty(await db.ParticipationReceipts.ToListAsync());
    }

    [Fact]
    public async Task OrganizerOfDifferentEventCannotReadOrWrite()
    {
        var another = new Event("Altro evento", DateTime.UtcNow);
        db.Add(another);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AttendanceAccessException>(() => service.SimulateAsync(organizer.Id, Message() with { EventId = another.Id }));
        await Assert.ThrowsAsync<AttendanceAccessException>(() => service.GetDashboardAsync(organizer.Id, another.Id));
    }

    [Fact]
    public async Task RollbackKeepsBothParticipationAndHistoryUnchanged()
    {
        interceptor.Fail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SimulateAsync(organizer.Id, Message()));
        interceptor.Fail = false;
        await using var verify = CreateContext();
        Assert.Equal(ParticipationStatus.NoResponse, (await verify.EventParticipants.SingleAsync()).Status);
        Assert.Empty(await verify.ParticipationReceipts.ToListAsync());
        Assert.True((await service.SimulateAsync(organizer.Id, Message())).Changed);
    }

    [Fact]
    public async Task DemoSetupRejectsExistingDataAndUnauthorizedCaller()
    {
        var demo = new DemoSetupService(new AttendanceStore(db));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => demo.InitializeAsync(false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => demo.InitializeAsync(true));
        Assert.Equal(1, await db.Events.CountAsync());
        Assert.Equal(2, await db.People.CountAsync());
    }

    [Fact]
    public async Task DatabaseEnforcesMessageUniquenessIndependentlyOfService()
    {
        await service.SimulateAsync(organizer.Id, Message());
        var ex = await Assert.ThrowsAsync<SqliteException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO ParticipationReceipts (Id, EventId, PersonId, OrganizerPersonId, MessageId, PreviousStatus, Status, ProcessedAtUtc) SELECT {Guid.NewGuid()}, EventId, PersonId, OrganizerPersonId, MessageId, PreviousStatus, Status, ProcessedAtUtc FROM ParticipationReceipts"));
        Assert.Equal(2067, ex.SqliteExtendedErrorCode);
    }

    [Fact]
    public async Task ConcurrentStateChangeCannotOverwriteNewerStatus()
    {
        await using var otherDb = CreateContext();
        var stale = await otherDb.EventParticipants.SingleAsync();
        await service.SimulateAsync(organizer.Id, Message());
        stale.ChangeStatus(ParticipationStatus.Absent, DateTime.UtcNow);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => otherDb.SaveChangesAsync());
        db.ChangeTracker.Clear();
        Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync()).Status);
    }

    [Fact]
    public async Task DashboardCountsAllStatesAndOnlyTheRequestedEvent()
    {
        foreach (var status in new[] { ParticipationStatus.Confirmed, ParticipationStatus.Undecided, ParticipationStatus.Absent })
        {
            var person = new Person("Conteggio " + status);
            var participant = new EventParticipant(ev.Id, person.Id);
            participant.ChangeStatus(status, DateTime.UtcNow);
            db.AddRange(person, participant);
        }
        var otherEvent = new Event("Altro evento conteggi", DateTime.UtcNow);
        var otherParticipation = new EventParticipant(otherEvent.Id, sender.Id);
        otherParticipation.ChangeStatus(ParticipationStatus.Confirmed, DateTime.UtcNow);
        db.AddRange(otherEvent, otherParticipation);
        await db.SaveChangesAsync();
        var dashboard = await service.GetDashboardAsync(organizer.Id, ev.Id);
        Assert.Equal(1, dashboard.Confirmed);
        Assert.Equal(1, dashboard.Undecided);
        Assert.Equal(1, dashboard.Absent);
        Assert.Equal(1, dashboard.NoResponse);
        Assert.Equal(4, dashboard.Total);
        Assert.Single(dashboard.Senders);
    }

    [Fact]
    public void StatusChangeRejectsInvalidEnumAndNonUtcTimestamp()
    {
        var participant = new EventParticipant(ev.Id, sender.Id);
        Assert.Throws<ArgumentOutOfRangeException>(() => participant.ChangeStatus((ParticipationStatus)100, DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => participant.ChangeStatus(ParticipationStatus.Confirmed, DateTime.Now));
        Assert.True(participant.ChangeStatus(ParticipationStatus.Confirmed, FixedClock.Now.UtcDateTime));
        Assert.False(participant.ChangeStatus(ParticipationStatus.Confirmed, FixedClock.Now.AddDays(1).UtcDateTime));
        Assert.Equal(FixedClock.Now.UtcDateTime, participant.UpdatedAtUtc);
    }

    public void Dispose() { db.Dispose(); connection.Dispose(); }

    private sealed class FixedClock : TimeProvider
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            Fail ? throw new InvalidOperationException("Errore di test dopo la scrittura, prima del commit.") : ValueTask.FromResult(result);
    }
}
