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
    private static Task<Guid> AddHikeQuestion(WhatsAppService service, LocalWebFactory f) =>
        service.AddAttendanceQuestionAsync(f.OrganizerPersonId, f.EventId, "Come sali alla baita?", "A piedi\nCon il gatto delle nevi", true);

    [Fact]
    public async Task FixedDateEventConfirmsPresenceWithoutAskingDatesOrNights()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var sender = await Activate(p, f, service); await SetStayEvent(p, f);
        await service.SetAttendanceStayModeAsync(f.OrganizerPersonId, f.EventId, false, 2, 0, true);
        var reply = await service.IncomingAsync(Incoming("!presenza si", "fixed-start"));
        Assert.Contains("ci sei", reply.ReplyText); Assert.DoesNotContain("Quando arrivi", reply.ReplyText); Assert.DoesNotContain("giorni resti", reply.ReplyText);
        var db = p.GetRequiredService<TRobotDbContext>();
        var stay = await db.Set<AttendanceStay>().SingleAsync();
        Assert.Equal(sender, stay.Id); Assert.Equal(new DateOnly(2030, 12, 30), stay.Arrival); Assert.Equal(2, stay.Days);
        Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync()).Status);
        Assert.Equal(AttendanceDialogueStep.Closed, (await db.Set<AttendanceDialogue>().SingleAsync()).Step);
        Assert.Single(await db.Set<AttendanceStayChange>().ToListAsync());
    }

    [Fact]
    public async Task FixedDateEventAsksOnlyTheConfiguredQuestionsAndStoresTheAnswer()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var sender = await Activate(p, f, service); await SetStayEvent(p, f);
        await service.SetAttendanceStayModeAsync(f.OrganizerPersonId, f.EventId, false, 1, 0, true);
        var question = await AddHikeQuestion(service, f);
        var asked = await service.IncomingAsync(Incoming("!presenza si", "q-start"));
        Assert.Contains("Come sali alla baita?", asked.ReplyText); Assert.Contains("1 = A piedi", asked.ReplyText);
        Assert.Contains("2 = Con il gatto delle nevi", asked.ReplyText); Assert.DoesNotContain("Quando arrivi", asked.ReplyText);
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Equal(AttendanceDialogueStep.Question, (await db.Set<AttendanceDialogue>().SingleAsync()).Step);
        Assert.Contains("da 1 a 2", (await service.IncomingAsync(Incoming("7", "q-bad"))).ReplyText);
        Assert.Empty(await db.Set<AttendanceAnswer>().ToListAsync());
        var saved = await service.IncomingAsync(Incoming("2", "q-answer"));
        Assert.Contains("Risposta salvata", saved.ReplyText);
        var answer = await db.Set<AttendanceAnswer>().SingleAsync();
        Assert.Equal(sender, answer.PersonId); Assert.Equal(question, answer.QuestionId);
        var option = await db.Set<AttendanceQuestionOption>().SingleAsync(x => x.Id == answer.OptionId);
        Assert.Equal("Con il gatto delle nevi", option.Text);
        Assert.Equal(AttendanceDialogueStep.Closed, (await db.Set<AttendanceDialogue>().SingleAsync()).Step);
    }

    [Fact]
    public async Task FreeDateEventAsksQuestionsAfterTheStayIsConfirmed()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        await AddHikeQuestion(service, f);
        await service.IncomingAsync(Incoming("!presenza si", "free-start")); await service.IncomingAsync(Incoming("30/12", "free-date"));
        await service.IncomingAsync(Incoming("4", "free-days"));
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Empty(await db.Set<AttendanceAnswer>().ToListAsync());
        var asked = await service.IncomingAsync(Incoming("si", "free-confirm"));
        Assert.Contains("Soggiorno salvato", asked.ReplyText); Assert.Contains("Come sali alla baita?", asked.ReplyText);
        Assert.Equal(4, (await db.Set<AttendanceStay>().SingleAsync()).Days);
        await service.IncomingAsync(Incoming("1", "free-answer"));
        Assert.Single(await db.Set<AttendanceAnswer>().ToListAsync());
        Assert.Equal(AttendanceDialogueStep.Closed, (await db.Set<AttendanceDialogue>().SingleAsync()).Step);
    }

    [Fact]
    public async Task ExpiredQuestionDialoguePointsToEditAndKeepsThePresence()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service); await SetStayEvent(p, f);
        await service.SetAttendanceStayModeAsync(f.OrganizerPersonId, f.EventId, false, 1, 0, true);
        await AddHikeQuestion(service, f);
        await service.IncomingAsync(Incoming("!presenza si", "x-start"));
        clock.Utc = Now.AddMinutes(15);
        var late = await service.IncomingAsync(Incoming("1", "x-late") with { TimestampUnixSeconds = new DateTimeOffset(clock.Utc).ToUnixTimeSeconds() });
        Assert.Contains("!presenza modifica", late.ReplyText);
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Empty(await db.Set<AttendanceAnswer>().ToListAsync());
        Assert.Single(await db.Set<AttendanceStay>().ToListAsync());
        Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync()).Status);
    }

    [Fact]
    public async Task EditInFixedModeRevisitsOnlyQuestionsAndKeepsASingleAnswerRow()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        await service.SetAttendanceStayModeAsync(f.OrganizerPersonId, f.EventId, false, 1, 0, true);
        await service.IncomingAsync(Incoming("!presenza si", "e-start"));
        Assert.Contains("non ci sono altri dettagli", (await service.IncomingAsync(Incoming("!presenza modifica", "e-none"))).ReplyText);
        await AddHikeQuestion(service, f);
        Assert.Contains("Come sali alla baita?", (await service.IncomingAsync(Incoming("!presenza modifica", "e-edit"))).ReplyText);
        await service.IncomingAsync(Incoming("1", "e-first"));
        await service.IncomingAsync(Incoming("!presenza modifica", "e-edit2")); await service.IncomingAsync(Incoming("2", "e-second"));
        var db = p.GetRequiredService<TRobotDbContext>();
        var answer = await db.Set<AttendanceAnswer>().SingleAsync();
        Assert.Equal("Con il gatto delle nevi", (await db.Set<AttendanceQuestionOption>().SingleAsync(x => x.Id == answer.OptionId)).Text);
        Assert.Equal(1, answer.Revision);
    }

    [Fact]
    public async Task DeactivatedQuestionIsNotAskedAndInvalidDefinitionsAreRejected()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        await service.SetAttendanceStayModeAsync(f.OrganizerPersonId, f.EventId, false, 1, 0, true);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AddAttendanceQuestionAsync(f.OrganizerPersonId, f.EventId, "Una sola?", "solo una", true));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AddAttendanceQuestionAsync(f.OrganizerPersonId, f.EventId, "Doppie?", "Si\nsi", true));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AddAttendanceQuestionAsync(f.OrganizerPersonId, f.EventId, "Senza conferma?", "a\nb", false));
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Empty(await db.Set<AttendanceQuestion>().ToListAsync());
        var question = await AddHikeQuestion(service, f);
        await service.SetAttendanceQuestionActiveAsync(f.OrganizerPersonId, f.EventId, question, false, 0, true);
        var reply = await service.IncomingAsync(Incoming("!presenza si", "off-start"));
        Assert.DoesNotContain("Come sali alla baita?", reply.ReplyText);
        Assert.Equal(AttendanceDialogueStep.Closed, (await db.Set<AttendanceDialogue>().SingleAsync()).Step);
    }

    [Fact]
    public async Task QuestionsAreChangedOnlyByAnAuthorizedOrganizerAndAppearInTheOverview()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        await Assert.ThrowsAnyAsync<Exception>(() => service.AddAttendanceQuestionAsync(Guid.NewGuid(), f.EventId, "Intrusa?", "a\nb", true));
        await service.SetAttendanceStayModeAsync(f.OrganizerPersonId, f.EventId, false, 1, 0, true);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.SetAttendanceStayModeAsync(f.OrganizerPersonId, f.EventId, true, 1, 0, true));
        await AddHikeQuestion(service, f);
        await service.IncomingAsync(Incoming("!presenza si", "ov-start")); await service.IncomingAsync(Incoming("1", "ov-answer"));
        var overview = await service.AttendanceOverviewAsync(f.OrganizerPersonId, f.EventId);
        Assert.False(overview.Collection.AskStayDates);
        var question = Assert.Single(overview.Questions!);
        Assert.Single(question.Options[0].People); Assert.Empty(question.Options[1].People);
    }
}
