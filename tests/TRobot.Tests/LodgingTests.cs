using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Local;
using TRobot.Application.Lodging;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class LodgingTests
{
    [Fact]
    public void DomainValidatesCapacityAndTextBeforeChangingRoom()
    {
        var room = new LodgingRoom(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " Camera ", 2, " Note ");
        Assert.Equal("Camera", room.Name); Assert.Equal("Note", room.Notes);
        var revision = room.Revision;
        Assert.Throws<ArgumentOutOfRangeException>(() => room.Update("Mutata", 0, null));
        Assert.Equal("Camera", room.Name); Assert.Equal(2, room.Capacity); Assert.Equal(revision, room.Revision);
        Assert.Throws<ArgumentOutOfRangeException>(() => room.Update(new string('a', 151), 2, null));
    }

    [Fact]
    public async Task UnassignedIncludesAllStatusesAndPeopleWithoutHouseholdAndStartsDraft()
    {
        await using var f = new Fixture();
        var page = await f.Page();
        Assert.False(page.IsFinal); Assert.Equal(0, page.Version.Revision); Assert.Empty(page.Assignments);
        Assert.Single(Assert.Single(page.Households).People); Assert.Null(page.Households[0].Id);
        foreach (var status in Enum.GetValues<ParticipationStatus>())
        {
            var p = new Person(status.ToString()); var participation = new EventParticipant(f.EventId, p.Id);
            participation.ChangeStatus(status, DateTime.UtcNow); f.Db.AddRange(p, participation);
        }
        await f.Db.SaveChangesAsync();
        page = await f.Page(); Assert.Equal(5, page.Households.SelectMany(x => x.People).Count());
        Assert.Equal(1, page.UnassignedConfirmed);
    }

    [Fact]
    public async Task HouseholdCanSplitAndChildrenFromDifferentHouseholdsCanShareRoom()
    {
        await using var f = new Fixture();
        var a = await f.Room(3); var b = await f.Room(3);
        var h = new Household(f.EventId, "Nucleo A", Guid.NewGuid()); var other = new Household(f.EventId, "Nucleo B", Guid.NewGuid());
        var child = new Person("Bambino A"); var secondChild = new Person("Bambino B");
        f.Db.AddRange(h, other, child, secondChild, new EventParticipant(f.EventId, child.Id), new EventParticipant(f.EventId, secondChild.Id),
            new HouseholdMember(f.EventId, h.Id, f.PersonId, HouseholdMemberKind.Adult, null, Guid.NewGuid()),
            new HouseholdMember(f.EventId, h.Id, child.Id, HouseholdMemberKind.Child, null, Guid.NewGuid()),
            new HouseholdMember(f.EventId, other.Id, secondChild.Id, HouseholdMemberKind.Child, null, Guid.NewGuid()));
        await f.Db.SaveChangesAsync();
        await f.Service.AssignHouseholdAsync(f.ActorId, f.EventId, (await f.Page()).Version, h.Id, a);
        await f.Assign(child.Id, b); await f.Assign(secondChild.Id, b);
        var page = await f.Page(); Assert.Equal(a, page.RoomFor(f.PersonId));
        Assert.Equal(b, page.RoomFor(child.Id)); Assert.Equal(b, page.RoomFor(secondChild.Id));
        Assert.Equal(2, page.Occupancy(b)); Assert.Equal(3, page.Assignments.Count);
        await f.Service.AssignHouseholdAsync(f.ActorId, f.EventId, page.Version, h.Id, b);
        page = await f.Page(); Assert.Equal(3, page.Assignments.Count); Assert.Equal(3, page.Occupancy(b));
    }

    [Fact]
    public async Task CapacityPreventsOverflowAndHouseholdMoveIsAtomicAndCanReuseOwnPlaces()
    {
        await using var f = new Fixture(); var a = await f.Room(2); var b = await f.Room(1);
        var second = new Person("Secondo"); var h = new Household(f.EventId, "Famiglia", Guid.NewGuid());
        f.Db.AddRange(second, new EventParticipant(f.EventId, second.Id), h,
            new HouseholdMember(f.EventId, h.Id, f.PersonId, HouseholdMemberKind.Adult, null, Guid.NewGuid()),
            new HouseholdMember(f.EventId, h.Id, second.Id, HouseholdMemberKind.Child, null, Guid.NewGuid()));
        await f.Db.SaveChangesAsync(); await f.Assign(f.PersonId, a); await f.Assign(second.Id, b);
        var before = await f.Page(); var audits = await f.Db.Set<LocalAudit>().CountAsync();
        await Assert.ThrowsAsync<LocalValidationException>(() => f.Service.AssignHouseholdAsync(f.ActorId, f.EventId, before.Version, h.Id, b));
        Assert.Equal(before.Version, (await f.Page()).Version); Assert.Equal(a, (await f.Page()).RoomFor(f.PersonId));
        Assert.Equal(audits, await f.Db.Set<LocalAudit>().CountAsync());
        await f.Service.AssignHouseholdAsync(f.ActorId, f.EventId, before.Version, h.Id, a);
        Assert.Equal(2, (await f.Page()).Occupancy(a));
        var room = (await f.Page()).Rooms.Single(x => x.Id == a);
        var version = (await f.Page()).Version;
        await Assert.ThrowsAsync<LocalValidationException>(() => f.Service.SaveRoomAsync(f.ActorId, f.EventId, version, room.Id, room.ApartmentId, "Ridotta", 1, null));
    }

    [Fact]
    public async Task RemovalAndMovingPreserveOneAssignmentAndAuditedHistory()
    {
        await using var f = new Fixture(); var a = await f.Room(1); var b = await f.Room(1);
        await f.Assign(f.PersonId, a); var id = (await f.Page()).Assignments.Single().Id;
        await f.Assign(f.PersonId, b); await f.Assign(f.PersonId, null);
        var page = await f.Page(); Assert.Null(page.RoomFor(f.PersonId)); Assert.Equal(id, page.Assignments.Single().Id);
        await f.Assign(f.PersonId, a); Assert.Equal(id, (await f.Page()).Assignments.Single().Id);
        Assert.Equal(4, await f.Db.Set<LocalAudit>().CountAsync(x => x.EntityId == id));
        Assert.Equal(ParticipationStatus.NoResponse, (await f.Db.EventParticipants.SingleAsync()).Status);
    }

    [Fact]
    public async Task FinalizationRequiresConfirmedAssignmentsAndEveryEditReturnsDraft()
    {
        await using var f = new Fixture(); var room = await f.Room(1);
        var participation = await f.Db.EventParticipants.SingleAsync(); participation.ChangeStatus(ParticipationStatus.Confirmed, DateTime.UtcNow); await f.Db.SaveChangesAsync();
        var page = await f.Page();
        await Assert.ThrowsAsync<LocalValidationException>(() => f.Service.FinalizeAsync(f.ActorId, f.EventId, page.Version));
        await f.Assign(f.PersonId, room); await f.Service.FinalizeAsync(f.ActorId, f.EventId, (await f.Page()).Version);
        Assert.True((await f.Page()).IsFinal);
        await f.Assign(f.PersonId, null); Assert.False((await f.Page()).IsFinal);
    }

    [Fact]
    public async Task RepeatingUnchangedAssignmentPreservesFinalizedPlanRevisionAndAudit()
    {
        await using var f = new Fixture(); var room = await f.Room(1); await f.Assign(f.PersonId, room);
        await f.Service.FinalizeAsync(f.ActorId, f.EventId, (await f.Page()).Version);
        var before = await f.Page(); var audits = await f.Db.Set<LocalAudit>().CountAsync();
        Assert.False(await f.Service.AssignPersonAsync(f.ActorId, f.EventId, before.Version, f.PersonId, room));
        var after = await f.Page(); Assert.True(after.IsFinal); Assert.Equal(before.Version, after.Version);
        Assert.Equal(audits, await f.Db.Set<LocalAudit>().CountAsync());
    }

    [Fact]
    public async Task ExternalPresenceAndHouseholdChangesInvalidateFinalizationAndStaleForms()
    {
        await using var f = new Fixture(); var room = await f.Room(2); await f.Assign(f.PersonId, room);
        await f.Service.FinalizeAsync(f.ActorId, f.EventId, (await f.Page()).Version); var finalized = await f.Page();
        Assert.True(finalized.IsFinal);
        var participation = await f.Db.EventParticipants.SingleAsync(); participation.ChangeStatus(ParticipationStatus.Absent, DateTime.UtcNow); await f.Db.SaveChangesAsync();
        Assert.True((await f.Page()).FinalizationOutdated); Assert.False((await f.Page()).IsFinal);
        await Assert.ThrowsAsync<LocalConflictException>(() => f.Service.AssignPersonAsync(f.ActorId, f.EventId, finalized.Version, f.PersonId, null));
        await f.Service.FinalizeAsync(f.ActorId, f.EventId, (await f.Page()).Version);
        var h = new Household(f.EventId, "Nuovo nucleo", Guid.NewGuid()); f.Db.AddRange(h,
            new HouseholdMember(f.EventId, h.Id, f.PersonId, HouseholdMemberKind.Adult, null, Guid.NewGuid())); await f.Db.SaveChangesAsync();
        Assert.True((await f.Page()).FinalizationOutdated);
    }

    [Fact]
    public async Task CrossEventReferencesAndNonParticipantsRejectAndRevocationBlocksReadAndWrite()
    {
        await using var f = new Fixture(); var room = await f.Room(1);
        await Assert.ThrowsAsync<LocalValidationException>(() => f.Assign(f.ActorId, room));
        await Assert.ThrowsAsync<LocalValidationException>(() => f.Assign(f.PersonId, Guid.NewGuid()));
        await Assert.ThrowsAsync<LocalAccessException>(() => f.Service.ReadAsync(Guid.NewGuid(), f.EventId));
        var stale = (await f.Page()).Version;
        var organizer = await f.Db.EventOrganizers.SingleAsync(); organizer.SetActive(false); await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => f.Page());
        await Assert.ThrowsAsync<LocalAccessException>(() => f.Service.AssignPersonAsync(f.ActorId, f.EventId, stale, f.PersonId, room));
        Assert.Empty(await f.Db.Set<LodgingAssignment>().ToArrayAsync());
    }

    [Fact]
    public async Task AuditFailureRollsBackAssignmentsAndPlanRevision()
    {
        var fail = new FailAudit(); await using var f = new Fixture(fail); var room = await f.Room(1);
        var before = await f.Page(); fail.Armed = true;
        await Assert.ThrowsAsync<LocalValidationException>(() => f.Assign(f.PersonId, room));
        fail.Armed = false;
        Assert.Equal(before.Version, (await f.Page()).Version); Assert.Empty((await f.Page()).Assignments);
    }

    [Fact]
    public async Task HttpRequiresLoginAndCsrfEncodesNamesAndPrintOnlyShowsSelectedHousehold()
    {
        await using var f = new Fixture(); var room = await f.Room(3); await f.Assign(f.PersonId, room);
        var h = new Household(f.EventId, "Nucleo riservato", Guid.NewGuid()); var external = new Person("Occupante esterno segreto");
        f.Db.AddRange(h, new HouseholdMember(f.EventId, h.Id, f.PersonId, HouseholdMemberKind.Adult, null, Guid.NewGuid()), external, new EventParticipant(f.EventId, external.Id));
        await f.Db.SaveChangesAsync(); await f.Assign(external.Id, room);
        using var client = f.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var path = "/Lodging?eventId=" + f.EventId;
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync($"/Lodging/Print?eventId={f.EventId}&householdId={h.Id}")).StatusCode);
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Lodging/Apartment", Form(("EventId", f.EventId.ToString())))).StatusCode);
        var html = await client.GetStringAsync(path); var page = await f.Page();
        await client.PostAsync("/Lodging/Apartment", Form(("EventId", f.EventId.ToString()), ("Id", Guid.NewGuid().ToString()), ("Name", "<script>nome</script>"),
            ("Revision", page.Version.Revision.ToString()), ("Fingerprint", page.Version.Fingerprint), ("__RequestVerificationToken", Token(html))));
        Assert.Contains("&lt;script&gt;nome&lt;/script&gt;", await client.GetStringAsync(path));
        var print = await client.GetStringAsync($"/Lodging/Print?eventId={f.EventId}&householdId={h.Id}");
        Assert.Contains("Partecipante test HTTP", print); Assert.DoesNotContain("Occupante esterno segreto", print); Assert.Contains("Piano in bozza", print);
        await f.Assign(f.PersonId, null);
        Assert.Contains("Camera ancora non assegnata", await client.GetStringAsync($"/Lodging/Print?eventId={f.EventId}&householdId={h.Id}"));
    }

    [Fact]
    public async Task HttpFormsCreateRoomsAssignSplitNucleusPrintAndRejectOtherEventAndRevokedAccess()
    {
        await using var f = new Fixture();
        using var client = f.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        var path = "/Lodging?eventId=" + f.EventId;
        async Task<HttpResponseMessage> Post(string action, params (string Key, string Value)[] extra)
        {
            var html = await client.GetStringAsync(path); var page = await f.Page();
            var data = new[] { ("EventId", f.EventId.ToString()), ("Revision", page.Version.Revision.ToString()), ("Fingerprint", page.Version.Fingerprint),
                ("__RequestVerificationToken", Token(html)) }.Concat(extra).ToArray();
            return await client.PostAsync("/Lodging/" + action, Form(data));
        }
        foreach (var action in new[] { "Apartment", "Room", "Person", "Household", "FinalizePlan" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Lodging/" + action, Form(("EventId", f.EventId.ToString())))).StatusCode);
        var apartment = Guid.NewGuid(); var red = Guid.NewGuid(); var blue = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Redirect, (await Post("Apartment", ("Id", apartment.ToString()), ("Name", "Casa <test>"), ("Notes", "Ingresso <script>nota</script>"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post("Room", ("Id", red.ToString()), ("ApartmentId", apartment.ToString()), ("Name", "Rossa"), ("Capacity", "2"), ("Notes", "Note <b>camera</b>"))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post("Room", ("Id", blue.ToString()), ("ApartmentId", apartment.ToString()), ("Name", "Blu"), ("Capacity", "2"))).StatusCode);
        Assert.Equal(2, (await f.Page()).Rooms.Count);
        var child = new Person("Bambino <test>"); var h = new Household(f.EventId, "Nucleo HTTP", Guid.NewGuid());
        f.Db.AddRange(child, h, new EventParticipant(f.EventId, child.Id),
            new HouseholdMember(f.EventId, h.Id, f.PersonId, HouseholdMemberKind.Adult, null, Guid.NewGuid()),
            new HouseholdMember(f.EventId, h.Id, child.Id, HouseholdMemberKind.Child, null, Guid.NewGuid()));
        await f.Db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await Post("Household", ("HouseholdId", h.Id.ToString()), ("RoomId", red.ToString()))).StatusCode);
        Assert.Equal(2, (await f.Page()).Occupancy(red));
        Assert.Equal(HttpStatusCode.Redirect, (await Post("Person", ("PersonId", child.Id.ToString()), ("RoomId", blue.ToString()))).StatusCode);
        Assert.Equal(1, (await f.Page()).Occupancy(red)); Assert.Equal(1, (await f.Page()).Occupancy(blue));
        var printPath = $"/Lodging/Print?eventId={f.EventId}&householdId={h.Id}";
        var print = await client.GetStringAsync(printPath);
        Assert.Contains("Rossa", print); Assert.Contains("Blu", print); Assert.Contains("Bambino &lt;test&gt;", print);
        Assert.Contains("Ingresso &lt;script&gt;nota&lt;/script&gt;", print); Assert.DoesNotContain("<script>nota</script>", print);
        Assert.Equal(HttpStatusCode.Redirect, (await Post("FinalizePlan", ("Confirmed", "true"))).StatusCode);
        Assert.Contains("Piano definitivo", await client.GetStringAsync(printPath));
        Assert.Equal(HttpStatusCode.Redirect, (await Post("Person", ("PersonId", child.Id.ToString()), ("RoomId", ""))).StatusCode);
        Assert.Contains("Camera ancora non assegnata", await client.GetStringAsync(printPath));
        var foreign = new Event("Non autorizzato", DateTime.UtcNow); f.Db.Add(foreign); await f.Db.SaveChangesAsync();
        AssertDenied(await client.GetAsync("/Lodging?eventId=" + foreign.Id));
        AssertDenied(await client.GetAsync($"/Lodging/Print?eventId={foreign.Id}&householdId={h.Id}"));
        var current = await f.Page(); token = Token(await client.GetStringAsync(path));
        AssertDenied(await client.PostAsync("/Lodging/Person", Form(("EventId", foreign.Id.ToString()), ("Revision", "0"), ("Fingerprint", current.Version.Fingerprint),
            ("PersonId", f.PersonId.ToString()), ("RoomId", red.ToString()), ("__RequestVerificationToken", token))));
        var assignment = await f.Db.EventOrganizers.SingleAsync(); assignment.SetActive(false); await f.Db.SaveChangesAsync();
        AssertDenied(await client.GetAsync(printPath));
        AssertDenied(await client.PostAsync("/Lodging/Person", Form(("EventId", f.EventId.ToString()), ("Revision", current.Version.Revision.ToString()), ("Fingerprint", current.Version.Fingerprint),
            ("PersonId", f.PersonId.ToString()), ("RoomId", blue.ToString()), ("__RequestVerificationToken", token))));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Access/Denied")).StatusCode);
        Assert.Equal(red, (await f.Db.Set<LodgingAssignment>().AsNoTracking().SingleAsync(x => x.PersonId == f.PersonId)).RoomId);
        Assert.Null((await f.Db.Set<LodgingAssignment>().AsNoTracking().SingleAsync(x => x.PersonId == child.Id)).RoomId);
    }

    [Fact]
    public async Task IndividualPrintDoesNotAggregatePeopleWithoutHouseholdAndStaleEditsReject()
    {
        await using var f = new Fixture(); var room = await f.Room(2);
        var second = new Person("Altro senza nucleo privato"); f.Db.AddRange(second, new EventParticipant(f.EventId, second.Id)); await f.Db.SaveChangesAsync();
        var old = (await f.Page()).Version; await f.Assign(f.PersonId, room);
        await Assert.ThrowsAsync<LocalConflictException>(() => f.Service.AssignPersonAsync(f.ActorId, f.EventId, old, second.Id, room));
        using var client = f.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        var html = await client.GetStringAsync($"/Lodging/Print?eventId={f.EventId}&personId={f.PersonId}");
        Assert.Contains("Partecipante test HTTP", html); Assert.DoesNotContain(second.DisplayName, html);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Lodging/Print?eventId={f.EventId}")).StatusCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public LocalWebFactory Factory { get; }
        private readonly IServiceScope scope;
        public TRobotDbContext Db { get; }
        public LodgingService Service { get; }
        public Guid EventId => Factory.EventId;
        public Guid ActorId => Factory.OrganizerPersonId;
        public Guid PersonId { get; }
        public Fixture(SaveChangesInterceptor? interceptor = null)
        {
            Factory = new LocalWebFactory { SaveInterceptor = interceptor };
            scope = Factory.Services.CreateScope(); Db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            Service = scope.ServiceProvider.GetRequiredService<LodgingService>(); PersonId = Db.EventParticipants.Single().PersonId;
        }
        public Task<LodgingPage> Page() => Service.ReadAsync(ActorId, EventId);
        public async Task<Guid> Room(int capacity)
        {
            var apartment = Guid.NewGuid(); await Service.SaveApartmentAsync(ActorId, EventId, (await Page()).Version, apartment, "Appartamento", null);
            var room = Guid.NewGuid(); await Service.SaveRoomAsync(ActorId, EventId, (await Page()).Version, room, apartment, "Camera", capacity, null); return room;
        }
        public async Task Assign(Guid person, Guid? room) => await Service.AssignPersonAsync(ActorId, EventId, (await Page()).Version, person, room);
        public async ValueTask DisposeAsync() { scope.Dispose(); await Factory.DisposeAsync(); }
    }
    private sealed class FailAudit : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<LocalAudit>().Any(x => x.State == EntityState.Added)) throw new InvalidOperationException("Audit fallito.");
            return ValueTask.FromResult(result);
        }
    }
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static void AssertDenied(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Access/Denied", response.Headers.Location!.ToString());
    }
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] pairs) => new(pairs.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
}
