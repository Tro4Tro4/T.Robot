using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.History;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed partial class WhatsAppIntegrationTests
{
    private sealed record FamilySetup(Guid Sender, Guid Spouse, Guid Child, Guid SpouseChild, Guid HouseholdId);

    // Sender and spouse are adults; each has one linked child. Everyone starts confirmed with a stay.
    private static async Task<FamilySetup> SetFamily(IServiceProvider p, LocalWebFactory f, Guid person)
    {
        var db = p.GetRequiredService<TRobotDbContext>();
        var spouse = new Person("Coniuge sintetico"); var child = new Person("Bambino sintetico"); var spouseChild = new Person("Bambina sintetica");
        var family = new Household(f.EventId, "Rossi *test*", Guid.NewGuid());
        var senderMember = new HouseholdMember(f.EventId, family.Id, person, HouseholdMemberKind.Adult, null, Guid.NewGuid());
        var spouseMember = new HouseholdMember(f.EventId, family.Id, spouse.Id, HouseholdMemberKind.Adult, null, Guid.NewGuid());
        db.AddRange(spouse, child, spouseChild, family, senderMember, spouseMember,
            new EventParticipant(f.EventId, spouse.Id), new EventParticipant(f.EventId, child.Id), new EventParticipant(f.EventId, spouseChild.Id),
            new WhatsAppIdentityAlias(f.EventId, Options().Fingerprint("sender", "spouse@lid"), spouse.Id),
            new WhatsAppIdentityAlias(f.EventId, Options().Fingerprint("sender", "child@lid"), child.Id),
            new HouseholdMember(f.EventId, family.Id, child.Id, HouseholdMemberKind.Child, senderMember.Id, Guid.NewGuid()),
            new HouseholdMember(f.EventId, family.Id, spouseChild.Id, HouseholdMemberKind.Child, spouseMember.Id, Guid.NewGuid()));
        await db.SaveChangesAsync();
        foreach (var id in new[] { person, spouse.Id, child.Id, spouseChild.Id })
        {
            (await db.EventParticipants.SingleAsync(x => x.PersonId == id)).ChangeStatus(ParticipationStatus.Confirmed, Now.AddDays(-1));
            var stay = new AttendanceStay(f.EventId, id); stay.Set(new(2030, 12, 30), 3); db.Add(stay);
        }
        await db.SaveChangesAsync();
        return new(person, spouse.Id, child.Id, spouseChild.Id, family.Id);
    }

    private static async Task<ParticipationStatus> StatusOf(TRobotDbContext db, Guid person) =>
        (await db.EventParticipants.AsNoTracking().SingleAsync(x => x.PersonId == person)).Status;

    private static async Task AssertStaysPreserved(TRobotDbContext db)
    {
        var stays = await db.Set<AttendanceStay>().AsNoTracking().ToListAsync();
        Assert.Equal(4, stays.Count);
        Assert.All(stays, x => { Assert.Equal(new DateOnly(2030, 12, 30), x.Arrival); Assert.Equal(3, x.Days); Assert.Equal(1, x.Revision); });
        Assert.Empty(await db.Set<AttendanceStayChange>().ToListAsync());
    }

    [Theory]
    [InlineData("!presenza no", ParticipationStatus.Absent, "assente")]
    [InlineData("!presenza forse", ParticipationStatus.Undecided, "forse")]
    public async Task FamilyStatusYesAppliesToWholeHouseholdKeepingStaysWithHistory(string command, ParticipationStatus status, string label)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service);
        var family = await SetFamily(p, f, person); var db = p.GetRequiredService<TRobotDbContext>();

        var question = await service.IncomingAsync(Incoming(command, "family-status"));
        Assert.True(question.Handled); Assert.Null(question.Reaction); Assert.NotNull(question.ReplyOperationId);
        Assert.Contains($"Segnato \"{label}\" per te", question.ReplyText);
        Assert.Contains("Vale per tutta la famiglia Rossi test?", question.ReplyText);
        Assert.Contains("no per te e 1 bambini collegati", question.ReplyText);
        Assert.DoesNotContain("sintetic", question.ReplyText);
        Assert.True(question.ReplyText!.Length < 2000);
        Assert.Equal(status, await StatusOf(db, person));
        foreach (var other in new[] { family.Spouse, family.Child, family.SpouseChild })
            Assert.Equal(ParticipationStatus.Confirmed, await StatusOf(db, other));
        var dialogue = await db.Set<AttendanceDialogue>().AsNoTracking().SingleAsync();
        Assert.Equal(AttendanceDialogueStep.FamilyStatus, dialogue.Step); Assert.Equal(status, dialogue.FamilyStatus);
        Assert.Equal(family.HouseholdId, dialogue.HouseholdId); Assert.Equal(Now.AddMinutes(15), dialogue.ExpiresAtUtc);

        var answer = await service.IncomingAsync(Incoming("sì", "family-yes"));
        Assert.Equal($"👨‍👩‍👧 Fatto! \"{label}\" per tutta la famiglia.", answer.ReplyText);
        foreach (var id in new[] { person, family.Spouse, family.Child, family.SpouseChild })
            Assert.Equal(status, await StatusOf(db, id));
        await AssertStaysPreserved(db);
        Assert.Equal(AttendanceDialogueStep.Closed, (await db.Set<AttendanceDialogue>().AsNoTracking().SingleAsync()).Step);
        Assert.Null((await db.Set<AttendanceDialogue>().AsNoTracking().SingleAsync()).FamilyStatus);

        var receipts = await db.Set<WhatsAppIncomingReceipt>().AsNoTracking().ToListAsync();
        Assert.Equal(4, receipts.Count);
        Assert.All(receipts, x => { Assert.Equal(ParticipationStatus.Confirmed, x.PreviousStatus); Assert.Equal(status, x.Status); });
        Assert.Equal(new[] { person, family.Spouse, family.Child, family.SpouseChild }.Order(), receipts.Select(x => x.PersonId).Order());
        var audits = await db.Set<LocalAudit>().AsNoTracking().Where(x => x.Action == "FamilyPresenceApplied").ToListAsync();
        Assert.Equal(3, audits.Count);
        Assert.All(audits, x => { Assert.Equal(person, x.ActorId); Assert.Equal("WhatsApp", x.Module); });
        Assert.Equal(audits.Select(x => x.EntityId).Order(), receipts.Where(x => x.PersonId != person).Select(x => x.Id).Order());

        var history = await new EventHistoryStore(db).ReadAsync(f.EventId, HistoryCategory.Local, 0, 100, default);
        Assert.Equal(3, history.Count(x => x.LocalModule == "WhatsApp" && x.LocalAction == "FamilyPresenceApplied"));

        Assert.True((await service.IncomingAsync(Incoming("sì", "family-yes"))).Duplicate);
        Assert.True((await service.IncomingAsync(Incoming(command, "family-status"))).Duplicate);
        Assert.Equal(4, await db.Set<WhatsAppIncomingReceipt>().CountAsync());
        Assert.False((await service.IncomingAsync(Incoming("si", "after-close"))).Handled);
    }

    [Fact]
    public async Task FamilyStatusNoChangesSenderAndLinkedChildrenOnly()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service);
        var family = await SetFamily(p, f, person); var db = p.GetRequiredService<TRobotDbContext>();
        await service.IncomingAsync(Incoming("!presenza forse", "family-status"));
        var answer = await service.IncomingAsync(Incoming("no", "family-no"));
        Assert.Equal("👌 Segnato \"forse\" per te e 1 bambini collegati.", answer.ReplyText);
        Assert.Equal(ParticipationStatus.Undecided, await StatusOf(db, person));
        Assert.Equal(ParticipationStatus.Undecided, await StatusOf(db, family.Child));
        Assert.Equal(ParticipationStatus.Confirmed, await StatusOf(db, family.Spouse));
        Assert.Equal(ParticipationStatus.Confirmed, await StatusOf(db, family.SpouseChild));
        await AssertStaysPreserved(db);
        var audit = await db.Set<LocalAudit>().AsNoTracking().SingleAsync(x => x.Action == "FamilyPresenceApplied");
        Assert.Equal(person, audit.ActorId);
        Assert.Equal(family.Child, (await db.Set<WhatsAppIncomingReceipt>().AsNoTracking().SingleAsync(x => x.Id == audit.EntityId)).PersonId);
    }

    [Fact]
    public async Task FamilyStatusCancelKeepsOnlySenderAndInvalidAnswerAsksAgain()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service);
        var family = await SetFamily(p, f, person); var db = p.GetRequiredService<TRobotDbContext>();
        await service.IncomingAsync(Incoming("!presenza no", "family-status"));
        var help = await service.IncomingAsync(Incoming("30/12", "family-date"));
        Assert.Equal("👨‍👩‍👧 Rispondi sì per tutta la famiglia oppure no. !annulla lascia solo te.", help.ReplyText);
        Assert.Equal(AttendanceDialogueStep.FamilyStatus, (await db.Set<AttendanceDialogue>().AsNoTracking().SingleAsync()).Step);
        var cancel = await service.IncomingAsync(Incoming("!annulla", "family-cancel"));
        Assert.Equal("👌 Va bene: \"assente\" resta solo per te.", cancel.ReplyText);
        Assert.Equal(ParticipationStatus.Absent, await StatusOf(db, person));
        foreach (var other in new[] { family.Spouse, family.Child, family.SpouseChild })
            Assert.Equal(ParticipationStatus.Confirmed, await StatusOf(db, other));
        await AssertStaysPreserved(db);
        Assert.False((await service.IncomingAsync(Incoming("si", "family-late-yes"))).Handled);
        Assert.Equal(ParticipationStatus.Confirmed, await StatusOf(db, family.Spouse));
        Assert.False(await db.Set<LocalAudit>().AnyAsync(x => x.Action == "FamilyPresenceApplied"));
    }

    [Theory]
    [InlineData("status")]
    [InlineData("member")]
    [InlineData("stay")]
    public async Task FamilyStatusSnapshotChangeRejectsAnswer(string kind)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service);
        var family = await SetFamily(p, f, person); var db = p.GetRequiredService<TRobotDbContext>();
        await service.IncomingAsync(Incoming("!presenza forse", "family-status"));
        if (kind == "status") (await db.EventParticipants.SingleAsync(x => x.PersonId == family.Spouse)).ChangeStatus(ParticipationStatus.Absent, Now);
        if (kind == "member") (await db.Set<HouseholdMember>().SingleAsync(x => x.PersonId == family.SpouseChild)).Update(HouseholdMemberKind.Child, null, false);
        if (kind == "stay") (await db.Set<AttendanceStay>().SingleAsync(x => x.Id == family.Child)).Set(new(2030, 12, 31), 2);
        await db.SaveChangesAsync();
        var answer = await service.IncomingAsync(Incoming("si", "family-yes"));
        Assert.Equal("🔄 Presenze, soggiorni o nucleo sono cambiati. Ripeti !presenza forse per usare i dati aggiornati.", answer.ReplyText);
        Assert.Equal(ParticipationStatus.Undecided, await StatusOf(db, person));
        Assert.Equal(ParticipationStatus.Confirmed, await StatusOf(db, family.Child));
        Assert.Equal(ParticipationStatus.Confirmed, await StatusOf(db, family.SpouseChild));
        Assert.Equal(kind == "status" ? ParticipationStatus.Absent : ParticipationStatus.Confirmed, await StatusOf(db, family.Spouse));
        Assert.False(await db.Set<LocalAudit>().AnyAsync(x => x.Action == "FamilyPresenceApplied"));
        Assert.Equal(AttendanceDialogueStep.Closed, (await db.Set<AttendanceDialogue>().AsNoTracking().SingleAsync()).Step);
    }

    [Fact]
    public async Task FamilyStatusExpiresAfterFifteenMinutesWithoutChanges()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); var person = await Activate(p, f, service);
        var family = await SetFamily(p, f, person); var db = p.GetRequiredService<TRobotDbContext>();
        await service.IncomingAsync(Incoming("!presenza no", "family-status"));
        clock.Utc = Now.AddMinutes(15);
        var answer = await service.IncomingAsync(Incoming("si", "family-late") with { TimestampUnixSeconds = new DateTimeOffset(clock.Utc).ToUnixTimeSeconds() });
        Assert.Equal("⌛ Domanda scaduta o collegamento cambiato: \"assente\" resta solo per te. Per la famiglia ripeti !presenza no.", answer.ReplyText);
        Assert.Equal(ParticipationStatus.Absent, await StatusOf(db, person));
        foreach (var other in new[] { family.Spouse, family.Child, family.SpouseChild })
            Assert.Equal(ParticipationStatus.Confirmed, await StatusOf(db, other));
        Assert.Equal(AttendanceDialogueStep.Closed, (await db.Set<AttendanceDialogue>().AsNoTracking().SingleAsync()).Step);
    }

    [Theory]
    [InlineData("!presenza no", ParticipationStatus.Absent)]
    [InlineData("!presenza forse", ParticipationStatus.Undecided)]
    public async Task FamilyStatusWithoutHouseholdOrFromChildKeepsReaction(string command, ParticipationStatus status)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service);
        var db = p.GetRequiredService<TRobotDbContext>();
        var alone = await service.IncomingAsync(Incoming(command, "alone"));
        Assert.Equal("✅", alone.Reaction); Assert.Null(alone.ReplyText);
        Assert.Equal(status, await StatusOf(db, person));
        Assert.False(await db.Set<AttendanceDialogue>().AnyAsync());

        var family = await SetFamily(p, f, person);
        var child = await service.IncomingAsync(Incoming(command, "from-child") with { SenderIds = ["child@lid"] });
        Assert.Equal("✅", child.Reaction); Assert.Null(child.ReplyText);
        Assert.Equal(status, await StatusOf(db, family.Child));
        Assert.Equal(ParticipationStatus.Confirmed, await StatusOf(db, person));
        Assert.Equal(ParticipationStatus.Confirmed, await StatusOf(db, family.Spouse));
        Assert.False(await db.Set<AttendanceDialogue>().AnyAsync());
        Assert.False((await service.IncomingAsync(Incoming("si", "child-yes") with { SenderIds = ["child@lid"] })).Handled);
    }

    [Fact]
    public async Task FamilyStatusReplacesOpenStayDialogueAndYesRestartsDates()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service); await SetStayEvent(p, f);
        var family = await SetFamily(p, f, person); var db = p.GetRequiredService<TRobotDbContext>();
        await service.IncomingAsync(Incoming("!presenza si", "start")); await service.IncomingAsync(Incoming("30/12", "arrival"));
        var question = await service.IncomingAsync(Incoming("!presenza no", "family-status"));
        Assert.Contains("Vale per tutta la famiglia", question.ReplyText);
        var dialogue = await db.Set<AttendanceDialogue>().AsNoTracking().SingleAsync();
        Assert.Equal(AttendanceDialogueStep.FamilyStatus, dialogue.Step); Assert.Null(dialogue.Arrival);
        // A numeric answer from the old date dialogue no longer counts as days.
        Assert.Contains("Rispondi sì", (await service.IncomingAsync(Incoming("4", "old-days"))).ReplyText);
        await service.IncomingAsync(Incoming("!presenza si", "restart"));
        var restarted = await db.Set<AttendanceDialogue>().AsNoTracking().SingleAsync();
        Assert.Equal(AttendanceDialogueStep.Arrival, restarted.Step); Assert.Null(restarted.FamilyStatus);
        Assert.Equal(ParticipationStatus.Confirmed, await StatusOf(db, family.Spouse));
    }

    [Theory]
    [InlineData(ParticipationStatus.Confirmed)]
    [InlineData(ParticipationStatus.NoResponse)]
    public void StartFamilyStatusRejectsOtherStatuses(ParticipationStatus status)
    {
        var dialogue = new AttendanceDialogue(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<ArgumentOutOfRangeException>(() => dialogue.StartFamilyStatus(Guid.NewGuid(), Group, 1, Now, status, Guid.NewGuid(), new string('a', 64)));
        Assert.Equal(AttendanceDialogueStep.Closed, dialogue.Step); Assert.Null(dialogue.FamilyStatus);
    }
}
