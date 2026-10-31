using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Households;
using TRobot.Application.Local;
using TRobot.Application.Lodging;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class HouseholdRenameTests
{
    [Fact]
    public void DomainNormalizesNamesAndPreservesOriginalCreationFingerprint()
    {
        var h = new Household(Guid.NewGuid(), " Originale ", Guid.NewGuid());
        var fingerprint = h.CreationNameFingerprint;
        Assert.False(h.Rename(" Originale ")); Assert.Equal(0, h.Revision);
        Assert.Throws<ArgumentException>(() => h.Rename(" "));
        Assert.Throws<ArgumentOutOfRangeException>(() => h.Rename(new string('x', 151)));
        Assert.True(h.Rename(" <b>Corretto</b> "));
        Assert.Equal("<b>Corretto</b>", h.Name); Assert.Equal(1, h.Revision);
        Assert.Equal(fingerprint, h.CreationNameFingerprint);
        Assert.True(h.MatchesCreationName(" Originale ")); Assert.False(h.MatchesCreationName(h.Name));
        Assert.True(h.Rename(new string('x', 150)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplaySurvivesRepeatedRenameAndLegacyOriginalNameIsCaptured(bool legacy)
    {
        await using var f = new Fixture(); var id = await f.Create();
        if (legacy) await f.Legacy(id);
        Assert.Equal(id, await f.Service.CreateAsync(f.Actor, f.Event, " Originale ", id));
        Assert.False(await f.Rename(id, 0, " Originale ", false));
        var h = await f.Db.Set<Household>().SingleAsync();
        if (legacy) Assert.Null(h.CreationNameFingerprint);
        Assert.True(await f.Rename(id, 0, "Nuovo"));
        h = await f.Db.Set<Household>().SingleAsync(); Assert.NotNull(h.CreationNameFingerprint);
        Assert.Equal(id, await f.Service.CreateAsync(f.Actor, f.Event, "Originale", id));
        await Assert.ThrowsAsync<LocalConflictException>(() => f.Service.CreateAsync(f.Actor, f.Event, "Nuovo", id));
        await Assert.ThrowsAsync<LocalConflictException>(() => f.Service.CreateAsync(f.Actor, f.Event, "Diverso", id));
        Assert.True(await f.Rename(id, 1, "Terzo"));
        Assert.Equal(id, await f.Service.CreateAsync(f.Actor, f.Event, "Originale", id));
        Assert.Equal("Terzo", (await f.Db.Set<Household>().SingleAsync()).Name);
        Assert.Equal(3, await f.Db.Set<LocalAudit>().CountAsync());
    }

    [Fact]
    public async Task ValidationConfirmationAndRevisionsRejectWithoutMutationOrAudit()
    {
        await using var f = new Fixture(); var id = await f.Create();
        foreach (var name in new[] { "", " ", new string('x', 151) })
            await Assert.ThrowsAsync<LocalValidationException>(() => f.Rename(id, 0, name));
        await Assert.ThrowsAsync<LocalValidationException>(() => f.Rename(id, null, "Nuovo"));
        await Assert.ThrowsAsync<LocalValidationException>(() => f.Rename(id, 0, "Nuovo", false));
        await Assert.ThrowsAsync<LocalConflictException>(() => f.Rename(id, -1, "Nuovo"));
        Assert.False(await f.Rename(id, 0, " Originale ", false));
        Assert.Single(await f.Db.Set<LocalAudit>().ToArrayAsync());
        Assert.True(await f.Rename(id, 0, "Nuovo"));
        await Assert.ThrowsAsync<LocalConflictException>(() => f.Rename(id, 0, "Nuovo"));
        Assert.Equal(1, (await f.Db.Set<Household>().SingleAsync()).Revision);
        Assert.Equal(2, await f.Db.Set<LocalAudit>().CountAsync());
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("disabled")]
    [InlineData("revoked")]
    [InlineData("event")]
    [InlineData("target")]
    public async Task PermissionsAndEventScopeAreCheckedForEveryRename(string scenario)
    {
        await using var f = new Fixture(); var id = await f.Create();
        var actor = f.Actor; var ev = f.Event;
        if (scenario == "actor") actor = Guid.NewGuid();
        if (scenario == "event") ev = Guid.NewGuid();
        if (scenario == "target") id = Guid.NewGuid();
        if (scenario == "disabled") { (await f.Db.Users.SingleAsync()).IsEnabled = false; await f.Db.SaveChangesAsync(); }
        if (scenario == "revoked") { (await f.Db.EventOrganizers.SingleAsync()).SetActive(false); await f.Db.SaveChangesAsync(); }
        await Assert.ThrowsAsync<LocalAccessException>(() => f.Service.RenameAsync(actor, ev, id, 0, "Negato", true));
        Assert.Equal("Originale", (await f.Db.Set<Household>().SingleAsync()).Name);
        Assert.Single(await f.Db.Set<LocalAudit>().ToArrayAsync());
    }

    [Fact]
    public async Task AuditFailureRollsBackNameRevisionAndLegacyFingerprintThenAllowsRetry()
    {
        var fail = new FailAudit(); await using var f = new Fixture(fail); var id = await f.Create(); await f.Legacy(id);
        fail.Armed = true;
        await Assert.ThrowsAsync<LocalValidationException>(() => f.Rename(id, 0, "Fallito"));
        fail.Armed = false;
        var h = await f.Db.Set<Household>().SingleAsync();
        Assert.Equal("Originale", h.Name); Assert.Equal(0, h.Revision); Assert.Null(h.CreationNameFingerprint);
        Assert.Single(await f.Db.Set<LocalAudit>().ToArrayAsync());
        Assert.True(await f.Rename(id, 0, "Riuscito"));
    }

    [Fact]
    public async Task EfRejectsActualConcurrentHouseholdRename()
    {
        await using var f = new Fixture(); var id = await f.Create();
        var options = new DbContextOptionsBuilder<TRobotDbContext>().UseSqlite(f.Db.Database.GetDbConnection()).Options;
        await using var other = new TRobotDbContext(options);
        var stale = await other.Set<Household>().SingleAsync(x => x.Id == id);
        Assert.True(await f.Rename(id, 0, "Prima"));
        stale.Rename("Concorrente");
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync());
        Assert.Equal("Prima", (await f.Db.Set<Household>().SingleAsync()).Name);
    }

    [Fact]
    public async Task LodgingFingerprintMatchesM31AndRealRenameOnlyInvalidatesFinalizationPreservingRelations()
    {
        await using var f = new Fixture(); var id = await f.Create();
        var person = (await f.Db.EventParticipants.SingleAsync()).PersonId;
        await f.Service.AddAsync(f.Actor, f.Event, id, new(person, HouseholdMemberKind.Adult, null, 0, 0, false, Guid.NewGuid()));
        var lodging = f.Scope.ServiceProvider.GetRequiredService<LodgingService>();
        Task<LodgingPage> Page() => lodging.ReadAsync(f.Actor, f.Event);
        var apartment = Guid.NewGuid();
        await lodging.SaveApartmentAsync(f.Actor, f.Event, (await Page()).Version, apartment, "Casa", null);
        var room = Guid.NewGuid();
        await lodging.SaveRoomAsync(f.Actor, f.Event, (await Page()).Version, room, apartment, "Camera", 2, null);
        await lodging.AssignHouseholdAsync(f.Actor, f.Event, (await Page()).Version, id, room);
        await lodging.FinalizeAsync(f.Actor, f.Event, (await Page()).Version);
        var before = await Page();
        var ev = await f.Db.Events.SingleAsync(x => x.Id == f.Event);
        var store = f.Scope.ServiceProvider.GetRequiredService<ILocalStore>();
        // M31 serialized all four original Household properties in this exact order.
        var legacyFingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            EventName = ev.Name,
            apartments = before.Apartments.OrderBy(x => x.Name).ThenBy(x => x.Id),
            rooms = before.Rooms.OrderBy(x => x.Name).ThenBy(x => x.Id),
            assignments = before.Assignments.OrderBy(x => x.PersonId),
            people = (await store.ParticipationsAsync(f.Event, default)).OrderBy(x => x.PersonId),
            households = (await f.Db.Set<Household>().OrderBy(x => x.Id).ToArrayAsync()).Select(x => new { x.Id, x.EventId, x.Name, x.Revision }),
            members = (await f.Db.Set<HouseholdMember>().Where(x => x.IsActive).OrderBy(x => x.PersonId).ToArrayAsync())
        })));
        Assert.Equal(legacyFingerprint, before.Version.Fingerprint);
        var members = JsonSerializer.Serialize(await f.Db.Set<HouseholdMember>().ToArrayAsync());
        var participants = JsonSerializer.Serialize(await f.Db.EventParticipants.ToArrayAsync());
        var assignments = JsonSerializer.Serialize(await f.Db.Set<LodgingAssignment>().ToArrayAsync());
        await f.Legacy(id);
        Assert.Equal(before.Version, (await Page()).Version); Assert.True((await Page()).IsFinal);
        Assert.False(await f.Rename(id, 1, " Originale ", false));
        Assert.True((await Page()).IsFinal); Assert.Equal(before.Version, (await Page()).Version);
        Assert.True(await f.Rename(id, 1, "Corretto"));
        var after = await Page(); Assert.True(after.FinalizationOutdated); Assert.False(after.IsFinal);
        Assert.Equal(room, after.RoomFor(person)); Assert.NotEqual(before.Version.Fingerprint, after.Version.Fingerprint);
        Assert.Equal(members, JsonSerializer.Serialize(await f.Db.Set<HouseholdMember>().ToArrayAsync()));
        Assert.Equal(participants, JsonSerializer.Serialize(await f.Db.EventParticipants.ToArrayAsync()));
        Assert.Equal(assignments, JsonSerializer.Serialize(await f.Db.Set<LodgingAssignment>().ToArrayAsync()));
    }

    [Fact]
    public async Task HttpRequiresAntiforgeryRevisionConfirmationAndRetainsEncodedInputOnErrors()
    {
        await using var f = new Fixture(); var id = await f.Create();
        using var client = f.Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var path = $"/Households/Rename?eventId={f.Event}&householdId={id}";
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(path)).StatusCode);
        var login = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", login)));
        var token = Token(await client.GetStringAsync(path));
        (string, string)[] Data(string? revision, bool confirm, bool csrf = true) => new[]
        {
            ("EventId", f.Event.ToString()), ("HouseholdId", id.ToString()), ("Name", "<script>Corretto</script>"),
            ("Confirm", confirm.ToString()), ("Revision", revision ?? ""), ("__RequestVerificationToken", csrf ? token : "")
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Households/Rename", Form(Data("0", true, false)))).StatusCode);
        var missing = await client.PostAsync("/Households/Rename", Form(Data(null, true)));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode); Assert.Contains("&lt;script&gt;", await missing.Content.ReadAsStringAsync());
        var unconfirmed = await client.PostAsync("/Households/Rename", Form(Data("0", false)));
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode); Assert.Contains("Conferma esplicitamente", await unconfirmed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Households/Rename", Form(Data("0", true)))).StatusCode);
        var stale = await client.PostAsync("/Households/Rename", Form(Data("0", true)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); Assert.Contains("&lt;script&gt;", await stale.Content.ReadAsStringAsync());
        var index = await client.GetStringAsync($"/Households?eventId={f.Event}");
        Assert.Contains("&lt;script&gt;", index); Assert.DoesNotContain("<script>Corretto</script>", index);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public LocalWebFactory Factory { get; }
        public IServiceScope Scope { get; }
        public TRobotDbContext Db { get; }
        public HouseholdService Service { get; }
        public Guid Actor => Factory.OrganizerPersonId;
        public Guid Event => Factory.EventId;
        public Fixture(SaveChangesInterceptor? interceptor = null)
        {
            Factory = new LocalWebFactory { SaveInterceptor = interceptor }; Scope = Factory.Services.CreateScope();
            Db = Scope.ServiceProvider.GetRequiredService<TRobotDbContext>(); Service = Scope.ServiceProvider.GetRequiredService<HouseholdService>();
        }
        public Task<Guid> Create() => Service.CreateAsync(Actor, Event, "Originale", Guid.NewGuid());
        public Task<bool> Rename(Guid id, int? revision, string name, bool confirm = true) => Service.RenameAsync(Actor, Event, id, revision, name, confirm);
        public async Task Legacy(Guid id)
        {
            await Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Households SET CreationNameFingerprint = NULL WHERE Id = {id}");
            Db.ChangeTracker.Clear();
        }
        public async ValueTask DisposeAsync() { Scope.Dispose(); await Factory.DisposeAsync(); }
    }
    private sealed class FailAudit : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<LocalAudit>().Any(x => x.State == EntityState.Added)) throw new InvalidOperationException("Audit simulato fallito.");
            return ValueTask.FromResult(result);
        }
    }
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] pairs) => new(pairs.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
}

