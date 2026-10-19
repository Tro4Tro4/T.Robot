using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TRobot.Application.Local;
using TRobot.Application.WhatsApp;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed partial class WhatsAppIntegrationTests
{
    [Fact]
    public async Task CollectionDeadlineIsOptionalLocalUtcAuditedRevisionedAndClearableWithGatewayDisabled()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock(), false); await SetStayEvent(p, f);
        var overview = await service.AttendanceOverviewAsync(f.OrganizerPersonId, f.EventId);
        Assert.Null(overview.Collection.DeadlineLocal); Assert.Equal(0, overview.Collection.Revision);
        var local = new DateTime(2030, 12, 29, 12, 0, 0);
        await service.SetAttendanceDeadlineAsync(f.OrganizerPersonId, f.EventId, local, 0, true);
        var db = p.GetRequiredService<TRobotDbContext>();
        var settings = await db.Set<AttendanceCollectionSettings>().SingleAsync();
        Assert.Equal(f.EventId, settings.Id); Assert.Equal(DateTimeKind.Utc, settings.DeadlineUtc!.Value.Kind);
        Assert.Equal(local.AddHours(-1), settings.DeadlineUtc); Assert.Equal(1, settings.Revision);
        overview = await service.AttendanceOverviewAsync(f.OrganizerPersonId, f.EventId);
        Assert.True(overview.Collection.Expired); Assert.Equal(local, overview.Collection.DeadlineLocal);
        await service.SetAttendanceDeadlineAsync(f.OrganizerPersonId, f.EventId, local, 1, true);
        Assert.Equal(1, (await service.AttendanceOverviewAsync(f.OrganizerPersonId, f.EventId)).Collection.Revision);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.SetAttendanceDeadlineAsync(f.OrganizerPersonId, f.EventId, null, 0, true));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.SetAttendanceDeadlineAsync(f.OrganizerPersonId, f.EventId, null, 1, false));
        await service.SetAttendanceDeadlineAsync(f.OrganizerPersonId, f.EventId, null, 1, true);
        overview = await service.AttendanceOverviewAsync(f.OrganizerPersonId, f.EventId);
        Assert.Null(overview.Collection.DeadlineLocal); Assert.Equal(2, overview.Collection.Revision);
        Assert.Equal(2, await db.Set<LocalAudit>().CountAsync(x => x.Action == "CollectionDeadlineChanged"));
    }

    [Theory]
    [InlineData(2030, 3, 31)]
    [InlineData(2030, 10, 27)]
    public async Task CollectionDeadlineRejectsMissingAndAmbiguousLocalHours(int year, int month, int day)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock(), false); await SetStayEvent(p, f);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.SetAttendanceDeadlineAsync(f.OrganizerPersonId, f.EventId,
            new DateTime(year, month, day, 2, 30, 0), 0, true));
        Assert.Empty(await p.GetRequiredService<TRobotDbContext>().Set<AttendanceCollectionSettings>().ToListAsync());
    }

    [Fact]
    public async Task CollectionDeadlineAndReminderRecheckEventPermissionsAndRejectForeignEvent()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        await Assert.ThrowsAsync<LocalAccessException>(() => service.SetAttendanceDeadlineAsync(f.OrganizerPersonId, Guid.NewGuid(), null, 0, true));
        var db = p.GetRequiredService<TRobotDbContext>();
        (await db.EventOrganizers.SingleAsync()).SetActive(false); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.SetAttendanceDeadlineAsync(f.OrganizerPersonId, f.EventId, Now, 0, true));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.DraftAttendanceReminderAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid()));
        Assert.Empty(await db.Set<AttendanceCollectionSettings>().ToListAsync()); Assert.Empty(await db.Set<WhatsAppLiveMessage>().ToListAsync());
    }

    [Fact]
    public async Task CollectionPendingIncludesUndecidedNoResponseAndMissingStayButExcludesAbsentAndComplete()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var db = p.GetRequiredService<TRobotDbContext>();
        var statuses = new[] { ParticipationStatus.Undecided, ParticipationStatus.Confirmed, ParticipationStatus.Absent, ParticipationStatus.Confirmed };
        for (var i = 0; i < statuses.Length; i++)
        {
            var person = new Person("Persona sintetica " + i);
            var participant = new EventParticipant(f.EventId, person.Id); participant.ChangeStatus(statuses[i], Now);
            db.AddRange(person, participant);
            if (i == 3)
            {
                var stay = new AttendanceStay(f.EventId, person.Id); stay.Set(new DateOnly(2030, 12, 30), 2); db.Add(stay);
            }
        }
        await db.SaveChangesAsync();
        var overview = await Service(p, new FakeGateway(), new Clock(), false).AttendanceOverviewAsync(f.OrganizerPersonId, f.EventId);
        Assert.Equal(3, overview.Pending.Count);
        Assert.DoesNotContain(overview.Pending, x => x.Status == ParticipationStatus.Absent || x.Stay is not null);
    }

    [Fact]
    public async Task CollectionReminderDraftIsGenericIdempotentAndDoesNotSendAndGroupChangesInvalidateIt()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway(); var service = Service(p, gateway, new Clock()); await Activate(p, f, service);
        var operation = Guid.NewGuid(); var id = await service.DraftAttendanceReminderAsync(f.OrganizerPersonId, f.EventId, operation);
        Assert.Equal(id, await service.DraftAttendanceReminderAsync(f.OrganizerPersonId, f.EventId, operation));
        var db = p.GetRequiredService<TRobotDbContext>(); var draft = await db.Set<WhatsAppLiveMessage>().SingleAsync();
        Assert.Equal(WhatsAppLiveMessageStatus.Draft, draft.Status); Assert.Equal(Group, draft.GroupId); Assert.Equal(0, gateway.Sends);
        Assert.Contains("!presenza si", draft.Body); Assert.Contains("!presenza modifica", draft.Body);
        foreach (var person in await db.People.ToArrayAsync()) Assert.DoesNotContain(person.DisplayName, draft.Body);
        Assert.Empty(await db.Set<OutboundMessage>().ToListAsync());
        await service.SelectGroupAsync(f.OrganizerPersonId, f.EventId, "second-group@g.us", 1, true);
        Assert.False((await service.PreviewAsync(f.OrganizerPersonId, f.EventId, id)).CanSend);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.SendAsync(f.OrganizerPersonId, f.EventId, id, 0, true));
        Assert.Equal(0, gateway.Sends);
    }

    [Fact]
    public async Task CollectionReminderRejectsWhenNothingIsMissingAndPastDeadlineDoesNotBlockResponses()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        await service.SetAttendanceDeadlineAsync(f.OrganizerPersonId, f.EventId, new DateTime(2030, 12, 1, 12, 0, 0), 0, true);
        Assert.True((await service.IncomingAsync(Incoming("!presenza no", "collection-late"))).Handled);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.DraftAttendanceReminderAsync(f.OrganizerPersonId, f.EventId, Guid.NewGuid()));
        Assert.Empty(await p.GetRequiredService<TRobotDbContext>().Set<WhatsAppLiveMessage>().ToListAsync());
    }

    [Fact]
    public async Task CollectionHttpProtectsFormsWithCsrfRevisionAndConfirmation()
    {
        await using var f = new LocalWebFactory();
        using var client = f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Stays?eventId=" + f.EventId)).StatusCode);
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        var page = await client.GetStringAsync("/Stays?eventId=" + f.EventId); token = Token(page);
        Assert.Contains("Raccolta adesioni", page); Assert.Contains("Da completare", page);
        var fields = new[] { ("EventId", f.EventId.ToString()), ("Revision", "0"), ("Confirmed", "true"), ("DeadlineLocal", "2030-12-01T12:00") };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Stays/Deadline", Form(fields))).StatusCode);
        var protectedFields = fields.Append(("__RequestVerificationToken", token)).ToArray();
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Stays/Deadline", Form(protectedFields))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/Stays/Deadline", Form(protectedFields))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Stays/Reminder", Form(("EventId", f.EventId.ToString()), ("OperationId", Guid.NewGuid().ToString())))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Stays/Deadline", Form(("EventId", f.EventId.ToString()), ("Revision", "1"), ("__RequestVerificationToken", token)))).StatusCode);
    }

    [Fact]
    public async Task CollectionHttpReminderRedirectsToExistingPreviewWithoutSending()
    {
        await using var f = new LocalWebFactory(); var gateway = new FakeGateway();
        using var app = f.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        { services.RemoveAll<WhatsAppGatewayOptions>(); services.AddSingleton(Options()); services.RemoveAll<IWhatsAppGateway>(); services.AddSingleton<IWhatsAppGateway>(gateway); }));
        using (var scope = app.Services.CreateScope()) await Activate(scope.ServiceProvider, f, Service(scope.ServiceProvider, gateway, new Clock()));
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        token = Token(await client.GetStringAsync("/Stays?eventId=" + f.EventId));
        var response = await client.PostAsync("/Stays/Reminder", Form(("EventId", f.EventId.ToString()), ("OperationId", Guid.NewGuid().ToString()), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode); Assert.Contains("/WhatsApp/Preview", response.Headers.Location!.OriginalString);
        Assert.Equal(0, gateway.Sends);
    }
}
