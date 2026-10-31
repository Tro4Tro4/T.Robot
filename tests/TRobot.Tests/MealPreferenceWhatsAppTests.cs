using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Local;
using TRobot.Application.Menu;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed partial class WhatsAppIntegrationTests
{
    private static async Task<(Guid Voter, Guid Child, Guid Cycle)> PrepareMealVote(IServiceProvider p, LocalWebFactory f,
        TRobot.Application.WhatsApp.WhatsAppService whatsapp, Clock clock, int dishes = 2)
    {
        var voter = await Activate(p, f, whatsapp); await SetStayEvent(p, f);
        var db = p.GetRequiredService<TRobotDbContext>();
        var household = new Household(f.EventId, "Nucleo sintetico preferenze", Guid.NewGuid());
        var adult = new HouseholdMember(f.EventId, household.Id, voter, HouseholdMemberKind.Adult, null, Guid.NewGuid());
        var child = new Person("Bambino sintetico preferenze");
        var childMember = new HouseholdMember(f.EventId, household.Id, child.Id, HouseholdMemberKind.Child, adult.Id, Guid.NewGuid());
        var childParticipation = new EventParticipant(f.EventId, child.Id);
        childParticipation.ChangeStatus(ParticipationStatus.Confirmed, Now);
        (await db.EventParticipants.SingleAsync(x => x.EventId == f.EventId)).ChangeStatus(ParticipationStatus.Confirmed, Now);
        var stay = new AttendanceStay(f.EventId, voter); stay.Set(new(2030, 12, 30), 3);
        var childStay = new AttendanceStay(f.EventId, child.Id); childStay.Set(new(2030, 12, 30), 3);
        db.AddRange(household, adult, child, childMember, childParticipation, stay, childStay);
        for (var i = 0; i < dishes; i++) db.Add(new MealProposal(f.EventId, voter, new(2030, 12, 31), MealKind.Dinner,
            $"Piatto {i + 1:D2}", false, Now));
        await db.SaveChangesAsync();
        var store = p.GetRequiredService<ILocalStore>(); var voting = new MealVotingService(store, new(store, clock), clock);
        var id = await voting.StartCycleAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid());
        await voting.CloseCollectionAsync(f.OrganizerPersonId, f.EventId, id, 0);
        var cycle = (await voting.ReadAsync(f.OrganizerPersonId, f.EventId)).CurrentCycle!;
        await voting.OpenAsync(f.OrganizerPersonId, f.EventId, id, cycle.Revision);
        return (voter, child.Id, id);
    }

    private static async Task SelectPreferencePerson(IServiceProvider p, LocalWebFactory f,
        TRobot.Application.WhatsApp.WhatsAppService service, Guid person, string prefix)
    {
        var store = p.GetRequiredService<ILocalStore>(); var clock = new Clock();
        var voting = new MealVotingService(store, new(store, clock), clock);
        var sender = (await store.ListAsync<HouseholdMember>(f.EventId, default)).Single(x => x.Kind == HouseholdMemberKind.Adult).PersonId;
        var people = (await voting.ReadVoterAsync(f.OrganizerPersonId, f.EventId, sender)).OrderBy(x => x.PersonId).ToArray();
        await service.IncomingAsync(Incoming((Array.FindIndex(people, x => x.PersonId == person) + 1).ToString(), prefix + "person") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("31/12/2030", prefix + "day") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("3", prefix + "meal") with { IsPrivate = true });
    }

    [Fact]
    public async Task PreferencesPrivateDialogueStoresSeparateChildBallotOnlyAfterConfirmationAndKeepsOriginalProposals()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock);
        var seed = await PrepareMealVote(p, f, service, clock);
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.False((await service.IncomingAsync(Incoming("!preferenze", "no-autostart") with { IsPrivate = true })).Handled);
        var started = await service.IncomingAsync(Incoming("!preferenze", "pref-start"));
        Assert.True(started.Handled); Assert.True(started.ReplyPrivately);
        await SelectPreferencePerson(p, f, service, seed.Child, "child-");
        var selection = await service.IncomingAsync(Incoming("1,2", "pref-selection") with { IsPrivate = true });
        Assert.Contains("scelto", selection.ReplyText);
        Assert.Empty(await db.Set<MealPreferenceBallot>().ToListAsync());
        await service.IncomingAsync(Incoming("!riepilogo", "pref-review") with { IsPrivate = true });
        var message = Incoming("!conferma", "pref-confirm") with { IsPrivate = true };
        Assert.Contains("Preferenze salvate", (await service.IncomingAsync(message)).ReplyText);
        var ballot = await db.Set<MealPreferenceBallot>().SingleAsync();
        Assert.Equal(seed.Child, ballot.PersonId); Assert.Equal(2, ballot.Selections().Count);
        Assert.True((await service.IncomingAsync(message)).Duplicate);
        Assert.Single(await db.Set<MealPreferenceChange>().ToListAsync());
        Assert.Equal(2, await db.Set<MealProposal>().CountAsync());
        var store = p.GetRequiredService<ILocalStore>(); var voting = new MealVotingService(store, new(store, clock), clock);
        Assert.All((await voting.ReadAsync(f.OrganizerPersonId, f.EventId)).CurrentCounts, x =>
        { Assert.Equal(0, x.Adults); Assert.Equal(1, x.Children); });
    }

    [Theory]
    [InlineData("expiry")]
    [InlineData("stay")]
    [InlineData("child-link")]
    [InlineData("close")]
    public async Task PreferencePendingConfirmationCannotOutliveExpiryEligibilityDelegationOrVoting(string change)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock);
        var seed = await PrepareMealVote(p, f, service, clock);
        await service.IncomingAsync(Incoming("!preferenze", "st-start"));
        await SelectPreferencePerson(p, f, service, seed.Child, "st-");
        await service.IncomingAsync(Incoming("tutti", "st-selection") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("!riepilogo", "st-review") with { IsPrivate = true });
        var db = p.GetRequiredService<TRobotDbContext>();
        if (change == "expiry") clock.Utc = Now.AddMinutes(5);
        if (change == "stay") { (await db.Set<AttendanceStay>().SingleAsync(x => x.Id == seed.Child)).Set(new(2031, 1, 1), 1); await db.SaveChangesAsync(); }
        if (change == "child-link") { (await db.Set<HouseholdMember>().SingleAsync(x => x.PersonId == seed.Child)).Update(HouseholdMemberKind.Child, null, true); await db.SaveChangesAsync(); }
        if (change == "close")
        {
            var store = p.GetRequiredService<ILocalStore>(); var voting = new MealVotingService(store, new(store, clock), clock);
            var cycle = (await voting.ReadAsync(f.OrganizerPersonId, f.EventId)).CurrentCycle!;
            await voting.CloseAsync(f.OrganizerPersonId, f.EventId, cycle.Id, cycle.Revision);
        }
        await service.IncomingAsync(Incoming("!conferma", "st-confirm") with { IsPrivate = true, TimestampUnixSeconds = new DateTimeOffset(clock.Utc).ToUnixTimeSeconds() });
        Assert.Empty(await db.Set<MealPreferenceBallot>().ToListAsync());
    }

    [Fact]
    public async Task PreferenceReviewRequiresEveryPageAndDoesNotExtendFiveMinuteDeadline()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock);
        var seed = await PrepareMealVote(p, f, service, clock, 7);
        await service.IncomingAsync(Incoming("!preferenze", "pg-start"));
        await SelectPreferencePerson(p, f, service, seed.Voter, "pg-");
        await service.IncomingAsync(Incoming("tutti", "pg-all") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("!riepilogo", "pg-review") with { IsPrivate = true });
        Assert.Contains("tutte le pagine", (await service.IncomingAsync(Incoming("!conferma", "pg-too-early") with { IsPrivate = true })).ReplyText);
        var db = p.GetRequiredService<TRobotDbContext>(); Assert.Empty(await db.Set<MealPreferenceBallot>().ToListAsync());
        var page = await service.IncomingAsync(Incoming("!pagina 2", "pg-second") with { IsPrivate = true });
        Assert.Contains("Pagina 2/2", page.ReplyText); Assert.True(page.ReplyText!.Length <= 2000);
        Assert.Equal(Now.AddMinutes(5), (await db.Set<MealPreferenceSession>().SingleAsync()).ExpiresAtUtc);
        await service.IncomingAsync(Incoming("!conferma", "pg-save") with { IsPrivate = true });
        Assert.Equal(7, (await db.Set<MealPreferenceBallot>().SingleAsync()).Selections().Count);
    }

    [Fact]
    public async Task CollectionClosureBlocksInFlightProposalAndPreferenceDialogueCannotOverlapProposal()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock);
        await Activate(p, f, service); await SetStayEvent(p, f);
        await service.IncomingAsync(Incoming("!proponi", "ov-start"));
        await service.IncomingAsync(Incoming("31/12", "ov-day") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("cena", "ov-meal") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("Lasagne", "ov-dish") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("1", "ov-offer") with { IsPrivate = true });
        Assert.Contains("proposta aperta", (await service.IncomingAsync(Incoming("!preferenze", "ov-block"))).ReplyText);
        var db = p.GetRequiredService<TRobotDbContext>();
        var voter = (await db.EventParticipants.SingleAsync(x => x.EventId == f.EventId)).PersonId;
        db.Add(new MealProposal(f.EventId, voter, new(2030, 12, 31), MealKind.Dinner, "Fonte gia confermata", false, Now));
        await db.SaveChangesAsync();
        var store = p.GetRequiredService<ILocalStore>(); var voting = new MealVotingService(store, new(store, clock), clock);
        var cycle = await voting.StartCycleAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid());
        await voting.CloseCollectionAsync(f.OrganizerPersonId, f.EventId, cycle, 0);
        Assert.Contains("raccolta delle proposte e chiusa", (await service.IncomingAsync(Incoming("!conferma", "ov-confirm") with { IsPrivate = true })).ReplyText);
        Assert.Equal("Fonte gia confermata", (await db.Set<MealProposal>().SingleAsync()).DishName);
    }

    [Fact]
    public async Task PreferenceStartExplainsMissingAdultClassificationWithoutOpeningDialogueOrAcceptingOrdinaryDm()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock);
        var seed = await PrepareMealVote(p, f, service, clock);
        var db = p.GetRequiredService<TRobotDbContext>();
        var member = await db.Set<HouseholdMember>().SingleAsync(x => x.PersonId == seed.Voter);
        member.Update(HouseholdMemberKind.Adult, null, false); await db.SaveChangesAsync();
        var reply = await service.IncomingAsync(Incoming("!preferenze", "missing-classification"));
        Assert.True(reply.Handled); Assert.True(reply.ReplyPrivately); Assert.Contains("organizzatori", reply.ReplyText);
        Assert.Empty(await db.Set<MealPreferenceSession>().ToListAsync());
        Assert.False((await service.IncomingAsync(Incoming("ciao", "ordinary-private") with { IsPrivate = true })).Handled);
    }

    [Fact]
    public async Task EmptyCollectionCannotBeClosedAndRemainsAvailableForProposals()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service);
        var store = p.GetRequiredService<ILocalStore>(); var voting = new MealVotingService(store, new(store, clock), clock);
        var id = await voting.StartCycleAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid());
        await Assert.ThrowsAsync<LocalValidationException>(() => voting.CloseCollectionAsync(f.OrganizerPersonId, f.EventId, id, 0));
        Assert.Equal(MealVotingState.Collection, (await voting.ReadAsync(f.OrganizerPersonId, f.EventId)).CurrentCycle!.State);
        var response = await service.IncomingAsync(Incoming("!proponi", "empty-collection-still-open"));
        Assert.True(response.Handled); Assert.Contains("quale giorno", response.ReplyText);
    }
}
