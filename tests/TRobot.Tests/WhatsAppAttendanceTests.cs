using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TRobot.Application.Local;
using TRobot.Application.WhatsApp;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using TRobot.Web.Controllers;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed partial class WhatsAppIntegrationTests
{
    private static async Task SetStayEvent(IServiceProvider p, LocalWebFactory f)
    {
        var db = p.GetRequiredService<TRobotDbContext>(); var ev = await db.Events.SingleAsync();
        ev.UpdateDetails(ev.Name, new DateTime(2030, 12, 29, 23, 30, 0, DateTimeKind.Utc), "Europe/Rome");
        await db.SaveChangesAsync();
    }
    private static async Task AskStay(WhatsAppService service, string suffix = "", string date = "30/12", string days = "4")
    {
        await service.IncomingAsync(Incoming("!presenza si", "start" + suffix));
        await service.IncomingAsync(Incoming(date, "arrival" + suffix));
        await service.IncomingAsync(Incoming(days, "days" + suffix));
    }

    [Fact]
    public async Task GuidedStayPersistsAcrossScopesWithInclusiveDaysNewYearAndPrivateOverview()
    {
        await using var f = new LocalWebFactory(); var clock = new Clock(); var gateway = new FakeGateway(); Guid person;
        using (var first = f.Services.CreateScope())
        {
            var p = first.ServiceProvider; var service = Service(p, gateway, clock);
            person = await Activate(p, f, service); await SetStayEvent(p, f);
            await AskStay(service);
            Assert.Empty(await p.GetRequiredService<TRobotDbContext>().Set<AttendanceStay>().ToListAsync());
        }
        using var scope = f.Services.CreateScope(); var provider = scope.ServiceProvider; var resumed = Service(provider, gateway, clock);
        var result = await resumed.IncomingAsync(Incoming("sì", "confirm"));
        Assert.Contains("salvato", result.ReplyText); Assert.NotNull(result.ReplyOperationId);
        Assert.True((await resumed.IncomingAsync(Incoming("sì", "confirm"))).Duplicate);
        var db = provider.GetRequiredService<TRobotDbContext>(); var stay = await db.Set<AttendanceStay>().SingleAsync();
        Assert.Equal(new DateOnly(2030, 12, 30), stay.Arrival); Assert.Equal(4, stay.Days);
        Assert.Equal(new DateOnly(2031, 1, 2), stay.LastDay); Assert.Equal(3, stay.Nights);
        Assert.Single(await db.Set<AttendanceStayChange>().ToListAsync());
        var overview = await resumed.AttendanceOverviewAsync(f.OrganizerPersonId, f.EventId);
        Assert.Equal(4, overview.Days.Count); Assert.All(overview.Days, x => Assert.Equal(1, x.People));
        Assert.Equal(person, overview.People.Single().PersonId);
        await Assert.ThrowsAsync<LocalAccessException>(() => resumed.AttendanceOverviewAsync(Guid.NewGuid(), f.EventId));
        await Assert.ThrowsAsync<LocalConflictException>(() => resumed.IncomingAsync(Incoming("no", "confirm")));
    }

    [Theory]
    [InlineData("31/02")]
    [InlineData("28/11/2030")]
    [InlineData("31/01/2031")]
    [InlineData("1")]
    public async Task InvalidArrivalDoesNotAdvanceAndReportsExplicitAllowedRange(string input)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        await service.IncomingAsync(Incoming()); var result = await service.IncomingAsync(Incoming(input, "invalid"));
        Assert.Contains("29/11/2030", result.ReplyText); Assert.Contains("30/01/2031", result.ReplyText);
        Assert.Equal(AttendanceDialogueStep.Arrival, (await p.GetRequiredService<TRobotDbContext>().Set<AttendanceDialogue>().SingleAsync()).Step);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("32")]
    [InlineData("999")]
    [InlineData("no")]
    public async Task InvalidDaysDoNotAdvanceOrExtendExpiry(string input)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service); await SetStayEvent(p, f);
        await service.IncomingAsync(Incoming()); await service.IncomingAsync(Incoming("30/12", "arrival"));
        clock.Utc = Now.AddMinutes(1); var result = await service.IncomingAsync(Incoming(input, "invalid") with { TimestampUnixSeconds = new DateTimeOffset(clock.Utc).ToUnixTimeSeconds() });
        Assert.Contains("1 a 31", result.ReplyText);
        var dialogue = await p.GetRequiredService<TRobotDbContext>().Set<AttendanceDialogue>().SingleAsync();
        Assert.Equal(AttendanceDialogueStep.Days, dialogue.Step); Assert.Equal(Now.AddMinutes(15), dialogue.ExpiresAtUtc);
    }

    [Theory]
    [InlineData("!annulla")]
    [InlineData("!presenza no")]
    [InlineData("!presenza forse")]
    public async Task CancellationOrIndividualStatusKeepsStayAndHistoryAndStopsDialogue(string command)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        await AskStay(service); await service.IncomingAsync(Incoming("si", "confirmed"));
        await service.IncomingAsync(Incoming("!presenza si", "restart")); await service.IncomingAsync(Incoming(command, "cancel"));
        Assert.False((await service.IncomingAsync(Incoming("30/12", "after"))).Handled);
        var db = p.GetRequiredService<TRobotDbContext>(); Assert.Single(await db.Set<AttendanceStay>().ToListAsync()); Assert.Single(await db.Set<AttendanceStayChange>().ToListAsync());
        var overview = await service.AttendanceOverviewAsync(f.OrganizerPersonId, f.EventId);
        Assert.Equal(command == "!annulla" ? 4 : 0, overview.Days.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FamilyConfirmationOnlyWhenAdultAndNoMeansSenderOnly(bool all)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service); await SetStayEvent(p, f);
        var db = p.GetRequiredService<TRobotDbContext>(); var child = new Person("Nome minore non pubblicabile"); var nonparticipant = new Person("Membro non attivo");
        var family = new Household(f.EventId, "Famiglia *test* @safe", Guid.NewGuid());
        var inactive = new HouseholdMember(f.EventId, family.Id, nonparticipant.Id, HouseholdMemberKind.Adult, null, Guid.NewGuid());
        inactive.Update(HouseholdMemberKind.Adult, null, false);
        db.AddRange(child, nonparticipant, family, new EventParticipant(f.EventId, child.Id), new EventParticipant(f.EventId, nonparticipant.Id),
            new HouseholdMember(f.EventId, family.Id, person, HouseholdMemberKind.Adult, null, Guid.NewGuid()),
            new HouseholdMember(f.EventId, family.Id, child.Id, HouseholdMemberKind.Child, null, Guid.NewGuid()),
            inactive);
        await db.SaveChangesAsync(); await AskStay(service);
        var dialogue = await db.Set<AttendanceDialogue>().SingleAsync(); Assert.Equal(family.Id, dialogue.HouseholdId);
        var summary = await service.IncomingAsync(Incoming("4", "summary-again")); Assert.DoesNotContain(child.DisplayName, summary.ReplyText ?? "");
        await service.IncomingAsync(Incoming(all ? "si" : "no", "confirm"));
        Assert.Equal(all ? 2 : 1, await db.Set<AttendanceStay>().CountAsync()); Assert.Equal(all ? 2 : 1, await db.Set<AttendanceStayChange>().CountAsync());
        Assert.Equal(all ? ParticipationStatus.Confirmed : ParticipationStatus.NoResponse, (await db.EventParticipants.SingleAsync(x => x.PersonId == child.Id)).Status);
        Assert.False(await db.Set<AttendanceStay>().AnyAsync(x => x.Id == nonparticipant.Id));
        Assert.Equal(ParticipationStatus.NoResponse, (await db.EventParticipants.SingleAsync(x => x.PersonId == nonparticipant.Id)).Status);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("status")]
    [InlineData("stay")]
    [InlineData("event")]
    public async Task FamilySnapshotRejectsChangesAfterSummary(string kind)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service); await SetStayEvent(p, f);
        var db = p.GetRequiredService<TRobotDbContext>(); var child = new Person("Bambino sintetico"); var family = new Household(f.EventId, "Famiglia", Guid.NewGuid());
        var member = new HouseholdMember(f.EventId, family.Id, child.Id, HouseholdMemberKind.Child, null, Guid.NewGuid());
        db.AddRange(child, family, new EventParticipant(f.EventId, child.Id), member,
            new HouseholdMember(f.EventId, family.Id, person, HouseholdMemberKind.Adult, null, Guid.NewGuid()));
        await db.SaveChangesAsync(); await AskStay(service);
        if (kind == "member") (await db.Set<HouseholdMember>().SingleAsync(x => x.Id == member.Id)).Update(HouseholdMemberKind.Child, null, false);
        if (kind == "status") (await db.EventParticipants.SingleAsync(x => x.PersonId == child.Id)).ChangeStatus(ParticipationStatus.Absent, Now);
        if (kind == "stay") { var stay = new AttendanceStay(f.EventId, child.Id); stay.Set(new(2030, 12, 31), 1); db.Add(stay); }
        if (kind == "event") { var ev = await db.Events.SingleAsync(); ev.UpdateDetails(ev.Name, ev.StartsAtUtc.AddDays(1), ev.TimeZoneId); }
        await db.SaveChangesAsync(); var result = await service.IncomingAsync(Incoming("si", "confirm"));
        Assert.Contains("cambiati", result.ReplyText); Assert.Empty(await db.Set<AttendanceStayChange>().ToListAsync());
        Assert.False(await db.Set<AttendanceStay>().AnyAsync(x => x.Id == person));
    }

    [Fact]
    public async Task RecognizableAnswersRequireSessionAndExpiryIsAbsolute()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service); await SetStayEvent(p, f);
        Assert.False((await service.IncomingAsync(Incoming("30/12", "outside"))).Handled);
        Assert.False((await service.IncomingAsync(Incoming("ciao tutti", "chat"))).Handled);
        Assert.False((await service.IncomingAsync(Incoming("!annulla", "outside-cancel"))).Handled);
        await AskStay(service); clock.Utc = Now.AddMinutes(15);
        var result = await service.IncomingAsync(Incoming("si", "late") with { TimestampUnixSeconds = new DateTimeOffset(clock.Utc).ToUnixTimeSeconds() });
        Assert.Contains("scaduti", result.ReplyText); Assert.Empty(await p.GetRequiredService<TRobotDbContext>().Set<AttendanceStay>().ToListAsync());
    }

    [Fact]
    public async Task ControllerCallbackAuthenticatesAndAcceptsRawDateOnlyInsideMappedDialogue()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        var controller = new WhatsAppIncomingController(service, Options()) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Loopback;
        controller.Request.Headers.Authorization = "Bearer " + Options().Token;
        Assert.IsType<OkObjectResult>(await controller.Incoming(Incoming(), default));
        var raw = Assert.IsType<OkObjectResult>(await controller.Incoming(Incoming("30/12", "raw"), default));
        Assert.Contains("quanti giorni", Assert.IsType<WhatsAppIncomingResult>(raw.Value).ReplyText);
        controller.Request.Headers.Authorization = "Bearer " + new string('b', 64);
        Assert.IsType<UnauthorizedResult>(await controller.Incoming(Incoming("4", "unauthorized"), default));
        Assert.Equal(AttendanceDialogueStep.Days, (await p.GetRequiredService<TRobotDbContext>().Set<AttendanceDialogue>().SingleAsync()).Step);
    }

    [Fact]
    public async Task TwoSendersStayIsolatedAndChildCanConfirmOnlyOwnDates()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service); await SetStayEvent(p, f);
        var db = p.GetRequiredService<TRobotDbContext>(); var child = new Person("Bambino sintetico"); var family = new Household(f.EventId, "Famiglia", Guid.NewGuid());
        db.AddRange(child, family, new EventParticipant(f.EventId, child.Id),
            new WhatsAppIdentityAlias(f.EventId, Options().Fingerprint("sender", "child@lid"), child.Id),
            new HouseholdMember(f.EventId, family.Id, person, HouseholdMemberKind.Adult, null, Guid.NewGuid()),
            new HouseholdMember(f.EventId, family.Id, child.Id, HouseholdMemberKind.Child, null, Guid.NewGuid()));
        await db.SaveChangesAsync();
        await service.IncomingAsync(Incoming());
        Assert.False((await service.IncomingAsync(Incoming("30/12", "other-outside") with { SenderIds = ["child@lid"] })).Handled);
        foreach (var (text, id) in new[] { ("!presenza si", "c-start"), ("31/12", "c-arrival"), ("1", "c-days") })
            await service.IncomingAsync(Incoming(text, id) with { SenderIds = ["child@lid"] });
        Assert.Null((await db.Set<AttendanceDialogue>().SingleAsync(x => x.Id == child.Id)).HouseholdId);
        await service.IncomingAsync(Incoming("si", "c-confirm") with { SenderIds = ["child@lid"] });
        Assert.Equal(child.Id, (await db.Set<AttendanceStay>().SingleAsync()).Id);
        Assert.Equal(AttendanceDialogueStep.Arrival, (await db.Set<AttendanceDialogue>().SingleAsync(x => x.Id == person)).Step);
    }

    [Fact]
    public async Task LongNamesAreNeutralizedAndFamilySummaryDoesNotDiscloseMemberNames()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service); await SetStayEvent(p, f);
        var db = p.GetRequiredService<TRobotDbContext>(); var who = await db.People.SingleAsync(x => x.Id == person);
        who.Rename(new string('x', 135) + "*_~`@\u202e");
        var family = new Household(f.EventId, new string('y', 135) + "*_~`@\u202e", Guid.NewGuid()); var other = new Person("MEMBRO SEGRETO");
        db.AddRange(family, other, new EventParticipant(f.EventId, other.Id),
            new HouseholdMember(f.EventId, family.Id, person, HouseholdMemberKind.Adult, null, Guid.NewGuid()),
            new HouseholdMember(f.EventId, family.Id, other.Id, HouseholdMemberKind.Child, null, Guid.NewGuid()));
        await db.SaveChangesAsync();
        var start = await service.IncomingAsync(Incoming());
        await service.IncomingAsync(Incoming("30/12", "arrival")); var summary = await service.IncomingAsync(Incoming("4", "days"));
        Assert.True(start.ReplyText!.Length < 2000); Assert.True(summary.ReplyText!.Length < 2000);
        foreach (var forbidden in new[] { "*", "_", "~", "`", "@", "\u202e", other.DisplayName })
        { Assert.DoesNotContain(forbidden, start.ReplyText, StringComparison.Ordinal); Assert.DoesNotContain(forbidden, summary.ReplyText, StringComparison.Ordinal); }
        Assert.DoesNotContain("minore", summary.ReplyText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RepinInvalidatesOldDialogueAndRestartKeepsExistingStayAndHistory()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway(); var service = Service(p, gateway, new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        await AskStay(service); await service.IncomingAsync(Incoming("si", "confirmed"));
        await service.IncomingAsync(Incoming("!presenza si", "restart"));
        await service.SelectGroupAsync(f.OrganizerPersonId, f.EventId, Group, 1, true);
        var response = await service.IncomingAsync(Incoming("31/12", "after-repin"));
        Assert.Contains("collegamento cambiato", response.ReplyText);
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Single(await db.Set<AttendanceStay>().ToListAsync()); Assert.Single(await db.Set<AttendanceStayChange>().ToListAsync());
    }

    private sealed class LoopbackCallback : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        { app.Use(async (context, continuation) => { context.Connection.RemoteIpAddress = IPAddress.Loopback; await continuation(); }); next(app); };
    }

    [Fact]
    public async Task HttpCallbackBearerContractRoutesRawAnswersAndPrivateOverviewRequiresLogin()
    {
        await using var f = new LocalWebFactory(); var gateway = new FakeGateway(); var clock = new Clock();
        using var app = f.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<WhatsAppGatewayOptions>(); services.AddSingleton(Options());
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<IWhatsAppGateway>(); services.AddSingleton<IWhatsAppGateway>(gateway);
            services.AddSingleton<IStartupFilter, LoopbackCallback>();
        }));
        using (var setup = app.Services.CreateScope())
        { var p = setup.ServiceProvider; await Activate(p, f, p.GetRequiredService<WhatsAppService>()); await SetStayEvent(p, f); }
        using var client = app.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/internal/whatsapp/incoming", Incoming())).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", Options().Token);
        var response = await client.PostAsJsonAsync("/internal/whatsapp/incoming", Incoming()); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await client.PostAsJsonAsync("/internal/whatsapp/incoming", Incoming("30/12", "raw-http"));
        Assert.Contains("quanti giorni", (await raw.Content.ReadFromJsonAsync<WhatsAppIncomingResult>())!.ReplyText);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Stays?eventId=" + f.EventId)).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        var overview = await client.GetStringAsync("/Stays?eventId=" + f.EventId); Assert.Contains("Da completare", overview);
        var denied = await client.GetAsync("/Stays?eventId=" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode); Assert.Contains("Denied", denied.Headers.Location!.ToString());
        Assert.Equal(0, gateway.Sends);
    }

    [Fact]
    public async Task PreviouslyIgnoredReplyCannotEnterANewerDialogueButStartingSecondIsAllowed()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service); await SetStayEvent(p, f);
        var early = Incoming("30/12", "early"); Assert.False((await service.IncomingAsync(early)).Handled);
        clock.Utc = Now.AddSeconds(10).AddMilliseconds(700);
        await service.IncomingAsync(Incoming("!presenza si", "start") with { TimestampUnixSeconds = new DateTimeOffset(clock.Utc).ToUnixTimeSeconds() });
        Assert.False((await service.IncomingAsync(early)).Handled);
        var timely = await service.IncomingAsync(Incoming("30/12", "timely") with { TimestampUnixSeconds = new DateTimeOffset(clock.Utc).ToUnixTimeSeconds() });
        Assert.Contains("quanti giorni", timely.ReplyText);
    }

    [Fact]
    public async Task TwoAdultsConcurrentProposalsCannotSilentlyOverwriteFamilyStay()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service); await SetStayEvent(p, f);
        var db = p.GetRequiredService<TRobotDbContext>(); var other = new Person("Altro adulto sintetico"); var family = new Household(f.EventId, "Famiglia", Guid.NewGuid());
        db.AddRange(other, family, new EventParticipant(f.EventId, other.Id),
            new WhatsAppIdentityAlias(f.EventId, Options().Fingerprint("sender", "adult@lid"), other.Id),
            new HouseholdMember(f.EventId, family.Id, person, HouseholdMemberKind.Adult, null, Guid.NewGuid()),
            new HouseholdMember(f.EventId, family.Id, other.Id, HouseholdMemberKind.Adult, null, Guid.NewGuid()));
        await db.SaveChangesAsync();
        await service.IncomingAsync(Incoming("!presenza si", "a-start"));
        await service.IncomingAsync(Incoming("!presenza si", "b-start") with { SenderIds = ["adult@lid"] });
        await service.IncomingAsync(Incoming("30/12", "a-arrival")); await service.IncomingAsync(Incoming("4", "a-days"));
        await service.IncomingAsync(Incoming("31/12", "b-arrival") with { SenderIds = ["adult@lid"] });
        await service.IncomingAsync(Incoming("2", "b-days") with { SenderIds = ["adult@lid"] });
        await service.IncomingAsync(Incoming("si", "a-confirm"));
        var conflict = await service.IncomingAsync(Incoming("si", "b-confirm") with { SenderIds = ["adult@lid"] });
        Assert.Contains("cambiati", conflict.ReplyText);
        Assert.All(await db.Set<AttendanceStay>().ToListAsync(), stay => { Assert.Equal(new DateOnly(2030, 12, 30), stay.Arrival); Assert.Equal(4, stay.Days); });
        Assert.Equal(2, await db.Set<AttendanceStayChange>().CountAsync());
    }
}
