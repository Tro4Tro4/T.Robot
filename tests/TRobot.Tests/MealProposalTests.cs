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
    private static async Task ConfirmMealProposalStay(IServiceProvider p, Guid eventId)
    {
        var db = p.GetRequiredService<TRobotDbContext>();
        var participant = await db.EventParticipants.SingleAsync(x => x.EventId == eventId);
        participant.ChangeStatus(ParticipationStatus.Confirmed, Now);
        var stay = new AttendanceStay(eventId, participant.PersonId); stay.Set(new(2030, 12, 30), 3);
        db.Add(stay); await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("30/12", true)]
    [InlineData("31/12", true)]
    [InlineData("01/01", true)]
    [InlineData("29/12", false)]
    [InlineData("02/01", false)]
    public async Task MealProposalsAllowOnlyPersonalConfirmedStayIncludingBothBoundaries(string day, bool allowed)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        await ConfirmMealProposalStay(p, f.EventId);
        var db = p.GetRequiredService<TRobotDbContext>();
        await service.IncomingAsync(Incoming("!proponi", "stay-start"));
        var result = await service.IncomingAsync(Incoming(day, "stay-day") with { IsPrivate = true });
        Assert.True(result.Handled); Assert.True(result.ReplyPrivately);
        var session = await db.Set<MealProposalSession>().SingleAsync();
        Assert.Equal(allowed ? MealProposalStep.Meal : MealProposalStep.Day, session.Step);
        Assert.Equal(Now.AddMinutes(5), session.ExpiresAtUtc);
        if (!allowed)
        {
            Assert.Contains("fuori dal tuo soggiorno", result.ReplyText); Assert.Null(session.Day);
            Assert.Empty(await db.Set<MealProposal>().ToListAsync()); return;
        }
        await service.IncomingAsync(Incoming("cena", "stay-meal") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("Pasta", "stay-dish") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("1", "stay-offer") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("!conferma", "stay-confirm") with { IsPrivate = true });
        Assert.Equal(MealProposalStep.Closed, (await db.Set<MealProposalSession>().SingleAsync()).Step);
        Assert.Single(await db.Set<MealProposal>().ToListAsync());
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("undecided")]
    [InlineData("no-response")]
    [InlineData("missing-stay")]
    [InlineData("missing-dates")]
    [InlineData("changed-stay")]
    public async Task MealProposalRechecksPersonalPresenceAndStayBeforeSaving(string change)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        await ConfirmMealProposalStay(p, f.EventId);
        var db = p.GetRequiredService<TRobotDbContext>();
        await service.IncomingAsync(Incoming("!proponi", "change-start"));
        await service.IncomingAsync(Incoming("31/12", "change-day") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("cena", "change-meal") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("Pasta", "change-dish") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("1", "change-offer") with { IsPrivate = true });
        var participant = await db.EventParticipants.SingleAsync(x => x.EventId == f.EventId);
        var stay = await db.Set<AttendanceStay>().SingleAsync();
        switch (change)
        {
            case "absent": participant.ChangeStatus(ParticipationStatus.Absent, Now); break;
            case "undecided": participant.ChangeStatus(ParticipationStatus.Undecided, Now); break;
            case "no-response": participant.ChangeStatus(ParticipationStatus.NoResponse, Now); break;
            case "missing-stay": db.Remove(stay); break;
            case "missing-dates":
                db.Entry(stay).Property(x => x.Arrival).CurrentValue = default; break;
            case "changed-stay": stay.Set(new(2030, 12, 30), 1); break;
        }
        await db.SaveChangesAsync();
        var result = await service.IncomingAsync(Incoming("!conferma", "change-confirm") with { IsPrivate = true });
        Assert.True(result.Handled); Assert.True(result.ReplyPrivately); Assert.Null(result.Reaction);
        Assert.Contains("Nessun piatto registrato", result.ReplyText); Assert.Contains("!proponi nel gruppo", result.ReplyText);
        Assert.Empty(await db.Set<MealProposal>().ToListAsync());
        Assert.Equal(MealProposalStep.Closed, (await db.Set<MealProposalSession>().SingleAsync()).Step);
        await service.IncomingAsync(Incoming("!proponi", "change-restart"));
        var rejected = await service.IncomingAsync(Incoming("31/12", "change-retry-day") with { IsPrivate = true });
        Assert.True(rejected.ReplyPrivately);
        Assert.Equal(MealProposalStep.Day, (await db.Set<MealProposalSession>().SingleAsync()).Step);
        Assert.Null((await db.Set<MealProposalSession>().SingleAsync()).Day);
        Assert.Empty(await db.Set<MealProposal>().ToListAsync());
    }

    [Fact]
    public async Task MealDialogueSamePersonCanStartInAnotherEventWithoutOverwritingPreviousSession()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service);
        await SetStayEvent(p, f); await ConfirmMealProposalStay(p, f.EventId);
        Assert.True((await service.IncomingAsync(Incoming("!proponi", "first-event-start"))).Handled);
        await service.IncomingAsync(Incoming("30/12", "first-event-day") with { IsPrivate = true });
        var db = p.GetRequiredService<TRobotDbContext>();
        (await db.Set<WhatsAppConnection>().SingleAsync()).Disable();
        var next = new Event("Secondo evento proposte", new DateTime(2030, 12, 31, 12, 0, 0, DateTimeKind.Utc));
        var binding = new WhatsAppConnection(next.Id);
        binding.Activate(Group, f.OrganizerPersonId, Now);
        db.AddRange(next, new EventParticipant(next.Id, person), new EventOrganizer(next.Id, f.OrganizerPersonId), binding);
        foreach (var sender in Incoming().SenderIds)
            db.Add(new WhatsAppIdentityAlias(next.Id, Options().Fingerprint("sender", sender), person));
        await db.SaveChangesAsync();
        var second = await service.IncomingAsync(Incoming("!proponi", "second-event-start"));
        Assert.True(second.Handled); Assert.True(second.ReplyPrivately);
        var sessions = await db.Set<MealProposalSession>().AsNoTracking().ToArrayAsync();
        Assert.Equal(2, sessions.Length);
        Assert.All(sessions, x => Assert.Equal(person, x.Id));
        Assert.Equal(MealProposalStep.Meal, sessions.Single(x => x.EventId == f.EventId).Step);
        Assert.Equal(MealProposalStep.Day, sessions.Single(x => x.EventId == next.Id).Step);
    }

    [Fact]
    public async Task MealPrivateRouteRejectsOtherCommandsAndInvalidDaysAndUsesNextYear()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service); await SetStayEvent(p, f); await ConfirmMealProposalStay(p, f.EventId);
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.False((await service.IncomingAsync(Incoming("!presenza si", "private-presence") with { IsPrivate = true })).Handled);
        Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync()).Status);
        Assert.False((await service.IncomingAsync(Incoming("!proponi", "unknown-meal") with { SenderIds = ["unknown@lid"] })).Handled);
        await service.IncomingAsync(Incoming("!proponi", "n-start"));
        var invalid = await service.IncomingAsync(Incoming("31/02", "n-invalid") with { IsPrivate = true });
        Assert.Contains("29/11/2030", invalid.ReplyText); Assert.Contains("30/01/2031", invalid.ReplyText);
        await service.IncomingAsync(Incoming("!presenza no", "n-private-command") with { IsPrivate = true });
        Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync()).Status);
        await service.IncomingAsync(Incoming("01/01", "n-day") with { IsPrivate = true });
        Assert.Equal(new DateOnly(2031, 1, 1), (await db.Set<MealProposalSession>().SingleAsync()).Day);
        await service.IncomingAsync(Incoming("colazione", "n-meal") with { IsPrivate = true });
        var name = new string('a', 180);
        await service.IncomingAsync(Incoming(name, "n-dish") with { IsPrivate = true });
        var recap = await service.IncomingAsync(Incoming("1", "n-offer") with { IsPrivate = true });
        Assert.Contains(name, recap.ReplyText);
        await service.IncomingAsync(Incoming("!annulla", "n-cancel") with { IsPrivate = true });
        Assert.Empty(await db.Set<MealProposal>().ToListAsync());
    }

    [Fact]
    public async Task MealDialogueStartsOnlyInGroupPersistsAndConfirmsExactlyOnceWithoutChangingMenu()
    {
        await using var f = new LocalWebFactory(); var clock = new Clock(); var gateway = new FakeGateway(); Guid person;
        using (var first = f.Services.CreateScope())
        {
            var p = first.ServiceProvider; var service = Service(p, gateway, clock);
            person = await Activate(p, f, service); await SetStayEvent(p, f); await ConfirmMealProposalStay(p, f.EventId);
            Assert.False((await service.IncomingAsync(Incoming("!proponi", "unsolicited") with { IsPrivate = true })).Handled);
            var start = await service.IncomingAsync(Incoming("!proponi", "meal-start"));
            Assert.True(start.ReplyPrivately); Assert.Equal("\u2705", start.Reaction);
            Assert.True((await service.IncomingAsync(Incoming("!proponi", "meal-start"))).Duplicate);
            await service.IncomingAsync(Incoming("31/12", "meal-day") with { IsPrivate = true });
            await service.IncomingAsync(Incoming("cena", "meal-kind") with { IsPrivate = true });
            await service.IncomingAsync(Incoming("Lasagne", "meal-dish") with { IsPrivate = true });
            await service.IncomingAsync(Incoming("2", "meal-offer") with { IsPrivate = true });
            Assert.Empty(await p.GetRequiredService<TRobotDbContext>().Set<MealProposal>().ToListAsync());
        }
        using var scope = f.Services.CreateScope(); var provider = scope.ServiceProvider; var resumed = Service(provider, gateway, clock);
        var result = await resumed.IncomingAsync(Incoming("!conferma", "meal-confirm") with { IsPrivate = true });
        Assert.True(result.ReplyPrivately); Assert.Equal("\u2705", result.Reaction);
        Assert.True((await resumed.IncomingAsync(Incoming("!conferma", "meal-confirm") with { IsPrivate = true })).Duplicate);
        var db = provider.GetRequiredService<TRobotDbContext>(); var proposal = await db.Set<MealProposal>().SingleAsync();
        Assert.Equal(person, proposal.PersonId); Assert.Equal(new DateOnly(2030, 12, 31), proposal.Day);
        Assert.Equal(MealKind.Dinner, proposal.Meal); Assert.Equal("Lasagne", proposal.DishName); Assert.True(proposal.OffersToCook);
        var session = await db.Set<MealProposalSession>().SingleAsync(); Assert.Equal(MealProposalStep.Closed, session.Step);
        Assert.Null(session.DishName); Assert.Null(session.Day); Assert.Null(session.OffersToCook);
        Assert.Empty(await db.Set<MenuItem>().ToListAsync());
        var store = provider.GetRequiredService<ILocalStore>(); var archive = new MealProposalService(store, new(store, clock));
        Assert.Single((await archive.ReadAsync(f.OrganizerPersonId, f.EventId)).Proposals);
        await Assert.ThrowsAsync<LocalAccessException>(() => archive.ReadAsync(Guid.NewGuid(), f.EventId));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("expire")]
    [InlineData("binding")]
    [InlineData("alias")]
    [InlineData("event")]
    public async Task MealDialogueCancellationExpiryAndRevocationNeverCreateProposal(string reason)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service); await SetStayEvent(p, f); await ConfirmMealProposalStay(p, f.EventId);
        await service.IncomingAsync(Incoming("!proponi", "m-start"));
        await service.IncomingAsync(Incoming("30/12", "m-day") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("pranzo", "m-kind") with { IsPrivate = true });
        await service.IncomingAsync(Incoming("Risotto", "m-dish") with { IsPrivate = true });
        var db = p.GetRequiredService<TRobotDbContext>();
        clock.Utc = Now.AddMinutes(1);
        await service.IncomingAsync(Incoming("!proponi", "m-repeat") with { TimestampUnixSeconds = new DateTimeOffset(clock.Utc).ToUnixTimeSeconds() });
        Assert.Equal(Now.AddMinutes(5), (await db.Set<MealProposalSession>().SingleAsync()).ExpiresAtUtc);
        if (reason == "expire") clock.Utc = Now.AddMinutes(5);
        if (reason == "binding")
        { (await db.Set<WhatsAppConnection>().SingleAsync()).Activate(Group, f.OrganizerPersonId, Now); await db.SaveChangesAsync(); }
        if (reason == "alias")
        { var alias = await db.Set<WhatsAppIdentityAlias>().FirstAsync(); alias.SetActive(false); alias.SetActive(true); await db.SaveChangesAsync(); }
        if (reason == "event")
        { var ev = await db.Events.SingleAsync(); ev.UpdateDetails("Evento aggiornato", ev.StartsAtUtc, ev.TimeZoneId); await db.SaveChangesAsync(); }
        var result = await service.IncomingAsync(Incoming(reason == "cancel" ? "!annulla" : "2", "m-last") with
        { IsPrivate = true, TimestampUnixSeconds = new DateTimeOffset(clock.Utc).ToUnixTimeSeconds() });
        Assert.Equal(reason == "cancel", result.Handled);
        Assert.Empty(await db.Set<MealProposal>().ToListAsync());
        var session = await db.Set<MealProposalSession>().SingleAsync(); Assert.Equal(MealProposalStep.Closed, session.Step); Assert.Null(session.DishName);
    }
}
