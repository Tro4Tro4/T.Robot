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
    private static async Task<(Event Event, Guid PersonId)> AddRubricaEventAsync(IServiceProvider p,
        LocalWebFactory f, string participantName, bool activeConnection = false)
    {
        var db = p.GetRequiredService<TRobotDbContext>();
        var person = new Person(participantName);
        var ev = new Event("Evento rubrica", new DateTime(2030, 12, 31, 12, 0, 0, DateTimeKind.Utc));
        db.AddRange(ev, person, new EventParticipant(ev.Id, person.Id), new EventOrganizer(ev.Id, f.OrganizerPersonId));
        if (activeConnection)
        {
            var connection = new WhatsAppConnection(ev.Id);
            connection.Activate(SecondGroup, f.OrganizerPersonId, Now);
            connection.UpdateFeatures(WhatsAppFeatureSet.All);
            db.Add(connection);
        }
        await db.SaveChangesAsync();
        return (ev, person.Id);
    }

    private static WhatsAppIdentityAlias Alias(Guid eventId, string sender, Guid personId) =>
        new(eventId, Options().Fingerprint("sender", sender), personId);

    [Fact]
    public async Task ActivatingAGroupAssociatesMembersWhenNameAndNumberAreBothInTheCommonRubrica()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway
        {
            AvailableGroups = [new(Group, "Gruppo sintetico"), new(SecondGroup, "Secondo evento")],
            AvailableMembers = [new(Sender, [Alternate], "Partecipante test HTTP")]
        };
        var service = Service(p, gateway, new Clock());
        var firstPerson = await Activate(p, f, service);
        var (second, secondPerson) = await AddRubricaEventAsync(p, f, "Partecipante test HTTP");

        await service.SelectGroupAsync(f.OrganizerPersonId, second.Id, SecondGroup, 0, true);

        var db = p.GetRequiredService<TRobotDbContext>();
        var aliases = await db.Set<WhatsAppIdentityAlias>().Where(x => x.EventId == second.Id).ToListAsync();
        Assert.Equal(2, aliases.Count);
        Assert.All(aliases, alias => { Assert.True(alias.IsActive); Assert.Equal(secondPerson, alias.PersonId); });
        Assert.NotEqual(firstPerson, secondPerson);
        Assert.Equal(1, await db.Set<LocalAudit>().CountAsync(x => x.EventId == second.Id && x.Action == "IdentityMapped"));
        var page = await service.ReadAsync(f.OrganizerPersonId, second.Id);
        var entry = Assert.Single(page.Rubrica);
        Assert.Equal("Partecipante test HTTP", entry.Name);
        Assert.Equal(2, entry.Identities);
        var user = await db.Users.SingleAsync(); user.IsSystemAdministrator = false; await db.SaveChangesAsync();
        Assert.Empty((await service.ReadAsync(f.OrganizerPersonId, second.Id)).Rubrica);
    }

    [Fact]
    public async Task RubricaButtonRequiresBothNameAndNumberAndDoesNotInventParticipants()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway
        {
            AvailableMembers = [
                new(Sender, [Alternate], "Partecipante test HTTP"),
                new("same-name@lid", ["same-name@s.whatsapp.net"], "Partecipante test HTTP"),
                new("other-name@lid", ["other-name@s.whatsapp.net"], "Altro nome"),
                new("outsider@lid", ["outsider@s.whatsapp.net"], "Persona senza evento")
            ]
        };
        var service = Service(p, gateway, new Clock());
        await Activate(p, f, service);
        var (second, secondPerson) = await AddRubricaEventAsync(p, f, "Partecipante test HTTP", activeConnection: true);
        var db = p.GetRequiredService<TRobotDbContext>();
        var outsider = new Person("Persona senza evento");
        // Questo numero e' in rubrica con un altro nome: il solo numero non deve bastare.
        var numberOnly = new Person("Altra persona");
        db.AddRange(outsider, new EventParticipant(f.EventId, outsider.Id),
            Alias(f.EventId, "outsider@lid", outsider.Id),
            Alias(f.EventId, "outsider@s.whatsapp.net", outsider.Id),
            numberOnly, new EventParticipant(f.EventId, numberOnly.Id),
            Alias(f.EventId, "other-name@lid", numberOnly.Id),
            Alias(f.EventId, "other-name@s.whatsapp.net", numberOnly.Id));
        await db.SaveChangesAsync();
        var connection = await p.GetRequiredService<ILocalStore>()
            .FindAsync<WhatsAppConnection>(second.Id, second.Id, default) ?? throw new InvalidOperationException();

        var result = await service.AssociateFromRubricaAsync(f.OrganizerPersonId, second.Id, connection.Revision, true);

        Assert.Equal(1, result.Associated);
        Assert.Equal(0, result.AlreadyAssociated);
        Assert.Equal(2, result.NotRecognized);
        Assert.Equal(1, result.NotParticipant);
        Assert.Equal(0, result.Ambiguous);
        Assert.Equal(0, result.Conflicts);
        var aliases = await db.Set<WhatsAppIdentityAlias>().Where(x => x.EventId == second.Id).ToListAsync();
        Assert.Equal(2, aliases.Count);
        Assert.All(aliases, alias => Assert.Equal(secondPerson, alias.PersonId));
        Assert.Equal(0, await db.Set<WhatsAppIdentityAlias>().CountAsync(x =>
            x.EventId == second.Id && x.SenderHash == Options().Fingerprint("sender", "same-name@lid")));

        var again = await service.AssociateFromRubricaAsync(f.OrganizerPersonId, second.Id, connection.Revision, true);
        Assert.Equal(0, again.Associated);
        Assert.Equal(1, again.AlreadyAssociated);
        var page = await service.ReadAsync(f.OrganizerPersonId, second.Id);
        Assert.Single(page.Rubrica); // "Persona senza evento" non e' un partecipante di questo evento.
    }

    [Fact]
    public async Task RubricaSkipsAmbiguousNamesAndExistingDifferentAssociations()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway
        {
            AvailableMembers = [
                new(Sender, [Alternate], "Partecipante test HTTP"),
                new("conflict@lid", ["conflict@s.whatsapp.net"], "Conflitto")
            ]
        };
        var service = Service(p, gateway, new Clock());
        await Activate(p, f, service);
        var (second, _) = await AddRubricaEventAsync(p, f, "Partecipante test HTTP", activeConnection: true);
        var db = p.GetRequiredService<TRobotDbContext>();
        var twin = new Person("Partecipante test HTTP");
        var conflictTarget = new Person("Conflitto");
        var other = new Person("Altra persona");
        var rubricaPerson = new Person("Conflitto");
        db.AddRange(twin, conflictTarget, other, rubricaPerson,
            new EventParticipant(second.Id, twin.Id),
            new EventParticipant(second.Id, conflictTarget.Id),
            new EventParticipant(second.Id, other.Id),
            Alias(second.Id, "conflict@lid", other.Id),
            new EventParticipant(f.EventId, rubricaPerson.Id),
            Alias(f.EventId, "conflict@lid", rubricaPerson.Id),
            Alias(f.EventId, "conflict@s.whatsapp.net", rubricaPerson.Id));
        await db.SaveChangesAsync();
        var connection = await p.GetRequiredService<ILocalStore>()
            .FindAsync<WhatsAppConnection>(second.Id, second.Id, default) ?? throw new InvalidOperationException();

        var result = await service.AssociateFromRubricaAsync(f.OrganizerPersonId, second.Id, connection.Revision, true);

        Assert.Equal(0, result.Associated);
        Assert.Equal(0, result.AlreadyAssociated);
        Assert.Equal(0, result.NotRecognized);
        Assert.Equal(1, result.Ambiguous);
        Assert.Equal(0, result.NotParticipant);
        Assert.Equal(1, result.Conflicts);
        Assert.Single(await db.Set<WhatsAppIdentityAlias>().Where(x => x.EventId == second.Id).ToListAsync());
    }

    [Fact]
    public async Task RubricaRequiresConfirmationAndCurrentRevision()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock());
        await Activate(p, f, service);
        var (second, _) = await AddRubricaEventAsync(p, f, "Partecipante test HTTP", activeConnection: true);
        var connection = await p.GetRequiredService<ILocalStore>()
            .FindAsync<WhatsAppConnection>(second.Id, second.Id, default) ?? throw new InvalidOperationException();

        await Assert.ThrowsAsync<LocalValidationException>(() =>
            service.AssociateFromRubricaAsync(f.OrganizerPersonId, second.Id, connection.Revision, false));
        await Assert.ThrowsAsync<LocalConflictException>(() =>
            service.AssociateFromRubricaAsync(f.OrganizerPersonId, second.Id, connection.Revision + 1, true));
    }

    [Fact]
    public async Task RubricaReactivatesADeactivatedAssociationOfTheTargetEvent()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway
        {
            AvailableGroups = [new(Group, "Gruppo sintetico"), new(SecondGroup, "Secondo evento")],
            AvailableMembers = [new(Sender, [Alternate], "Partecipante test HTTP")]
        };
        var service = Service(p, gateway, new Clock());
        await Activate(p, f, service);
        var (second, secondPerson) = await AddRubricaEventAsync(p, f, "Partecipante test HTTP", activeConnection: true);
        var db = p.GetRequiredService<TRobotDbContext>();
        // La coppia resta in rubrica grazie all'altro evento, ma qui l'associazione e' revocata.
        var revoked = Alias(second.Id, Sender, secondPerson);
        db.AddRange(revoked, Alias(second.Id, Alternate, secondPerson));
        await db.SaveChangesAsync();
        revoked.SetActive(false);
        await db.SaveChangesAsync();
        var connection = await p.GetRequiredService<ILocalStore>()
            .FindAsync<WhatsAppConnection>(second.Id, second.Id, default) ?? throw new InvalidOperationException();

        var result = await service.AssociateFromRubricaAsync(f.OrganizerPersonId, second.Id, connection.Revision, true);

        Assert.Equal(1, result.Associated);
        Assert.Equal(0, result.Conflicts);
        var stored = await db.Set<WhatsAppIdentityAlias>().AsNoTracking()
            .SingleAsync(x => x.EventId == second.Id && x.SenderHash == Options().Fingerprint("sender", Sender));
        Assert.True(stored.IsActive);
        var again = await service.AssociateFromRubricaAsync(f.OrganizerPersonId, second.Id, connection.Revision, true);
        Assert.Equal(0, again.Associated);
        Assert.Equal(1, again.AlreadyAssociated);
        Assert.Equal(2, await db.Set<WhatsAppIdentityAlias>().CountAsync(x => x.EventId == second.Id));
    }

    [Fact]
    public async Task DeactivatedAssociationsAreNotPartOfTheCommonRubrica()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock());
        await Activate(p, f, service);
        var db = p.GetRequiredService<TRobotDbContext>();
        foreach (var alias in await db.Set<WhatsAppIdentityAlias>().Where(x => x.EventId == f.EventId).ToListAsync())
            alias.SetActive(false);
        await db.SaveChangesAsync();
        var (second, _) = await AddRubricaEventAsync(p, f, "Partecipante test HTTP", activeConnection: true);
        var connection = await p.GetRequiredService<ILocalStore>()
            .FindAsync<WhatsAppConnection>(second.Id, second.Id, default) ?? throw new InvalidOperationException();

        var result = await service.AssociateFromRubricaAsync(f.OrganizerPersonId, second.Id, connection.Revision, true);

        Assert.Equal(0, result.Associated);
        Assert.Equal(1, result.NotRecognized);
        Assert.Empty(await db.Set<WhatsAppIdentityAlias>().Where(x => x.EventId == second.Id).ToListAsync());
    }
}
