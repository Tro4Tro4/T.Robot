using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Households;
using TRobot.Application.Local;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class HouseholdTests
{
    private static HouseholdMemberEdit Input(Guid person, int householdRevision = 0, HouseholdMemberKind kind = HouseholdMemberKind.Adult,
        Guid? companion = null, bool confirm = false, int revision = 0) =>
        new(person, kind, companion, householdRevision, revision, confirm, Guid.NewGuid());

    [Fact]
    public void DomainRejectsInvalidClassificationAndSelfReferenceWithoutMutation()
    {
        var household = new Household(Guid.NewGuid(), " Famiglia ", Guid.NewGuid());
        Assert.Equal("Famiglia", household.Name);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Household(household.EventId, new string('a', 151), Guid.NewGuid()));
        var member = new HouseholdMember(household.EventId, household.Id, Guid.NewGuid(), HouseholdMemberKind.Adult, null, Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => member.Update((HouseholdMemberKind)10, null, true));
        Assert.Throws<ArgumentException>(() => member.Update(HouseholdMemberKind.Adult, member.Id, true));
        Assert.Equal(HouseholdMemberKind.Adult, member.Kind); Assert.Equal(0, member.Revision);
        Assert.False(member.Update(HouseholdMemberKind.Adult, null, true));
        Assert.True(member.Update(HouseholdMemberKind.Child, null, true)); Assert.Equal(1, member.Revision);
    }

    [Fact]
    public async Task CreateAndAddAreIdempotentButCollidingPayloadAndStaleRevisionReject()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<HouseholdService>();
        var person = (await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().EventParticipants.SingleAsync()).PersonId;
        var id = Guid.NewGuid();
        Assert.Equal(id, await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "Nucleo", id));
        Assert.Equal(id, await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, " Nucleo ", id));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "Diverso", id));
        var input = Input(person);
        var memberId = await service.AddAsync(factory.OrganizerPersonId, factory.EventId, id, input);
        Assert.Equal(memberId, await service.AddAsync(factory.OrganizerPersonId, factory.EventId, id, input));
        Assert.Equal(id, await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "Nucleo", id));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.AddAsync(factory.OrganizerPersonId, factory.EventId, id, input with { Kind = HouseholdMemberKind.Child }));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, id, memberId, input with { Confirm = true, Kind = HouseholdMemberKind.Child }, true));
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<LocalAudit>().CountAsync(x => x.Module == "Households"));
    }

    [Fact]
    public async Task OnlyActualParticipantsOfSameEventMayJoinAndReferencesDoNotCrossEvents()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<HouseholdService>();
        var other = new Event("Altro evento", DateTime.UtcNow); var outsider = new Person("Altro partecipante");
        db.AddRange(other, outsider, new EventParticipant(other.Id, outsider.Id), new EventOrganizer(other.Id, factory.OrganizerPersonId)); await db.SaveChangesAsync();
        var first = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "Primo", Guid.NewGuid());
        var second = await service.CreateAsync(factory.OrganizerPersonId, other.Id, "Secondo", Guid.NewGuid());
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AddAsync(factory.OrganizerPersonId, factory.EventId, first, Input(factory.OrganizerPersonId)));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AddAsync(factory.OrganizerPersonId, factory.EventId, first, Input(outsider.Id)));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.AddAsync(factory.OrganizerPersonId, factory.EventId, second, Input(outsider.Id)));
        Assert.Empty((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Members);
    }

    [Fact]
    public async Task PersonWithoutSenderHasOneActiveHouseholdAndRemovalPreservesPresenceAndHistory()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<HouseholdService>();
        var person = new Person("Senza WhatsApp"); var participation = new EventParticipant(factory.EventId, person.Id);
        participation.ChangeStatus(ParticipationStatus.Confirmed, DateTime.UtcNow); db.AddRange(person, participation); await db.SaveChangesAsync();
        var a = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "A", Guid.NewGuid());
        var b = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "B", Guid.NewGuid());
        var member = await service.AddAsync(factory.OrganizerPersonId, factory.EventId, a, Input(person.Id, kind: HouseholdMemberKind.Child));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AddAsync(factory.OrganizerPersonId, factory.EventId, b, Input(person.Id)));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, a, member, Input(person.Id, 1, HouseholdMemberKind.Child), false));
        Assert.True(await service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, a, member, Input(person.Id, 1, HouseholdMemberKind.Child, confirm: true), false));
        await service.AddAsync(factory.OrganizerPersonId, factory.EventId, b, Input(person.Id));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, a, member,
            Input(person.Id, 2, HouseholdMemberKind.Child, confirm: true, revision: 1), true));
        var memberships = (await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Members;
        Assert.Equal(2, memberships.Count); Assert.Single(memberships, x => x.IsActive);
        Assert.Equal(ParticipationStatus.Confirmed, (await db.EventParticipants.SingleAsync(x => x.PersonId == person.Id)).Status);
        Assert.Null((await db.People.SingleAsync(x => x.Id == person.Id)).WhatsAppSenderId);
    }

    [Fact]
    public async Task AddCannotBypassConfirmedReactivationWithNewOperationId()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<HouseholdService>();
        var person = (await db.EventParticipants.SingleAsync()).PersonId;
        var h = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "Nucleo", Guid.NewGuid());
        var member = await service.AddAsync(factory.OrganizerPersonId, factory.EventId, h, Input(person));
        await service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, member, Input(person, 1, confirm: true), false);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AddAsync(factory.OrganizerPersonId, factory.EventId, h,
            Input(person, 2, confirm: true)));
        var page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(member, Assert.Single(page.Members).Id); Assert.False(page.Members[0].IsActive);
        Assert.Equal(2, page.Households[0].Revision);
        Assert.Equal(3, await db.Set<LocalAudit>().CountAsync(x => x.Module == "Households"));
        Assert.True(await service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, member,
            Input(person, 2, confirm: true, revision: 1), true));
        Assert.Single((await service.ListAsync(factory.OrganizerPersonId, factory.EventId)).Members);
    }

    [Fact]
    public async Task ReactivationRequiresConfirmationAndBothCurrentRevisionsAndKeepsSameMembership()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<HouseholdService>();
        var person = (await db.EventParticipants.SingleAsync()).PersonId;
        var h = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "Nucleo", Guid.NewGuid());
        var input = Input(person);
        var member = await service.AddAsync(factory.OrganizerPersonId, factory.EventId, h, input);
        Assert.True(await service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, member,
            input with { HouseholdRevision = 1, Confirm = true }, false));
        var reactivation = input with { HouseholdRevision = 2, Revision = 1 };
        await Assert.ThrowsAsync<LocalValidationException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, member, reactivation, true));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, member,
            reactivation with { Confirm = true, Revision = 0 }, true));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, member,
            reactivation with { Confirm = true, HouseholdRevision = 1 }, true));
        Assert.True(await service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, member, reactivation with { Confirm = true }, true));
        var page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(member, Assert.Single(page.Members).Id);
        Assert.True(page.Members[0].IsActive); Assert.Equal(2, page.Members[0].Revision);
        Assert.Equal(3, Assert.Single(page.Households).Revision);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.AddAsync(factory.OrganizerPersonId, factory.EventId, h, input));
        Assert.Equal(4, await db.Set<LocalAudit>().CountAsync(x => x.Module == "Households"));
    }

    [Fact]
    public async Task CompanionMustReferenceActiveAdultPrimaryInSameHouseholdAndParentCannotBeRemoved()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<HouseholdService>();
        var parent = (await db.EventParticipants.SingleAsync()).PersonId;
        var child = new Person("Bambino"); var friend = new Person("Amico");
        db.AddRange(child, friend, new EventParticipant(factory.EventId, child.Id), new EventParticipant(factory.EventId, friend.Id)); await db.SaveChangesAsync();
        var h = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "Nucleo", Guid.NewGuid());
        var primary = await service.AddAsync(factory.OrganizerPersonId, factory.EventId, h, Input(parent));
        var childMember = await service.AddAsync(factory.OrganizerPersonId, factory.EventId, h, Input(child.Id, 1, HouseholdMemberKind.Child, primary));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AddAsync(factory.OrganizerPersonId, factory.EventId, h, Input(friend.Id, 2, companion: childMember)));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AddAsync(factory.OrganizerPersonId, factory.EventId, h, Input(friend.Id, 2, companion: Guid.NewGuid())));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, primary, Input(parent, 2, confirm: true), false));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, primary, Input(parent, 2, HouseholdMemberKind.Child, confirm: true), true));
        Assert.True(await service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, childMember, Input(child.Id, 2, HouseholdMemberKind.Child, confirm: true), true));
        Assert.True(await service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, primary, Input(parent, 3, confirm: true), false));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AddAsync(factory.OrganizerPersonId, factory.EventId, h, Input(friend.Id, 4, companion: primary)));
    }

    [Fact]
    public async Task ActiveAdultCompanionCannotBeReferenceAndOtherHouseholdPrimaryRejects()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); var service = scope.ServiceProvider.GetRequiredService<HouseholdService>();
        var parent = (await db.EventParticipants.SingleAsync()).PersonId;
        var adult = new Person("Accompagnatore adulto"); var third = new Person("Terzo");
        db.AddRange(adult, third, new EventParticipant(factory.EventId, adult.Id), new EventParticipant(factory.EventId, third.Id)); await db.SaveChangesAsync();
        var h = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "Nucleo", Guid.NewGuid());
        var other = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "Altro", Guid.NewGuid());
        var primary = await service.AddAsync(factory.OrganizerPersonId, factory.EventId, h, Input(parent));
        var companion = await service.AddAsync(factory.OrganizerPersonId, factory.EventId, h, Input(adult.Id, 1, companion: primary));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AddAsync(factory.OrganizerPersonId, factory.EventId, h, Input(third.Id, 2, companion: companion)));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.AddAsync(factory.OrganizerPersonId, factory.EventId, other, Input(third.Id, companion: primary)));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, primary,
            Input(parent, 2, companion: companion, confirm: true), true));
    }

    [Fact]
    public async Task DisabledAndRevokedOrganizersCannotReadOrChangeHouseholds()
    {
        await using var factory = new LocalWebFactory(); using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); var service = scope.ServiceProvider.GetRequiredService<HouseholdService>();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ListAsync(Guid.NewGuid(), factory.EventId));
        var user = await db.Users.SingleAsync(); user.IsEnabled = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "Vietato", Guid.NewGuid()));
        user = await db.Users.SingleAsync(); user.IsEnabled = true; var assignment = await db.EventOrganizers.SingleAsync(); assignment.SetActive(false); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ListAsync(factory.OrganizerPersonId, factory.EventId));
        Assert.Empty(await db.Set<Household>().ToArrayAsync());
    }

    [Fact]
    public async Task AuditFailureRollsBackMemberHouseholdRevisionAndAudit()
    {
        var interceptor = new FailAudit(); await using var factory = new LocalWebFactory { SaveInterceptor = interceptor };
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<HouseholdService>(); var person = (await db.EventParticipants.SingleAsync()).PersonId;
        var h = await service.CreateAsync(factory.OrganizerPersonId, factory.EventId, "Nucleo", Guid.NewGuid());
        var member = await service.AddAsync(factory.OrganizerPersonId, factory.EventId, h, Input(person));
        interceptor.Armed = true;
        await Assert.ThrowsAsync<LocalValidationException>(() => service.UpdateAsync(factory.OrganizerPersonId, factory.EventId, h, member, Input(person, 1, HouseholdMemberKind.Child, confirm: true), false));
        interceptor.Armed = false; var page = await service.ListAsync(factory.OrganizerPersonId, factory.EventId);
        Assert.Equal(1, page.Households.Single().Revision); Assert.True(page.Members.Single().IsActive);
        Assert.Equal(0, page.Members.Single().Revision); Assert.Equal(HouseholdMemberKind.Adult, page.Members.Single().Kind);
        Assert.Equal(2, await db.Set<LocalAudit>().CountAsync());
    }

    [Fact]
    public async Task HttpProtectsCsrfEncodesNameAndRequiresConfirmationAndCurrentRevisions()
    {
        await using var factory = new LocalWebFactory(); using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/Households?eventId=" + factory.EventId)).StatusCode);
        var login = Token(await client.GetStringAsync("/Access/Login"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", login)))).StatusCode);
        var create = await client.GetStringAsync("/Households/Create?eventId=" + factory.EventId); var token = Token(create); var h = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Households/Create", Form(("EventId", factory.EventId.ToString()), ("Name", "x")))).StatusCode);
        var data = new[] { ("EventId", factory.EventId.ToString()), ("Name", "<script>nome</script>"), ("OperationId", h.ToString()), ("__RequestVerificationToken", token) };
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Households/Create", Form(data))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Households/Create", Form(data))).StatusCode);
        Assert.Contains("&lt;script&gt;", await client.GetStringAsync("/Households?eventId=" + factory.EventId));
        Guid person; using (var scope = factory.Services.CreateScope()) person = (await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().EventParticipants.SingleAsync()).PersonId;
        var add = await client.GetStringAsync($"/Households/Add?eventId={factory.EventId}&householdId={h}"); var member = Guid.NewGuid();
        var addition = new[] { ("EventId", factory.EventId.ToString()), ("HouseholdId", h.ToString()), ("OperationId", member.ToString()), ("PersonId", person.ToString()), ("__RequestVerificationToken", Token(add)) };
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Households/Save", Form(addition))).StatusCode);
        var edit = await client.GetStringAsync($"/Households/Edit?eventId={factory.EventId}&id={member}");
        var change = new[] { ("EventId", factory.EventId.ToString()), ("HouseholdId", h.ToString()), ("Id", member.ToString()), ("PersonId", person.ToString()), ("Kind", "1"), ("HouseholdRevision", "1"), ("Revision", "0"), ("__RequestVerificationToken", Token(edit)) };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Households/Save", Form(change))).StatusCode);
        var confirmed = change.Append(("Confirm", "true")).ToArray();
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Households/Save", Form(confirmed))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/Households/Save", Form(confirmed))).StatusCode);
    }

    private sealed class FailAudit : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<LocalAudit>().Any(x => x.State == EntityState.Added)) throw new InvalidOperationException("Audit simulato fallito.");
            return ValueTask.FromResult(result);
        }
    }
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] pairs) => new(pairs.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
}