public sealed partial class WhatsAppIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HouseholdRenameInvalidatesPendingFamilyConfirmationOnlyWhenNameChanges(bool change)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service); await SetStayEvent(p, f);
        var db = p.GetRequiredService<TRobotDbContext>(); var child = new Person("Bambino sintetico");
        var family = new Household(f.EventId, "Originale", Guid.NewGuid());
        db.AddRange(child, family, new EventParticipant(f.EventId, child.Id),
            new HouseholdMember(f.EventId, family.Id, person, HouseholdMemberKind.Adult, null, Guid.NewGuid()),
            new HouseholdMember(f.EventId, family.Id, child.Id, HouseholdMemberKind.Child, null, Guid.NewGuid()));
        await db.SaveChangesAsync(); await AskStay(service);
        var participationsBefore = JsonSerializer.Serialize(await db.EventParticipants.OrderBy(x => x.PersonId).ToArrayAsync());
        var staysBefore = JsonSerializer.Serialize(await db.Set<AttendanceStay>().OrderBy(x => x.Id).ToArrayAsync());
        var historyBefore = JsonSerializer.Serialize(await db.Set<AttendanceStayChange>().OrderBy(x => x.Id).ToArrayAsync());
        Assert.Equal(change, await p.GetRequiredService<HouseholdService>().RenameAsync(f.OrganizerPersonId,
            f.EventId, family.Id, 0, change ? "Corretto" : " Originale ", change));
        var result = await service.IncomingAsync(Incoming("si", "after-household-rename"));
        if (change)
        {
            Assert.Contains("cambiati", result.ReplyText);
            Assert.Equal(participationsBefore, JsonSerializer.Serialize(await db.EventParticipants.OrderBy(x => x.PersonId).ToArrayAsync()));
            Assert.Equal(staysBefore, JsonSerializer.Serialize(await db.Set<AttendanceStay>().OrderBy(x => x.Id).ToArrayAsync()));
            Assert.Equal(historyBefore, JsonSerializer.Serialize(await db.Set<AttendanceStayChange>().OrderBy(x => x.Id).ToArrayAsync()));
        }
        else Assert.Equal(2, await db.Set<AttendanceStay>().CountAsync());
    }
}
