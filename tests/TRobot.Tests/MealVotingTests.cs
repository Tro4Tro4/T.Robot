using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TRobot.Application.Local;
using TRobot.Application.Menu;
using TRobot.Domain;
using TRobot.Infrastructure.Identity;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class MealVotingTests
{
    private static readonly DateOnly Day = new(2030, 12, 31);
    private static readonly DateTime Utc = new(2030, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PreparationMergesOnlySameMealPreservesOriginalsAndFreezesPublication()
    {
        await using var h = await Harness.Create();
        var lunch = new MealProposal(h.EventId, h.AdultId, Day, MealKind.Lunch, "Altro pasto", true, Utc);
        h.Db.Add(lunch); await h.Db.SaveChangesAsync();
        await h.Prepare();
        var page = await h.Page(); var cycle = page.CurrentCycle!; var entries = page.Entries.Where(x => x.Meal == MealKind.Dinner).ToArray();
        await Assert.ThrowsAsync<LocalValidationException>(() => h.Service.SaveEntryAsync(h.ActorId, h.EventId,
            new(entries[0].Id, cycle.Id, "Errato", [h.FirstId, lunch.Id], false, cycle.Revision, entries[0].Revision)));
        await h.Service.SaveEntryAsync(h.ActorId, h.EventId, new(entries[0].Id, cycle.Id, "Pasta accorpata", [h.FirstId, h.SecondId], false, cycle.Revision, entries[0].Revision));
        page = await h.Page();
        Assert.Equal(3, await h.Db.Set<MealProposal>().CountAsync());
        Assert.True((await h.Db.Set<MealProposal>().SingleAsync(x => x.Id == h.FirstId)).OffersToCook);
        Assert.Equal(2, page.Sources.Count(x => x.EntryId == entries[0].Id));
        await h.Service.OpenAsync(h.ActorId, h.EventId, cycle.Id, page.CurrentCycle!.Revision);
        var ballot = await h.Ballot(h.AdultId);
        Assert.Single(ballot.Entries); Assert.Equal("Pasta accorpata", ballot.Entries[0].Name);
        await Assert.ThrowsAsync<LocalValidationException>(() => h.Service.SaveEntryAsync(h.ActorId, h.EventId,
            new(entries[0].Id, cycle.Id, "Correzione tardiva", [h.FirstId, h.SecondId], false, ballot.Cycle.Revision, 1)));
        await Assert.ThrowsAsync<LocalConflictException>(() => h.Service.StartCycleAsync(h.ActorId, h.EventId, Guid.NewGuid()));
    }

    [Fact]
    public async Task PreferencesReplaceAndClearWithIdempotentReceiptsAndStaleProtection()
    {
        await using var h = await Harness.Create(); await h.Open();
        var ballot = await h.Ballot(h.AdultId); var input = Input(ballot, ballot.Entries.Select(x => x.Id).Concat([ballot.Entries[0].Id]).ToArray());
        Assert.Equal(1, await h.Service.SavePreferencesAsync(h.ActorId, h.EventId, input));
        Assert.Equal(1, await h.Service.SavePreferencesAsync(h.ActorId, h.EventId, input));
        await Assert.ThrowsAsync<LocalConflictException>(() => h.Service.SavePreferencesAsync(h.ActorId, h.EventId, input with { EntryIds = [] }));
        await Assert.ThrowsAsync<LocalConflictException>(() => h.Service.SavePreferencesAsync(h.ActorId, h.EventId, input with { OperationId = Guid.NewGuid() }));
        var saved = await h.Ballot(h.AdultId); Assert.Equal(2, saved.SelectedEntryIds.Count);
        Assert.Equal(2, await h.Service.SavePreferencesAsync(h.ActorId, h.EventId, Input(saved, [])));
        Assert.Empty((await h.Ballot(h.AdultId)).SelectedEntryIds);
        Assert.Equal(2, await h.Db.Set<MealPreferenceChange>().CountAsync());
        var changes = await h.Db.Set<MealPreferenceChange>().ToArrayAsync();
        Assert.All(changes, x => { Assert.Equal(h.AdultId, x.VoterId); Assert.Equal(h.ActorId, x.ActorId); });
        Assert.All((await h.Page()).CurrentCounts, x => Assert.Equal(0, x.Total));
    }

    [Fact]
    public async Task ParentCanVoteForChildOutsideOwnStayButNeverForOtherAdultsOrUnlinkedChildren()
    {
        await using var h = await Harness.Create(); await h.Open();
        var adultStay = await h.Db.Set<AttendanceStay>().SingleAsync(x => x.Id == h.AdultId); adultStay.Set(Day.AddDays(-1), 1); await h.Db.SaveChangesAsync();
        Assert.False((await h.Ballot(h.AdultId)).CanVote);
        var child = await h.Ballot(h.ChildId); Assert.True(child.CanVote);
        await h.Service.SavePreferencesAsync(h.ActorId, h.EventId, Input(child, [child.Entries[0].Id]));
        await Assert.ThrowsAsync<LocalAccessException>(() => h.Ballot(h.OtherAdultId));
        var membership = await h.Db.Set<HouseholdMember>().SingleAsync(x => x.PersonId == h.ChildId);
        membership.Update(HouseholdMemberKind.Child, h.OtherAdultMemberId, true); await h.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => h.Ballot(h.ChildId));
        var newParent = await h.Service.ReadBallotAsync(h.ActorId, h.EventId, h.OtherAdultId, h.ChildId, Day, MealKind.Dinner);
        Assert.Single(newParent.SelectedEntryIds);
        Assert.DoesNotContain(await h.Service.ReadVoterAsync(h.ActorId, h.EventId, h.AdultId), x => x.PersonId == h.ChildId);
    }

    [Fact]
    public async Task StayBoundariesFingerprintAndClassificationAreRecheckedAtConfirmation()
    {
        await using var h = await Harness.Create(); await h.Open();
        Assert.True((await h.Service.ReadBallotAsync(h.ActorId, h.EventId, h.AdultId, h.AdultId, Day, MealKind.Dinner)).CanVote);
        var old = await h.Ballot(h.AdultId);
        var stay = await h.Db.Set<AttendanceStay>().SingleAsync(x => x.Id == h.AdultId); stay.Set(Day.AddDays(-1), 2); await h.Db.SaveChangesAsync();
        Assert.True((await h.Ballot(h.AdultId)).CanVote);
        await Assert.ThrowsAsync<LocalConflictException>(() => h.Service.SavePreferencesAsync(h.ActorId, h.EventId, Input(old, [old.Entries[0].Id])));
        var fresh = await h.Ballot(h.AdultId);
        stay = await h.Db.Set<AttendanceStay>().SingleAsync(x => x.Id == h.AdultId);
        stay.Set(Day.AddDays(-1), 1); await h.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalConflictException>(() => h.Service.SavePreferencesAsync(h.ActorId, h.EventId, Input(fresh, [fresh.Entries[0].Id])));
        var member = await h.Db.Set<HouseholdMember>().SingleAsync(x => x.PersonId == h.AdultId); member.Update(HouseholdMemberKind.Child, null, true); await h.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => h.Ballot(h.AdultId));
    }

    [Fact]
    public async Task CountsExcludeAndRestoreCurrentPresenceWhileClosureSnapshotAndHistoryStayFixed()
    {
        await using var h = await Harness.Create(); await h.Open();
        var adult = await h.Ballot(h.AdultId); var entry = adult.Entries[0].Id;
        await h.Service.SavePreferencesAsync(h.ActorId, h.EventId, Input(adult, [entry]));
        var child = await h.Ballot(h.ChildId); await h.Service.SavePreferencesAsync(h.ActorId, h.EventId, Input(child, [entry]));
        var page = await h.Page(); var count = page.CurrentCounts.Single(x => x.EntryId == entry);
        Assert.Equal((1, 1, 2), (count.Adults, count.Children, count.Total));
        await h.Service.CloseAsync(h.ActorId, h.EventId, adult.Cycle.Id, page.CurrentCycle!.Revision);
        var frozen = (await h.Page()).CurrentCycle!.ClosedSnapshotJson;
        var participant = await h.Db.EventParticipants.SingleAsync(x => x.PersonId == h.ChildId); participant.ChangeStatus(ParticipationStatus.Absent, Utc); await h.Db.SaveChangesAsync();
        page = await h.Page(); count = page.CurrentCounts.Single(x => x.EntryId == entry);
        Assert.Equal(1, count.Total); Assert.Single(count.Excluded); Assert.Equal(2, page.ClosedCounts.Single(x => x.EntryId == entry).Total);
        Assert.Equal(frozen, page.CurrentCycle!.ClosedSnapshotJson);
        participant.ChangeStatus(ParticipationStatus.Confirmed, Utc); await h.Db.SaveChangesAsync();
        Assert.Equal(2, (await h.Page()).CurrentCounts.Single(x => x.EntryId == entry).Total);
        await Assert.ThrowsAsync<LocalConflictException>(() => h.Service.SavePreferencesAsync(h.ActorId, h.EventId, Input(adult, [])));
        await h.Service.StartCycleAsync(h.ActorId, h.EventId, Guid.NewGuid());
        page = await h.Page(); Assert.Equal(2, page.CurrentCycle!.Number); Assert.Equal(2, page.Cycles.Count);
        Assert.Equal(frozen, page.Cycles.Single(x => x.Id == adult.Cycle.Id).ClosedSnapshotJson);
        Assert.Equal(2, await h.Db.Set<MealPreferenceBallot>().CountAsync());
    }

    [Fact]
    public async Task AuthorizationAndExcludedEntryCannotBeBypassed()
    {
        await using var h = await Harness.Create(); await h.Prepare();
        var page = await h.Page(); var entry = page.Entries[0];
        await h.Service.SaveEntryAsync(h.ActorId, h.EventId, new(entry.Id, page.CurrentCycle!.Id, entry.Name,
            page.Sources.Where(x => x.EntryId == entry.Id).Select(x => x.ProposalId).ToArray(), true, page.CurrentCycle.Revision, entry.Revision));
        page = await h.Page(); await h.Service.OpenAsync(h.ActorId, h.EventId, page.CurrentCycle!.Id, page.CurrentCycle.Revision);
        var ballot = await h.Ballot(h.AdultId); Assert.Single(ballot.Entries);
        await Assert.ThrowsAsync<LocalValidationException>(() => h.Service.SavePreferencesAsync(h.ActorId, h.EventId, Input(ballot, [entry.Id])));
        await Assert.ThrowsAsync<LocalAccessException>(() => h.Service.ReadAsync(h.AdultId, h.EventId));
        await Assert.ThrowsAsync<LocalAccessException>(() => h.Service.ReadAsync(h.ActorId, Guid.NewGuid()));
        Assert.Empty(await h.Db.Set<MealPreferenceBallot>().ToArrayAsync());
    }

    private static MealPreferenceInput Input(MealBallotPage b, IReadOnlyList<Guid> entries) =>
        new(Guid.NewGuid(), b.Cycle.Id, b.Cycle.Revision, b.VoterId, b.PersonId, b.Day, b.Meal, entries, b.BallotRevision, b.EligibilityFingerprint);

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:;Foreign Keys=True");
        public TRobotDbContext Db { get; private set; } = null!;
        public MealVotingService Service { get; private set; } = null!;
        public Guid EventId { get; private set; }
        public Guid ActorId { get; private set; }
        public Guid AdultId { get; private set; }
        public Guid ChildId { get; private set; }
        public Guid OtherAdultId { get; private set; }
        public Guid OtherAdultMemberId { get; private set; }
        public Guid FirstId { get; private set; }
        public Guid SecondId { get; private set; }
        public static async Task<Harness> Create()
        {
            var h = new Harness(); await h.connection.OpenAsync();
            h.Db = new(new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(h.connection).Options); await h.Db.Database.EnsureCreatedAsync();
            var ev = new Event("Preferenze sintetiche", Utc); var actor = new Person("Regia"); var adult = new Person("Adulto"); var child = new Person("Figlio"); var other = new Person("Altro adulto");
            h.EventId = ev.Id; h.ActorId = actor.Id; h.AdultId = adult.Id; h.ChildId = child.Id; h.OtherAdultId = other.Id;
            var household = new Household(ev.Id, "Nucleo", Guid.NewGuid()); var member = new HouseholdMember(ev.Id, household.Id, adult.Id, HouseholdMemberKind.Adult, null, Guid.NewGuid());
            var otherMember = new HouseholdMember(ev.Id, household.Id, other.Id, HouseholdMemberKind.Adult, null, Guid.NewGuid()); h.OtherAdultMemberId = otherMember.Id;
            var childMember = new HouseholdMember(ev.Id, household.Id, child.Id, HouseholdMemberKind.Child, member.Id, Guid.NewGuid());
            h.Db.AddRange(ev, actor, adult, child, other, household, member, otherMember, childMember,
                new EventOrganizer(ev.Id, actor.Id), new OrganizerUser { Id = Guid.NewGuid(), PersonId = actor.Id, UserName = "synthetic-regia" });
            foreach (var p in new[] { adult, child, other })
            { var participation = new EventParticipant(ev.Id, p.Id); participation.ChangeStatus(ParticipationStatus.Confirmed, Utc); var stay = new AttendanceStay(ev.Id, p.Id); stay.Set(Day, 1); h.Db.AddRange(participation, stay); }
            var first = new MealProposal(ev.Id, adult.Id, Day, MealKind.Dinner, "Pasta", true, Utc);
            var second = new MealProposal(ev.Id, adult.Id, Day, MealKind.Dinner, "Pasta doppia", false, Utc);
            h.FirstId = first.Id; h.SecondId = second.Id; h.Db.AddRange(first, second); await h.Db.SaveChangesAsync();
            var clock = new FixedClock(); var store = new LocalStore(h.Db, clock); h.Service = new(store, new(store, clock), clock); return h;
        }
        public Task<MealVotingPage> Page() => Service.ReadAsync(ActorId, EventId);
        public Task<MealBallotPage> Ballot(Guid target) => Service.ReadBallotAsync(ActorId, EventId, AdultId, target, Day, MealKind.Dinner);
        public async Task Prepare() { var id = Guid.NewGuid(); await Service.StartCycleAsync(ActorId, EventId, id); await Service.CloseCollectionAsync(ActorId, EventId, id, 0); }
        public async Task Open() { await Prepare(); var cycle = (await Page()).CurrentCycle!; await Service.OpenAsync(ActorId, EventId, cycle.Id, cycle.Revision); }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
    }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Utc); }
}
