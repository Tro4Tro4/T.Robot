using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Local;
using TRobot.Application.WhatsApp;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed partial class WhatsAppIntegrationTests
{
    private static Guid SummaryPerson(TRobotDbContext db, Guid eventId, ParticipationStatus status,
        Household? household = null, HouseholdMemberKind kind = HouseholdMemberKind.Adult, bool active = true)
    {
        var person = new Person("NOME INDIVIDUALE PRIVATO");
        var participation = new EventParticipant(eventId, person.Id); participation.ChangeStatus(status, Now);
        db.AddRange(person, participation);
        if (household is not null)
        {
            var member = new HouseholdMember(eventId, household.Id, person.Id, kind, null, Guid.NewGuid());
            member.Update(kind, null, active); db.Add(member);
        }
        return person.Id;
    }

    [Fact]
    public async Task SummaryWithoutConfirmationsStillReturnsOnePageAndAggregateStatuses()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        var reply = (await service.IncomingAsync(Incoming("!riepilogo"))).ReplyText!;
        Assert.Contains("Pagina 1/1", reply); Assert.Contains("Confermati: 0", reply);
        Assert.Contains("Adulti: 0 · Bambini: 0", reply); Assert.Contains("Senza risposta: 1", reply);
        Assert.Contains("Nessun nucleo", reply); Assert.DoesNotContain("Pagina successiva", reply);
        Assert.Contains("Pagina inesistente", (await service.IncomingAsync(Incoming("!riepilogo 2", "empty-page-two"))).ReplyText);
    }

    [Fact]
    public async Task SummaryCountsConfirmedChildrenWithoutAliasesAndDoesNotConfirmFamiliesImplicitly()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var sender = await Activate(p, f, service);
        var db = p.GetRequiredService<TRobotDbContext>();
        (await db.EventParticipants.SingleAsync()).ChangeStatus(ParticipationStatus.Confirmed, Now);
        var household = new Household(f.EventId, "Famiglia *Dolci*\n!comando\u202e", Guid.NewGuid());
        var empty = new Household(f.EventId, "NUCLEO NON CONFERMATO", Guid.NewGuid()); db.AddRange(household, empty);
        db.Add(new HouseholdMember(f.EventId, household.Id, sender, HouseholdMemberKind.Adult, null, Guid.NewGuid()));
        var child = SummaryPerson(db, f.EventId, ParticipationStatus.Confirmed, household, HouseholdMemberKind.Child);
        SummaryPerson(db, f.EventId, ParticipationStatus.Undecided, household, HouseholdMemberKind.Child);
        SummaryPerson(db, f.EventId, ParticipationStatus.Absent, household);
        SummaryPerson(db, f.EventId, ParticipationStatus.NoResponse, empty);
        SummaryPerson(db, f.EventId, ParticipationStatus.Confirmed, household, HouseholdMemberKind.Adult, false);
        SummaryPerson(db, f.EventId, ParticipationStatus.Confirmed);
        var stay = new AttendanceStay(f.EventId, child); stay.Set(new DateOnly(2030, 12, 30), 4); db.Add(stay);
        var otherEvent = new Event("EVENTO PRIVATO", Now); db.Add(otherEvent);
        var foreign = new Household(otherEvent.Id, "NUCLEO ALTRO EVENTO", Guid.NewGuid()); db.Add(foreign);
        SummaryPerson(db, otherEvent.Id, ParticipationStatus.Confirmed, foreign);
        await db.SaveChangesAsync();
        // Both active aliases resolve to the same sender and must not double the counts.
        Assert.Equal(2, await db.Set<WhatsAppIdentityAlias>().CountAsync(x => x.PersonId == sender && x.IsActive));
        var reply = await service.IncomingAsync(Incoming("!riepilogo")); var text = reply.ReplyText!;
        Assert.True(reply.Handled); Assert.NotNull(reply.ReplyOperationId); Assert.Null(reply.Reaction);
        Assert.Contains("Confermati: 4", text); Assert.Contains("Adulti: 1 · Bambini: 1", text);
        Assert.Contains("età non specificata: 2", text);
        Assert.Contains("Indecisi: 1 · Assenti: 1 · Senza risposta: 1", text);
        Assert.Contains("soggiorno da completare: 3", text);
        Assert.Contains("2 confermati · 1 adulti · 1 bambini", text);
        Assert.Contains("Famiglia ＊Dolci＊ !comando ", text); Assert.DoesNotContain("\u202e", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NOME INDIVIDUALE", text); Assert.DoesNotContain("NUCLEO NON", text);
        Assert.DoesNotContain("ALTRO EVENTO", text); Assert.DoesNotContain("30/12", text);
        Assert.Equal(4, await db.EventParticipants.CountAsync(x => x.EventId == f.EventId && x.Status == ParticipationStatus.Confirmed));
        Assert.Single(await db.Set<AttendanceStay>().ToListAsync()); Assert.Empty(await db.Set<WhatsAppListApproval>().ToListAsync());
    }

    [Fact]
    public async Task SummaryPaginatesFiveMaximumLengthHouseholdsWithInvariantOrderingAndTotals()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        var db = p.GetRequiredService<TRobotDbContext>();
        for (var i = 10; i >= 0; i--)
        {
            var h = new Household(f.EventId, $"Nucleo {i:D2} " + string.Concat(Enumerable.Repeat("🍰", 69)) + "xx", Guid.NewGuid());
            Assert.Equal(150, h.Name.Length); db.Add(h); SummaryPerson(db, f.EventId, ParticipationStatus.Confirmed, h);
        }
        await db.SaveChangesAsync(); var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            var one = (await service.IncomingAsync(Incoming("!riepilogo", "one"))).ReplyText!;
            var two = (await service.IncomingAsync(Incoming("!riepilogo 2", "two"))).ReplyText!;
            var three = (await service.IncomingAsync(Incoming("!riepilogo 3", "three"))).ReplyText!;
            Assert.All(new[] { one, two, three }, text => { Assert.True(text.Length <= 2000); Assert.Contains("Confermati: 11", text); });
            Assert.Equal(5, one.Split("🏡").Length - 1); Assert.Equal(5, two.Split("🏡").Length - 1);
            Assert.Contains("Nucleo 00", one); Assert.Contains("Nucleo 04", one); Assert.DoesNotContain("Nucleo 05", one);
            Assert.Contains("!riepilogo 2", one); Assert.Contains("Nucleo 05", two); Assert.Contains("Nucleo 09", two);
            Assert.Contains("Nucleo 10", three); Assert.Contains("Pagina 3/3", three);
            Assert.Contains("Pagina inesistente", (await service.IncomingAsync(Incoming("!riepilogo 4", "four"))).ReplyText);
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("100001")]
    [InlineData("abc")]
    [InlineData("1 extra")]
    public async Task SummaryRejectsInvalidPageSyntax(string page)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        Assert.Contains("!riepilogo [pagina]", (await service.IncomingAsync(Incoming("!riepilogo " + page))).ReplyText);
    }

    [Fact]
    public async Task SummaryPreservesPendingDialogueAndUsesTechnicalReceiptReplayAndCollisionRules()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        await service.IncomingAsync(Incoming("!presenza si", "presence"));
        var db = p.GetRequiredService<TRobotDbContext>();
        var dialogue = await db.Set<AttendanceDialogue>().SingleAsync(); var expiry = dialogue.ExpiresAtUtc;
        var message = Incoming("!riepilogo", "summary"); var reply = await service.IncomingAsync(message);
        Assert.Contains("Confermati: 1", reply.ReplyText); Assert.Contains("età non specificata: 1", reply.ReplyText);
        Assert.Contains("Nessun nucleo", reply.ReplyText);
        Assert.Equal(AttendanceDialogueStep.Arrival, dialogue.Step); Assert.Equal(expiry, dialogue.ExpiresAtUtc);
        Assert.Equal(new WhatsAppIncomingResult(true, true), await service.IncomingAsync(message));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(message with { Text = "!riepilogo 2" }));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(message with { Text = "!presenza no" }));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(Incoming("!riepilogo", "presence")));
        var receipt = await db.Set<WhatsAppCommandReceipt>().SingleAsync();
        Assert.Equal(reply.ReplyOperationId, receipt.Id); Assert.Equal(64, receipt.CommandHash.Length);
        Assert.Empty(await db.Set<AttendanceStay>().ToListAsync());
    }

    [Theory]
    [InlineData("group")]
    [InlineData("unknown")]
    [InlineData("stale")]
    [InlineData("disabled")]
    [InlineData("revoked")]
    [InlineData("prefix")]
    public async Task SummaryRetainsExistingAuthorizationAndChannelFilters(string scenario)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        var db = p.GetRequiredService<TRobotDbContext>(); var message = Incoming("!riepilogo");
        if (scenario == "group") message = message with { GroupId = "other@g.us" };
        if (scenario == "unknown") message = message with { SenderIds = ["unknown@lid"] };
        if (scenario == "stale") message = message with { TimestampUnixSeconds = message.TimestampUnixSeconds - 121 };
        if (scenario == "prefix") message = message with { Text = "!riepilogo2" };
        if (scenario == "disabled") await service.DisconnectAsync(f.OrganizerPersonId, f.EventId, true);
        if (scenario == "revoked")
        {
            (await db.EventOrganizers.SingleAsync()).SetActive(false); await db.SaveChangesAsync();
            await Assert.ThrowsAsync<LocalAccessException>(() => service.IncomingAsync(message));
        }
        else Assert.False((await service.IncomingAsync(message)).Handled);
        Assert.Empty(await db.Set<WhatsAppCommandReceipt>().ToListAsync());
        Assert.Equal(ParticipationStatus.NoResponse, (await db.EventParticipants.SingleAsync()).Status);
    }
}
