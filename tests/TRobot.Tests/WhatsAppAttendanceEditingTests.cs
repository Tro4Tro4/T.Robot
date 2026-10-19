using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Households;
using TRobot.Application.Local;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed partial class WhatsAppIntegrationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task LoneAdultEditRejectsChildAddedOrReactivatedAfterStartOrSummary(bool reactivate, bool afterSummary)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var sender = await Activate(p, f, service); await SetStayEvent(p, f);
        await AskStay(service); await service.IncomingAsync(Incoming("si", "initial-confirm"));
        var db = p.GetRequiredService<TRobotDbContext>(); var households = p.GetRequiredService<HouseholdService>();
        var family = await households.CreateAsync(f.OrganizerPersonId, f.EventId, "Nucleo inizialmente individuale", Guid.NewGuid());
        var parent = await households.AddAsync(f.OrganizerPersonId, f.EventId, family,
            new(sender, HouseholdMemberKind.Adult, null, 0, 0, false, Guid.NewGuid()));
        var child = new Person("MINORE NON PUBBLICABILE"); db.AddRange(child, new EventParticipant(f.EventId, child.Id)); await db.SaveChangesAsync();
        Guid? childMember = null;
        if (reactivate)
        {
            childMember = await households.AddAsync(f.OrganizerPersonId, f.EventId, family,
                new(child.Id, HouseholdMemberKind.Child, parent, 1, 0, false, Guid.NewGuid()));
            await households.UpdateAsync(f.OrganizerPersonId, f.EventId, family, childMember.Value,
                new(child.Id, HouseholdMemberKind.Child, parent, 2, 0, true, Guid.NewGuid()), false);
        }
        await service.IncomingAsync(Incoming("!presenza modifica", "edit-start"));
        if (afterSummary)
        {
            await service.IncomingAsync(Incoming("2", "edit-choice"));
            var summary = await service.IncomingAsync(Incoming("2", "edit-days"));
            Assert.Contains("Confermi il tuo soggiorno", summary.ReplyText);
        }
        var familyRevision = (await db.Set<Household>().SingleAsync(x => x.Id == family)).Revision;
        if (reactivate)
            await households.UpdateAsync(f.OrganizerPersonId, f.EventId, family, childMember!.Value,
                new(child.Id, HouseholdMemberKind.Child, parent, familyRevision, 1, true, Guid.NewGuid()), true);
        else
            await households.AddAsync(f.OrganizerPersonId, f.EventId, family,
                new(child.Id, HouseholdMemberKind.Child, parent, familyRevision, 0, false, Guid.NewGuid()));
        var result = await service.IncomingAsync(Incoming(afterSummary ? "si" : "2", "stale-reply"));
        Assert.Contains("cambiati", result.ReplyText);
        Assert.Equal(ParticipationStatus.NoResponse, (await db.EventParticipants.SingleAsync(x => x.PersonId == child.Id)).Status);
        var stay = Assert.Single(await db.Set<AttendanceStay>().ToListAsync());
        Assert.Equal(sender, stay.Id); Assert.Equal(new DateOnly(2030, 12, 30), stay.Arrival); Assert.Equal(4, stay.Days);
        Assert.Single(await db.Set<AttendanceStayChange>().ToListAsync());
        Assert.Equal(AttendanceDialogueStep.Closed, (await db.Set<AttendanceDialogue>().SingleAsync()).Step);
    }

    [Theory]
    [InlineData("1", "31/12", 31, 4)]
    [InlineData("2", "2", 30, 2)]
    [InlineData("3", "31/12", 31, 2)]
    public async Task EditPreservesUnselectedFieldAndOnlyWritesAtFinalConfirmation(string choice, string answer, int arrivalDay, int days)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        await AskStay(service); await service.IncomingAsync(Incoming("si", "initial-confirm"));
        var db = p.GetRequiredService<TRobotDbContext>();
        var start = await service.IncomingAsync(Incoming("!presenza modifica", "edit-start"));
        Assert.Contains("1 = arrivo", start.ReplyText); Assert.Contains("3 notti", start.ReplyText);
        Assert.True((await service.IncomingAsync(Incoming("!presenza modifica", "edit-start"))).Duplicate);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(Incoming("!presenza si", "edit-start")));
        await service.IncomingAsync(Incoming(choice, "edit-choice"));
        var result = await service.IncomingAsync(Incoming(answer, "edit-answer"));
        if (choice == "3") result = await service.IncomingAsync(Incoming("2", "edit-days"));
        Assert.Contains("Confermi", result.ReplyText);
        var old = await db.Set<AttendanceStay>().SingleAsync(); Assert.Equal(30, old.Arrival.Day); Assert.Equal(4, old.Days);
        Assert.Single(await db.Set<AttendanceStayChange>().ToListAsync());
        await service.IncomingAsync(Incoming("si", "edit-confirm"));
        old = await db.Set<AttendanceStay>().SingleAsync();
        Assert.Equal(arrivalDay, old.Arrival.Day); Assert.Equal(days, old.Days);
        Assert.Equal(2, await db.Set<AttendanceStayChange>().CountAsync());
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("expiry")]
    [InlineData("repin")]
    [InlineData("concurrent-before-choice")]
    [InlineData("concurrent-after-summary")]
    public async Task InterruptedOrStaleEditNeverOverwritesRegisteredStay(string kind)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service); await SetStayEvent(p, f);
        await AskStay(service); await service.IncomingAsync(Incoming("si", "initial-confirm"));
        var db = p.GetRequiredService<TRobotDbContext>(); var stay = await db.Set<AttendanceStay>().SingleAsync();
        await service.IncomingAsync(Incoming("!presenza modifica", "edit-start"));
        if (kind == "concurrent-before-choice") { stay = await db.Set<AttendanceStay>().SingleAsync(); stay.Set(new(2030, 12, 29), 6); await db.SaveChangesAsync(); }
        var choice = await service.IncomingAsync(Incoming("1", "edit-choice"));
        if (kind == "concurrent-before-choice") Assert.Contains("cambiati", choice.ReplyText);
        else
        {
            await service.IncomingAsync(Incoming("31/12", "edit-date"));
            if (kind == "cancel") await service.IncomingAsync(Incoming("!annulla", "edit-cancel"));
            if (kind == "expiry") clock.Utc = Now.AddMinutes(15);
            if (kind == "repin") await service.SelectGroupAsync(f.OrganizerPersonId, f.EventId, Group, 1, true);
            if (kind == "concurrent-after-summary") { stay = await db.Set<AttendanceStay>().SingleAsync(); stay.Set(new(2030, 12, 29), 6); await db.SaveChangesAsync(); }
            await service.IncomingAsync(Incoming("si", "edit-confirm") with { TimestampUnixSeconds = new DateTimeOffset(clock.Utc).ToUnixTimeSeconds() });
        }
        stay = await db.Set<AttendanceStay>().SingleAsync();
        Assert.Equal(kind.StartsWith("concurrent", StringComparison.Ordinal) ? 29 : 30, stay.Arrival.Day);
        Assert.Equal(kind.StartsWith("concurrent", StringComparison.Ordinal) ? 6 : 4, stay.Days);
        Assert.Single(await db.Set<AttendanceStayChange>().ToListAsync());
    }

    [Fact]
    public async Task EditRequiresConfirmedAndCollectsMissingStayWithoutChangingParticipation()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        var db = p.GetRequiredService<TRobotDbContext>(); var person = await db.EventParticipants.SingleAsync();
        Assert.Contains("!presenza si", (await service.IncomingAsync(Incoming("!presenza modifica", "not-confirmed"))).ReplyText);
        person = await db.EventParticipants.SingleAsync();
        Assert.Equal(ParticipationStatus.NoResponse, person.Status); Assert.Empty(await db.Set<AttendanceDialogue>().ToListAsync());
        person.ChangeStatus(ParticipationStatus.Confirmed, Now); await db.SaveChangesAsync(); var at = person.UpdatedAtUtc;
        Assert.Contains("Quando arrivi", (await service.IncomingAsync(Incoming("!presenza modifica", "missing-start"))).ReplyText);
        await service.IncomingAsync(Incoming("30/12", "missing-date")); await service.IncomingAsync(Incoming("4", "missing-days"));
        person = await db.EventParticipants.SingleAsync();
        Assert.Equal(at, person.UpdatedAtUtc); Assert.Empty(await db.Set<AttendanceStay>().ToListAsync());
        await service.IncomingAsync(Incoming("si", "missing-confirm")); Assert.Single(await db.Set<AttendanceStay>().ToListAsync());
    }

    [Theory]
    [InlineData("initial")]
    [InlineData("edit")]
    [InlineData("link-change")]
    [InlineData("child-stay-change")]
    public async Task IndividualParentConfirmationIncludesOnlyExplicitlyLinkedChildrenAndRejectsStaleChanges(string kind)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var sender = await Activate(p, f, service); await SetStayEvent(p, f);
        var db = p.GetRequiredService<TRobotDbContext>(); var family = new Household(f.EventId, "Famiglia", Guid.NewGuid()); db.Add(family);
        var parent = new HouseholdMember(f.EventId, family.Id, sender, HouseholdMemberKind.Adult, null, Guid.NewGuid()); db.Add(parent);
        var otherParent = SummaryPerson(db, f.EventId, ParticipationStatus.NoResponse, family);
        var otherMember = db.ChangeTracker.Entries<HouseholdMember>().Single(x => x.Entity.PersonId == otherParent).Entity;
        var child = SummaryPerson(db, f.EventId, ParticipationStatus.NoResponse);
        var childMember = new HouseholdMember(f.EventId, family.Id, child, HouseholdMemberKind.Child, parent.Id, Guid.NewGuid()); db.Add(childMember);
        var otherChild = SummaryPerson(db, f.EventId, ParticipationStatus.NoResponse, family, HouseholdMemberKind.Child);
        var inactive = SummaryPerson(db, f.EventId, ParticipationStatus.NoResponse, family, HouseholdMemberKind.Child, false);
        db.ChangeTracker.Entries<HouseholdMember>().Single(x => x.Entity.PersonId == otherChild).Entity.Update(HouseholdMemberKind.Child, otherMember.Id, true);
        db.ChangeTracker.Entries<HouseholdMember>().Single(x => x.Entity.PersonId == inactive).Entity.Update(HouseholdMemberKind.Child, parent.Id, false);
        await db.SaveChangesAsync();
        if (kind == "edit")
        {
            await AskStay(service, "old"); await service.IncomingAsync(Incoming("no", "old-confirm"));
            await service.IncomingAsync(Incoming("!presenza modifica", "edit-start"));
            await service.IncomingAsync(Incoming("2", "edit-choice"));
        }
        else
        {
            await service.IncomingAsync(Incoming("!presenza si", "start"));
            await service.IncomingAsync(Incoming("30/12", "arrival"));
        }
        var summary = await service.IncomingAsync(Incoming("2", "days"));
        Assert.Contains("no per te e 1 bambini collegati", summary.ReplyText);
        Assert.DoesNotContain("NOME INDIVIDUALE", summary.ReplyText);
        if (kind == "link-change") (await db.Set<HouseholdMember>().SingleAsync(x => x.Id == childMember.Id)).Update(HouseholdMemberKind.Child, otherMember.Id, true);
        if (kind == "child-stay-change") { var stay = new AttendanceStay(f.EventId, child); stay.Set(new(2030, 12, 29), 6); db.Add(stay); }
        await db.SaveChangesAsync(); var result = await service.IncomingAsync(Incoming("no", "confirm"));
        if (kind is "link-change" or "child-stay-change")
        {
            Assert.Contains("cambiati", result.ReplyText); Assert.Empty(await db.Set<AttendanceStayChange>().ToListAsync()); return;
        }
        var stays = await db.Set<AttendanceStay>().ToListAsync(); Assert.Equal(2, stays.Count);
        Assert.All(stays, stay => { Assert.Contains(stay.Id, new[] { sender, child }); Assert.Equal(2, stay.Days); Assert.Equal(new(2030, 12, 30), stay.Arrival); });
        Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync(x => x.PersonId == child)).Status);
        foreach (var id in new[] { otherParent, otherChild, inactive })
            Assert.Equal(ParticipationStatus.NoResponse, (await db.EventParticipants.SingleAsync(x => x.PersonId == id)).Status);
        Assert.Equal(kind == "edit" ? 4 : 2, await db.Set<AttendanceStayChange>().CountAsync());
        await service.IncomingAsync(Incoming("!presenza no", "parent-absent"));
        Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync(x => x.PersonId == child)).Status);
        Assert.Equal(2, await db.Set<AttendanceStay>().CountAsync());
    }
}
