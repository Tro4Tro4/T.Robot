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
    private static async Task<string> Help(WhatsAppService service, string text, string id) =>
        (await service.IncomingAsync(Incoming(text, id))).ReplyText!;

    private static async Task ApproveKind(WhatsAppService service, LocalWebFactory f, WhatsAppListKind kind) =>
        await ApproveInformation(service, f, await service.PreviewListAsync(f.OrganizerPersonId, f.EventId, kind));

    [Fact]
    public async Task HelpIndexListsOnlyActiveTopicsAndListsAppearOnlyWithCurrentApproval()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service); var db = p.GetRequiredService<TRobotDbContext>();
        var index = await Help(service, "!aiuto", "h1");
        Assert.Contains("!aiuto presenza", index); Assert.Contains("!aiuto riepilogo", index); Assert.Contains("!aiuto pasti", index);
        Assert.DoesNotContain("elenchi", index); Assert.DoesNotContain("Termine adesioni", index);
        db.Add(new MenuItem(Guid.NewGuid(), f.EventId, "Voce sintetica", MenuCategory.Food, 2, "porzioni")); await db.SaveChangesAsync();
        Assert.DoesNotContain("elenchi", await Help(service, "!aiuto", "h2"));
        await ApproveKind(service, f, WhatsAppListKind.Menu);
        Assert.Contains("!aiuto elenchi", await Help(service, "!aiuto", "h3"));
        var menuOnly = await Help(service, "!aiuto elenchi", "h4");
        Assert.Contains("!menu [pagina]", menuOnly); Assert.Contains("!porto <rif>", menuOnly); Assert.Contains("!conferma", menuOnly);
        Assert.DoesNotContain("!todo", menuOnly); Assert.DoesNotContain("!prendo", menuOnly); Assert.DoesNotContain("!fatto", menuOnly);
        var menu = await db.Set<MenuItem>().SingleAsync(); menu.Update("Voce cambiata", menu.Category, 2, menu.Unit, null, menu.Availability, null); await db.SaveChangesAsync();
        Assert.DoesNotContain("elenchi", await Help(service, "!aiuto", "h5"));
        Assert.Contains("non trovata", await Help(service, "!aiuto elenchi", "h6"));
        db.Add(new OrganizationTask(f.EventId, "Attività sintetica", null, TaskPriority.Normal, person, null)); await db.SaveChangesAsync();
        await ApproveKind(service, f, WhatsAppListKind.Todo);
        var todoOnly = await Help(service, "!aiuto ELENCHI", "h7");
        Assert.Contains("!todo [pagina]", todoOnly); Assert.Contains("!prendo <rif>", todoOnly); Assert.Contains("!fatto <rif>", todoOnly); Assert.Contains("!conferma", todoOnly);
        Assert.DoesNotContain("!menu", todoOnly); Assert.DoesNotContain("!porto", todoOnly);
        Assert.Contains("!aiuto elenchi", await Help(service, "!aiuto", "h8"));
    }

    [Fact]
    public async Task HelpPresenceExplainsMaybeAndAbsentFamilyQuestionAliasesAndDeadline()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        await service.SetAttendanceDeadlineAsync(f.OrganizerPersonId, f.EventId, new DateTime(2030, 12, 29, 12, 0, 0), 0, true);
        var presence = await Help(service, "!aiuto presenza", "p1");
        Assert.Contains("cambiano subito la tua presenza", presence); Assert.Contains("!annulla = solo tu", presence);
        Assert.DoesNotContain("aggiornano soltanto", presence); Assert.Contains("Termine adesioni", presence);
        Assert.Contains("Termine adesioni", await Help(service, "!aiuto", "p2"));
        Assert.Equal(presence, await Help(service, "!aiuto PRESENZE", "p3"));
        Assert.Contains("!riepilogo gg/mm[/aaaa] [pagina]", await Help(service, "!aiuto Riepilogo", "p4"));
        Assert.Contains("!proponi", await Help(service, "!aiuto piatti", "p5"));
    }

    [Fact]
    public async Task HelpUnknownInactiveOrExtraTopicShowsNotFoundWithShortIndexWithoutListCommands()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        var i = 0;
        foreach (var text in new[] { "!aiuto boh", "!aiuto elenchi", "!aiuto menu", "!aiuto todo", "!aiuto presenza extra", "!aiuto pasti 2" })
        {
            var reply = await Help(service, text, "n" + i++);
            Assert.StartsWith("🤖 Voce non trovata o non attiva.", reply); Assert.Contains("!aiuto presenza", reply);
            Assert.DoesNotContain("!aiuto elenchi", reply); Assert.DoesNotContain("!porto", reply); Assert.DoesNotContain("!prendo", reply);
        }
    }

    [Fact]
    public async Task HelpMealsPresentsPreferencesAsUsableOnlyWhileVotingIsOpen()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); var db = p.GetRequiredService<TRobotDbContext>();
        var before = await Help(service, "!aiuto pasti", "m1");
        Assert.Contains("!proponi", before); Assert.Contains("sarà disponibile quando gli organizzatori apriranno il voto", before); Assert.DoesNotContain("!conferma", before);
        var cycle = new MealVotingCycle(f.EventId, Guid.NewGuid(), Now); db.Add(cycle); cycle.CloseCollection(); await db.SaveChangesAsync();
        Assert.Contains("sarà disponibile", await Help(service, "!aiuto pasti", "m2"));
        (await db.Set<MealVotingCycle>().SingleAsync()).Open(Now); await db.SaveChangesAsync();
        var open = await Help(service, "!aiuto pasti", "m3");
        Assert.DoesNotContain("sarà disponibile", open); Assert.Contains("!preferenze\nTi scrivo in privato", open); Assert.Contains("cinque minuti", open);
    }

    [Fact]
    public async Task HelpRepliesStayUnderLimitWithEverythingActiveAndAreIdempotent()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); var person = await Activate(p, f, service); var db = p.GetRequiredService<TRobotDbContext>();
        db.Add(new MenuItem(Guid.NewGuid(), f.EventId, "Voce sintetica", MenuCategory.Food, 2, "porzioni"));
        db.Add(new OrganizationTask(f.EventId, "Attività sintetica", null, TaskPriority.Normal, person, null));
        var cycle = new MealVotingCycle(f.EventId, Guid.NewGuid(), Now); db.Add(cycle); cycle.CloseCollection(); cycle.Open(Now); await db.SaveChangesAsync();
        await ApproveKind(service, f, WhatsAppListKind.Menu); await ApproveKind(service, f, WhatsAppListKind.Todo);
        await service.SetAttendanceDeadlineAsync(f.OrganizerPersonId, f.EventId, new DateTime(2030, 12, 29, 12, 0, 0), 0, true);
        var i = 0; var max = 0;
        foreach (var text in new[] { "!aiuto", "!aiuto presenza", "!aiuto riepilogo", "!aiuto pasti", "!aiuto elenchi", "!aiuto boh" })
        {
            var reply = await Help(service, text, "l" + i++); max = Math.Max(max, reply.Length);
            Assert.InRange(reply.Length, 1, 1999); Assert.DoesNotContain('\u2014', reply);
        }
        Assert.True(max > 0);
        var first = await service.IncomingAsync(Incoming("!aiuto presenza", "idem"));
        Assert.Equal(new WhatsAppIncomingResult(true, true), await service.IncomingAsync(Incoming("!aiuto presenza", "idem")));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(Incoming("!aiuto pasti", "idem")));
        Assert.NotNull(first.ReplyOperationId);
    }
}
