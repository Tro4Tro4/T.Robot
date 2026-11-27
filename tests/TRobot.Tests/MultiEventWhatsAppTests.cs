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
    private const string SecondGroup = "second-event-group@g.us";

    private static async Task<(Guid PersonId, Event SecondEvent, WhatsAppConnection Connection)> AddSecondEventAsync(
        IServiceProvider p, LocalWebFactory f, Guid personId, WhatsAppFeatureSet? features = null)
    {
        var db = p.GetRequiredService<TRobotDbContext>();
        var second = new Event("Secondo evento WhatsApp", new DateTime(2030, 12, 31, 12, 0, 0, DateTimeKind.Utc));
        var connection = new WhatsAppConnection(second.Id);
        connection.Activate(SecondGroup, f.OrganizerPersonId, Now);
        connection.UpdateFeatures(features ?? WhatsAppFeatureSet.All);
        db.AddRange(second, new EventParticipant(second.Id, personId),
            new EventOrganizer(second.Id, f.OrganizerPersonId), connection);
        foreach (var sender in Incoming().SenderIds)
            db.Add(new WhatsAppIdentityAlias(second.Id, Options().Fingerprint("sender", sender), personId));
        await db.SaveChangesAsync();
        return (personId, second, connection);
    }

    [Fact]
    public async Task MultiEventMessagesUseOnlyTheGroupAndConnectionOfTheirEvent()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service);
        var (_, second, connection) = await AddSecondEventAsync(p, f, person);
        var db = p.GetRequiredService<TRobotDbContext>();

        var secondReply = await service.IncomingAsync(Incoming("!aiuto", "second-event-help") with { GroupId = SecondGroup });
        Assert.True(secondReply.Handled);
        Assert.Single(await db.Set<WhatsAppCommandReceipt>().Where(x => x.EventId == second.Id).ToListAsync());
        Assert.Empty(await db.Set<WhatsAppCommandReceipt>().Where(x => x.EventId == f.EventId).ToListAsync());

        Assert.Equal(SecondGroup, connection.GroupId);
        Assert.Equal(f.EventId, (await db.Set<WhatsAppConnection>().SingleAsync(x => x.GroupId == Group)).EventId);
    }

    [Fact]
    public async Task FeatureFlagsGateCommandsAndSingleGroupDeactivationKeepsDeviceConnected()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway(); var service = Service(p, gateway, new Clock()); await Activate(p, f, service);
        var store = p.GetRequiredService<ILocalStore>();
        var connection = await store.FindAsync<WhatsAppConnection>(f.EventId, f.EventId, default) ?? throw new InvalidOperationException();

        Assert.True(await service.UpdateFeaturesAsync(f.OrganizerPersonId, f.EventId,
            new(WhatsAppFeature.Attendance), connection.FeaturesRevision, true));
        Assert.False((await service.IncomingAsync(Incoming("!aiuto", "feature-off"))).Handled);
        Assert.True((await service.IncomingAsync(Incoming("!presenza si", "feature-on"))).Handled);

        var groupSets = gateway.GroupSets;
        Assert.True(await service.DeactivateGroupAsync(f.OrganizerPersonId, f.EventId, connection.Revision, true));
        Assert.False((await store.FindAsync<WhatsAppConnection>(f.EventId, f.EventId, default))!.Enabled);
        Assert.Equal(0, gateway.Disconnects); Assert.Equal(groupSets + 1, gateway.GroupSets);
    }

    [Fact]
    public async Task PrivateMealMessageWithTwoOpenSessionsIsIgnoredWithoutTouchingEitherEvent()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service);
        var (_, second, connection) = await AddSecondEventAsync(p, f, person);
        var db = p.GetRequiredService<TRobotDbContext>();
        var firstConnection = await db.Set<WhatsAppConnection>().SingleAsync(x => x.EventId == f.EventId);

        foreach (var (ev, conn) in new[] { (await db.Events.SingleAsync(x => x.Id == f.EventId), firstConnection), (second, connection) })
        {
            var aliases = await db.Set<WhatsAppIdentityAlias>().Where(x => x.EventId == ev.Id).ToListAsync();
            var identity = Options().Fingerprint("meal-identity", $"event:{ev.Revision};" + string.Join(';', aliases
                .Where(x => x.PersonId == person).OrderBy(x => x.Id).Select(x => $"{x.Id}:{x.Revision}:{x.IsActive}")));
            var session = new MealProposalSession(ev.Id, person);
            session.Start(f.OrganizerPersonId, conn.GroupId!, conn.Revision, identity, Now);
            session.SetDay(new DateOnly(2030, 12, 31)); session.SetMeal(MealKind.Dinner); session.SetDish("Lasagne");
            session.SetAvailability(true); db.Add(session);
        }
        await db.SaveChangesAsync();

        Assert.False((await service.IncomingAsync(Incoming("!conferma", "ambiguous-private") with { IsPrivate = true })).Handled);
        Assert.Empty(await db.Set<MealProposal>().ToListAsync());
        Assert.All(await db.Set<MealProposalSession>().ToListAsync(), x => Assert.Equal(MealProposalStep.Confirmation, x.Step));
    }
}
