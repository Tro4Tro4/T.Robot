using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Application.Local;
using TRobot.Application.Menu;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed class MenuTests
{
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1000000.001")]
    [InlineData("1.0001")]
    public void InvalidQuantitiesAreRejected(string value) => Assert.Throws<ArgumentException>(() =>
        new MenuItem(Guid.NewGuid(), Guid.NewGuid(), "Acqua", MenuCategory.Drink,
            decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), "litri"));

    [Fact]
    public void DomainNormalizesPreservesStateOnErrorAndIncrementsRevisionOnce()
    {
        var item = new MenuItem(Guid.NewGuid(), Guid.NewGuid(), " Acqua ", MenuCategory.Drink, 12.125m, " litri ", notes: " note\r\nvarie ");
        Assert.Equal("Acqua", item.Name); Assert.Equal("note\nvarie", item.Notes);
        Assert.Throws<ArgumentException>(() => item.Update("Nuovo", MenuCategory.Food, 1, "porzioni", null, MenuAvailability.Assigned, null));
        Assert.Equal("Acqua", item.Name); Assert.Equal(0, item.Revision);
        Assert.True(item.Update("Acqua", MenuCategory.Drink, 15, "litri", null, MenuAvailability.Ready, null));
        Assert.Equal(1, item.Revision);
        Assert.False(item.Update("Acqua", MenuCategory.Drink, 15, "litri", null, MenuAvailability.Ready, null));
        Assert.Equal(1, item.Revision);
        Assert.Throws<ArgumentException>(() => new MenuItem(Guid.NewGuid(), Guid.NewGuid(), "Acqua", (MenuCategory)99, 1, "litri"));
        Assert.Throws<ArgumentException>(() => new MenuItem(Guid.NewGuid(), Guid.NewGuid(), "Acqua", MenuCategory.Drink, 1, "litri", availability: (MenuAvailability)99));
        Assert.Throws<ArgumentException>(() => new MenuItem(Guid.NewGuid(), Guid.NewGuid(), new string('a', 151), MenuCategory.Drink, 1, "litri"));
        Assert.Throws<ArgumentException>(() => new MenuItem(Guid.NewGuid(), Guid.NewGuid(), "Acqua", MenuCategory.Drink, 1, new string('a', 31)));
        Assert.Throws<ArgumentException>(() => new MenuItem(Guid.NewGuid(), Guid.NewGuid(), "Acqua", MenuCategory.Drink, 1, "litri", notes: new string('a', 2001)));
    }

    [Fact]
    public async Task ServiceRejectsForeignReferencesStaleEditsAndUnconfirmedCancellationAndKeepsAttendance()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<MenuService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var person = await db.EventParticipants.SingleAsync();
        var input = Input() with { ResponsiblePersonId = person.PersonId, Availability = MenuAvailability.Assigned };
        await Assert.ThrowsAsync<LocalAccessException>(() => service.SaveAsync(factory.OrganizerPersonId, Guid.NewGuid(), input, true));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.SaveAsync(factory.OrganizerPersonId, factory.EventId, input with { ResponsiblePersonId = Guid.NewGuid() }, true));
        await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, input, true);
        await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, input, true);
        Assert.Equal(1, await db.Set<LocalAudit>().CountAsync(x => x.Module == "Menu"));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.SaveAsync(factory.OrganizerPersonId, factory.EventId, input with { Name = "Diverso" }, true));
        var edit = input with { Name = "Acqua naturale", Revision = 0 };
        await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, edit, false);
        await Assert.ThrowsAsync<LocalConflictException>(() => service.SaveAsync(factory.OrganizerPersonId, factory.EventId, edit, false));
        await Assert.ThrowsAsync<LocalValidationException>(() => service.SaveAsync(factory.OrganizerPersonId, factory.EventId,
            edit with { Revision = 1, Availability = MenuAvailability.Cancelled }, false));
        await service.SaveAsync(factory.OrganizerPersonId, factory.EventId,
            edit with { Revision = 1, Availability = MenuAvailability.Cancelled, ConfirmCancellation = true }, false);
        var after = await db.Set<MenuItem>().AsNoTracking().SingleAsync();
        Assert.Equal(MenuAvailability.Cancelled, after.Availability); Assert.Equal(2, after.Revision);
        Assert.Equal(ParticipationStatus.NoResponse, (await db.EventParticipants.AsNoTracking().SingleAsync()).Status);
        Assert.Empty(await db.ParticipationReceipts.ToListAsync());
        Assert.Equal(3, await db.Set<LocalAudit>().CountAsync(x => x.Module == "Menu"));
    }

    [Fact]
    public async Task CancellationRequiresConfirmationOnCreationAndReplaysWithoutAnotherAudit()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<MenuService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var input = Input() with { Availability = MenuAvailability.Cancelled };
        await Assert.ThrowsAsync<LocalValidationException>(() => service.SaveAsync(factory.OrganizerPersonId, factory.EventId, input, true));
        Assert.Empty(await db.Set<MenuItem>().ToListAsync());
        Assert.Empty(await db.Set<LocalAudit>().Where(x => x.Module == "Menu").ToListAsync());
        await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, input with { ConfirmCancellation = true }, true);
        await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, input, true);
        Assert.Equal(MenuAvailability.Cancelled, (await db.Set<MenuItem>().SingleAsync()).Availability);
        Assert.Equal(1, await db.Set<LocalAudit>().CountAsync(x => x.Module == "Menu"));
    }

    [Fact]
    public async Task ExistingItemCannotBeUpdatedOrRecreatedThroughAnotherManagedEvent()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<MenuService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var other = new Event("Altro evento gestibile", DateTime.UtcNow);
        db.AddRange(other, new EventOrganizer(other.Id, factory.OrganizerPersonId));
        await db.SaveChangesAsync();
        var input = Input();
        await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, input, true);
        await Assert.ThrowsAsync<LocalValidationException>(() => service.SaveAsync(factory.OrganizerPersonId, other.Id,
            input with { Name = "Modifica estranea", Revision = 0 }, false));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.SaveAsync(factory.OrganizerPersonId, other.Id, input, true));
        var saved = await db.Set<MenuItem>().AsNoTracking().SingleAsync();
        Assert.Equal(factory.EventId, saved.EventId); Assert.Equal(input.Name, saved.Name); Assert.Equal(0, saved.Revision);
        Assert.Equal(1, await db.Set<LocalAudit>().CountAsync(x => x.Module == "Menu"));
    }

    [Fact]
    public async Task RevokedAndDisabledActorsCannotReadOrWrite()
    {
        await using var factory = new LocalWebFactory();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<MenuService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var assignment = await db.EventOrganizers.SingleAsync(); assignment.SetActive(false); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.ReadAsync(factory.OrganizerPersonId, factory.EventId));
        await Assert.ThrowsAsync<LocalAccessException>(() => service.SaveAsync(factory.OrganizerPersonId, factory.EventId, Input(), true));
        assignment = await db.EventOrganizers.SingleAsync(); assignment.SetActive(true);
        var user = await db.Users.SingleAsync(); user.IsEnabled = false; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<LocalAccessException>(() => service.SaveAsync(factory.OrganizerPersonId, factory.EventId, Input(), true));
        Assert.Empty(await db.Set<MenuItem>().ToListAsync());
    }

    [Fact]
    public async Task FailureAfterDatabaseSaveRollsBackBothItemAndAudit()
    {
        var interceptor = new FailAfterSave();
        await using var factory = new LocalWebFactory { SaveInterceptor = interceptor };
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<MenuService>();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        interceptor.Armed = true;
        await Assert.ThrowsAsync<ApplicationException>(() => service.SaveAsync(factory.OrganizerPersonId, factory.EventId, Input(), true));
        interceptor.Armed = false;
        Assert.Empty(await db.Set<MenuItem>().AsNoTracking().ToListAsync());
        Assert.Empty(await db.Set<LocalAudit>().Where(x => x.Module == "Menu").ToListAsync());
        await service.SaveAsync(factory.OrganizerPersonId, factory.EventId, Input(), true);
        Assert.Equal(1, await db.Set<MenuItem>().CountAsync());
    }

    [Theory]
    [InlineData("1,5")]
    [InlineData("1.5")]
    public async Task HttpRequiresLoginCsrfAndRevisionAndEncodesText(string quantity)
    {
        await using var factory = new LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var url = "/Menu?eventId=" + factory.EventId;
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(url)).StatusCode);
        var token = Token(await client.GetStringAsync("/Access/Login"));
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)))).StatusCode);
        var html = await client.GetStringAsync(url); token = Token(html);
        var id = Guid.NewGuid();
        var form = new[] { ("Input.Id", id.ToString()), ("Input.Name", "<script>test</script>"), ("Input.Category", "1"), ("Input.Quantity", quantity), ("Input.Unit", "litri"), ("Input.Availability", "0") };
        var create = "/Menu/Create?eventId=" + factory.EventId;
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(create, Form(form))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(create, Form(form.Append(("__RequestVerificationToken", token)).ToArray()))).StatusCode);
        var updated = await client.GetStringAsync(url);
        Assert.Contains("&lt;script&gt;test&lt;/script&gt;", updated); Assert.DoesNotContain("<script>test</script>", updated);
        var foreign = await client.PostAsync("/Menu/Create?eventId=" + Guid.NewGuid(), Form(form.Append(("__RequestVerificationToken", token)).ToArray()));
        Assert.Equal(HttpStatusCode.Redirect, foreign.StatusCode); Assert.Contains("/Access/Denied", foreign.Headers.Location!.ToString());
        var missingRevision = await client.PostAsync("/Menu/Update?eventId=" + factory.EventId, Form(form.Append(("__RequestVerificationToken", token)).ToArray()));
        Assert.Equal(HttpStatusCode.OK, missingRevision.StatusCode); Assert.Contains("Revisione obbligatoria", await missingRevision.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1.5m, (await scope.ServiceProvider.GetRequiredService<TRobotDbContext>().Set<MenuItem>().SingleAsync()).Quantity);
    }

    [Fact]
    public async Task HttpNotesValidateNormalizedLengthAndKeepRevisionAfterInvalidUpdate()
    {
        await using var factory = new LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var token = Token(await client.GetStringAsync("/Access/Login"));
        await client.PostAsync("/Access/Login", Form(("UserName", LocalWebFactory.UserName), ("Password", LocalWebFactory.Password), ("__RequestVerificationToken", token)));
        var url = "/Menu?eventId=" + factory.EventId;
        token = Token(await client.GetStringAsync(url));
        var id = Guid.NewGuid();
        var browserNotes = new string('N', 1998) + "\r\nZ";
        var form = new[] { ("Input.Id", id.ToString()), ("Input.Name", "Acqua"), ("Input.Category", "1"),
            ("Input.Quantity", "1"), ("Input.Unit", "litri"), ("Input.Availability", "0"), ("__RequestVerificationToken", token) };
        var create = await client.PostAsync("/Menu/Create?eventId=" + factory.EventId,
            Form(form.Append(("Input.Notes", browserNotes)).ToArray()));
        Assert.Equal(HttpStatusCode.Redirect, create.StatusCode);
        var editForm = form.Append(("Input.Revision", "0")).ToArray();
        var invalid = await client.PostAsync("/Menu/Update?eventId=" + factory.EventId,
            Form(editForm.Append(("Input.Notes", browserNotes + "X")).ToArray()));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Le note possono contenere al massimo 2000 caratteri", await invalid.Content.ReadAsStringAsync());
        var equivalent = await client.PostAsync("/Menu/Update?eventId=" + factory.EventId,
            Form(editForm.Append(("Input.Notes", browserNotes)).ToArray()));
        Assert.Equal(HttpStatusCode.Redirect, equivalent.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
        var saved = await db.Set<MenuItem>().SingleAsync();
        Assert.Equal(2000, saved.Notes!.Length); Assert.EndsWith("\nZ", saved.Notes);
        Assert.Equal(0, saved.Revision);
        Assert.Equal(1, await db.Set<LocalAudit>().CountAsync(x => x.Module == "Menu"));
    }

    private static MenuInput Input() => new(Guid.NewGuid(), "Acqua", MenuCategory.Drink, 12, "litri", null, MenuAvailability.Planned, null, null, false);
    private static FormUrlEncodedContent Form(params (string Key, string Value)[] values) => new(values.Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { if (Armed) throw new ApplicationException("Errore audit sintetico dopo il salvataggio."); return ValueTask.FromResult(result); }
    }
}
