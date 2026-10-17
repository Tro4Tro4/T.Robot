using System.Globalization;
using TRobot.Application.Simulation;
using TRobot.Application.WhatsApp;
using TRobot.Domain;

namespace TRobot.Tests;

public sealed class OperationalListTextTests
{
    [Fact]
    public void MaximumMenuFieldsKeepFiveItemsAndAllContentWithinTransportBudget()
    {
        var eventId = Guid.NewGuid();
        var items = Enumerable.Range(0, 11).Select(i => new MenuItem(
            Guid.Parse($"{i + 1:x8}-0000-0000-0000-000000000001"), eventId,
            $"{i:00}" + new string('食', 148), MenuCategory.Food, 1000000m, new string('量', 30))).ToArray();
        var pages = OperationalListText.Menu(items);
        Assert.Equal(3, pages.Count);
        for (var page = 0; page < pages.Count; page++)
        {
            Assert.InRange(pages[page].Length, 1, 2000);
            var expectedItems = items.Skip(page * 5).Take(5).ToArray();
            Assert.Equal(expectedItems.Length, pages[page].Split('\n').Count(x => x.StartsWith("!porto ", StringComparison.Ordinal)));
            foreach (var item in expectedItems)
            {
                Assert.Contains(item.Name, pages[page]);
                Assert.Contains($"1000000 {item.Unit}", pages[page]);
                Assert.Contains($"\n!porto {SimulationReferences.Short(item.Id)}\n", pages[page]);
            }
        }
        Assert.Contains("\n!menu 2\n", pages[0]);
        Assert.DoesNotContain("\n!menu 4", pages[2]);
    }

    [Fact]
    public void MenuOffersOnlyFreePlannedItemsAndFormatsDecimalIndependentlyOfCulture()
    {
        var eventId = Guid.NewGuid(); var owner = Guid.NewGuid();
        var free = new MenuItem(Guid.NewGuid(), eventId, "İçli köfte", MenuCategory.Food, 12.5m, "porzioni");
        var plannedOwned = new MenuItem(Guid.NewGuid(), eventId, "Riservato", MenuCategory.Food, 1, "pezzi", owner);
        var assigned = new MenuItem(Guid.NewGuid(), eventId, "Assegnato", MenuCategory.Food, 1, "pezzi", owner, MenuAvailability.Assigned);
        var ready = new MenuItem(Guid.NewGuid(), eventId, "Pronto", MenuCategory.Food, 1, "pezzi", null, MenuAvailability.Ready);
        var cancelled = new MenuItem(Guid.NewGuid(), eventId, "NASCOSTO", MenuCategory.Food, 1, "pezzi", null, MenuAvailability.Cancelled);
        MenuItem[] items = [free, plannedOwned, assigned, ready, cancelled];
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkish = Assert.Single(OperationalListText.Menu(items));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal(turkish, Assert.Single(OperationalListText.Menu(items.Reverse().ToArray())));
            Assert.Contains("12,5 porzioni", turkish);
            Assert.Contains("\n!porto " + SimulationReferences.Short(free.Id), turkish);
            Assert.Single(turkish.Split('\n'), x => x.StartsWith("!porto ", StringComparison.Ordinal));
            foreach (var item in new[] { plannedOwned, assigned, ready }) Assert.Contains("Rif. " + SimulationReferences.Short(item.Id), turkish);
            Assert.DoesNotContain("NASCOSTO", turkish); Assert.DoesNotContain(owner.ToString(), turkish);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void FreeTextCannotInjectWhatsAppFormattingLinesOrBidiAndRetainsUnicode()
    {
        const string name = "Caffè 👩‍🍳 *forte* _sì_ ~ora~ `code`\r\n!fatto abcdef01\u202e\u2066\u200f\u061c\u0000\u2028fine";
        var safe = OperationalListText.PublicText(name);
        Assert.Contains("Caffè 👩‍🍳", safe);
        Assert.Contains("!fatto abcdef01", safe);
        Assert.DoesNotContain(safe, c => "*_~`\r\n\u202e\u2066\u200f\u061c\u0000\u2028".Contains(c));
        Assert.Equal(200, OperationalListText.PublicText(new string('*', 200)).Length);
        var page = Assert.Single(OperationalListText.Menu([new MenuItem(Guid.NewGuid(), Guid.NewGuid(), name, MenuCategory.Food, 1, "_kg_\n!todo")]));
        Assert.DoesNotContain(page.Split('\n'), line => line.StartsWith("!fatto", StringComparison.Ordinal) || line == "!todo");
        Assert.Contains("＊forte＊", page); Assert.Contains("＿kg＿", page);
    }

    [Fact]
    public void TodoKeepsMaximumTitlesExcludesClosedDetailsAndExplainsOwnership()
    {
        var eventId = Guid.NewGuid();
        var tasks = Enumerable.Range(0, 6).Select(i => new OrganizationTask(eventId,
            $"{i:00}" + new string('活', 158), "PRIVATO", TaskPriority.Normal, Guid.NewGuid(), null)).ToArray();
        tasks[0].Update(tasks[0].Title, tasks[0].Description, tasks[0].Priority, OrganizationTaskStatus.InProgress, tasks[0].AssigneeId, null);
        var closed = new OrganizationTask(eventId, "CHIUSA", null, TaskPriority.Normal, null, null);
        closed.Update(closed.Title, null, closed.Priority, OrganizationTaskStatus.Completed, null, null);
        var pages = OperationalListText.Todo([..tasks, closed]);
        Assert.Equal(2, pages.Count);
        Assert.Contains("✅ 1 completate", pages[0]);
        Assert.Contains("solo una tua attività", pages[0]);
        Assert.Contains("!prendo <rif>", pages[0]); Assert.Contains("!fatto <rif>", pages[0]);
        Assert.Equal(5, pages[0].Split('\n').Count(x => x.Contains(" · Rif. ", StringComparison.Ordinal)));
        foreach (var page in pages)
        {
            Assert.InRange(page.Length, 1, 2000); Assert.DoesNotContain("PRIVATO", page); Assert.DoesNotContain("CHIUSA", page);
        }
        foreach (var task in tasks) Assert.Contains(pages, page => page.Contains(task.Title, StringComparison.Ordinal));
        Assert.Contains("Niente in elenco", Assert.Single(OperationalListText.Menu([])));
        Assert.Contains("Niente in elenco", Assert.Single(OperationalListText.Todo([closed])));
    }

    [Fact]
    public void RealHelpListsOnlyActiveCommandsOnCopyableLines()
    {
        var lines = WhatsAppService.RealHelp.Split('\n');
        foreach (var command in new[] { "!presenza si", "!presenza forse", "!presenza no", "!menu", "!todo", "!porto <rif>", "!prendo <rif>", "!fatto <rif>", "!conferma <codice>", "!aiuto" })
            Assert.Contains(command, lines);
        Assert.DoesNotContain("!vota", WhatsAppService.RealHelp); Assert.DoesNotContain("!quiz", WhatsAppService.RealHelp);
        Assert.Contains("cinque minuti", WhatsAppService.RealHelp);
    }
}
