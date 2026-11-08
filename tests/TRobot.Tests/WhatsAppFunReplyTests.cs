using System.Globalization;
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
    private const string SummarySyntax = "ℹ️ Sintassi:\n!riepilogo gg/mm[/aaaa] [pagina]\nIndica una data valida e una pagina intera da 1 a 100000.";

    private static int ExpectedFunIndex(string messageId)
    {
        var options = Options();
        var hash = options.Fingerprint("message", Group + ":" + messageId);
        return (int)(uint.Parse(options.Fingerprint("fun-reply", hash)[..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture) % 10);
    }

    private static string ExpectedPresenceMisuse(string messageId) =>
        WhatsAppService.PresenceMisuseTexts[ExpectedFunIndex(messageId)] + "\n" + WhatsAppService.PresenceUsage;

    private static readonly IReadOnlyList<string>[] AllMisuseGroups =
        [WhatsAppService.PresenceMisuseTexts, WhatsAppService.SummaryMisuseTexts, WhatsAppService.MenuMisuseTexts, WhatsAppService.TodoMisuseTexts];

    private static void AssertPhraseOnlyFrom(IReadOnlyList<string> group, string reply)
    {
        var phrase = reply.Split('\n')[0];
        Assert.Contains(phrase, group);
        foreach (var other in AllMisuseGroups.Where(x => !ReferenceEquals(x, group))) Assert.DoesNotContain(phrase, other);
    }

    private static WhatsAppIncoming At(WhatsAppIncoming input, Clock clock) =>
        input with { TimestampUnixSeconds = new DateTimeOffset(clock.Utc).ToUnixTimeSeconds() };

    [Fact]
    public void FunReplyTextsAreFortyDistinctShortAndWithoutLongDash()
    {
        var longDash = ((char)0x2014).ToString();
        Assert.All(AllMisuseGroups, group => Assert.Equal(10, group.Count));
        Assert.Equal(40, AllMisuseGroups.SelectMany(x => x).Distinct().Count());
        Assert.Same(WhatsAppService.PresenceMisuseTexts, WhatsAppService.MisuseTexts("!PRESENZA"));
        Assert.Same(WhatsAppService.SummaryMisuseTexts, WhatsAppService.MisuseTexts("!riepilogo"));
        Assert.Same(WhatsAppService.MenuMisuseTexts, WhatsAppService.MisuseTexts("!menu"));
        Assert.Same(WhatsAppService.TodoMisuseTexts, WhatsAppService.MisuseTexts("!todo"));
        Assert.Null(WhatsAppService.MisuseTexts("!aiuto"));
        // Longest fixed part: the date syntax of !riepilogo or the !presenza usage lines.
        var longestFixed = Math.Max(SummarySyntax.Length + 2, WhatsAppService.PresenceUsage.Length + 1);
        Assert.All(AllMisuseGroups.SelectMany(x => x), text =>
        {
            Assert.True(text.Length + longestFixed < 2000);
            Assert.DoesNotContain(longDash, text);
            Assert.DoesNotContain("\n", text);
        });
        Assert.DoesNotContain(longDash, WhatsAppService.PresenceUsage);
    }

    [Theory]
    [InlineData("!presenza bho")]
    [InlineData("!presenza")]
    [InlineData("!PRESENZA si 3")]
    [InlineData("!presenza modifica adesso")]
    public async Task MalformedPresenceGetsFunPhraseCorrectUsageAndHelpReference(string text)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var gateway = new FakeGateway(); var service = Service(p, gateway, new Clock()); var person = await Activate(p, f, service);
        var reply = await service.IncomingAsync(Incoming(text, "misuse"));
        Assert.True(reply.Handled); Assert.False(reply.Duplicate); Assert.Null(reply.Reaction); Assert.NotNull(reply.ReplyOperationId);
        Assert.Equal(ExpectedPresenceMisuse("misuse"), reply.ReplyText);
        AssertPhraseOnlyFrom(WhatsAppService.PresenceMisuseTexts, reply.ReplyText!);
        Assert.EndsWith("Uso corretto: !presenza si | forse | no | modifica\nDettagli: !aiuto presenza", reply.ReplyText);
        var db = p.GetRequiredService<TRobotDbContext>();
        var receipt = await db.Set<WhatsAppCommandReceipt>().SingleAsync();
        Assert.Equal(reply.ReplyOperationId, receipt.Id); Assert.Equal(person, receipt.PersonId);
        Assert.Equal(1, await db.Set<LocalAudit>().CountAsync(x => x.Action == "InformationReceived" && x.EntityId == receipt.Id));
        Assert.Equal(1, await db.Set<LocalAudit>().CountAsync(x => x.Action == WhatsAppService.FunReplyAction && x.EntityId == receipt.Id));
        Assert.Empty(await db.Set<WhatsAppIncomingReceipt>().ToListAsync());
        Assert.Equal(ParticipationStatus.NoResponse, (await db.EventParticipants.SingleAsync()).Status);
        Assert.Equal(0, gateway.Sends);
    }

    [Fact]
    public async Task MalformedPresenceFromUnassociatedSenderOrPrivateChatGetsNoFunReply()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        Assert.Equal(new WhatsAppIncomingResult(false), await service.IncomingAsync(Incoming("!presenza bho", "unknown") with { SenderIds = ["unknown@lid"] }));
        var privateReply = await service.IncomingAsync(Incoming("!presenza bho", "private") with { IsPrivate = true });
        Assert.False(privateReply.Handled); Assert.Null(privateReply.ReplyText);
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Empty(await db.Set<WhatsAppCommandReceipt>().ToListAsync());
        Assert.Equal(0, await db.Set<LocalAudit>().CountAsync(x => x.Action == WhatsAppService.FunReplyAction));
    }

    [Fact]
    public async Task ValidPresenceCommandsCancelAndDialogAnswersKeepPreviousBehavior()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        Assert.False((await service.IncomingAsync(Incoming("!annulla", "cancel-outside"))).Handled);
        Assert.False((await service.IncomingAsync(Incoming("30/12", "answer-outside"))).Handled);
        Assert.False((await service.IncomingAsync(Incoming("!annulla adesso", "cancel-extra"))).Handled);
        Assert.False((await service.IncomingAsync(Incoming("!vota abcdef12 1", "unknown-command"))).Handled);
        var edit = await service.IncomingAsync(Incoming("!presenza modifica", "edit"));
        Assert.DoesNotContain(WhatsAppService.PresenceUsage, edit.ReplyText ?? "");
        Assert.Equal(new WhatsAppIncomingResult(true, false, "✅"), await service.IncomingAsync(Incoming("!presenza forse", "maybe")));
        Assert.Equal(new WhatsAppIncomingResult(true, false, "✅"), await service.IncomingAsync(Incoming("!presenza no", "no")));
        var yes = await service.IncomingAsync(Incoming("!presenza sì", "yes"));
        Assert.Contains("Quando arrivi", yes.ReplyText); Assert.DoesNotContain(WhatsAppService.PresenceUsage, yes.ReplyText);
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Equal(0, await db.Set<LocalAudit>().CountAsync(x => x.Action == WhatsAppService.FunReplyAction));
    }

    [Fact]
    public async Task FunReplyIsDeterministicIdempotentAndConflictsOnDifferentText()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service);
        var ids = Enumerable.Range(0, 100).Select(i => "pick-" + i).ToArray();
        var first = ids[0]; var second = ids.First(x => ExpectedFunIndex(x) != ExpectedFunIndex(first));
        var reply = await service.IncomingAsync(Incoming("!presenza bho", first));
        Assert.Equal(ExpectedPresenceMisuse(first), reply.ReplyText);
        Assert.Equal(new WhatsAppIncomingResult(true, true), await service.IncomingAsync(Incoming("!presenza bho", first)));
        await Assert.ThrowsAsync<LocalConflictException>(() => service.IncomingAsync(Incoming("!presenza boh", first)));
        clock.Utc = Now.AddMinutes(5);
        var other = await service.IncomingAsync(At(Incoming("!presenza bho", second), clock));
        Assert.Equal(ExpectedPresenceMisuse(second), other.ReplyText);
        Assert.NotEqual(reply.ReplyText, other.ReplyText);
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Equal(2, await db.Set<WhatsAppCommandReceipt>().CountAsync());
        Assert.Equal(2, await db.Set<LocalAudit>().CountAsync(x => x.Action == WhatsAppService.FunReplyAction));
    }

    [Fact]
    public async Task ExistingSyntaxRepliesGetFunPhraseOnTopWhileHelpStaysUnchanged()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service);
        var summary = await service.IncomingAsync(Incoming("!riepilogo x/y", "summary-bad"));
        Assert.Equal(WhatsAppService.SummaryMisuseTexts[ExpectedFunIndex("summary-bad")] + "\n\n" + SummarySyntax, summary.ReplyText);
        AssertPhraseOnlyFrom(WhatsAppService.SummaryMisuseTexts, summary.ReplyText!);
        clock.Utc = Now.AddMinutes(5);
        var todo = await service.IncomingAsync(At(Incoming("!todo -1", "todo-bad"), clock));
        Assert.Equal(WhatsAppService.TodoMisuseTexts[ExpectedFunIndex("todo-bad")] + "\n\nℹ️ Sintassi:\n!todo [pagina]\nIndica una pagina intera da 1 a 100000.", todo.ReplyText);
        AssertPhraseOnlyFrom(WhatsAppService.TodoMisuseTexts, todo.ReplyText!);
        clock.Utc = Now.AddMinutes(10);
        var menu = await service.IncomingAsync(At(Incoming("!menu abc", "menu-bad"), clock));
        Assert.Equal(WhatsAppService.MenuMisuseTexts[ExpectedFunIndex("menu-bad")] + "\n\nℹ️ Sintassi:\n!menu [pagina]\nIndica una pagina intera da 1 a 100000.", menu.ReplyText);
        AssertPhraseOnlyFrom(WhatsAppService.MenuMisuseTexts, menu.ReplyText!);
        clock.Utc = Now.AddMinutes(15);
        var help = await service.IncomingAsync(At(Incoming("!aiuto inesistente", "help-bad"), clock));
        Assert.StartsWith("🤖 Voce non trovata", help.ReplyText);
        Assert.DoesNotContain(AllMisuseGroups.SelectMany(x => x), help.ReplyText!.Contains);
        var page = await service.IncomingAsync(At(Incoming("!riepilogo", "summary-good"), clock));
        Assert.DoesNotContain(AllMisuseGroups.SelectMany(x => x), page.ReplyText!.Contains);
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Equal(3, await db.Set<LocalAudit>().CountAsync(x => x.Action == WhatsAppService.FunReplyAction));
    }

    [Fact]
    public async Task PersonLimitAllowsOneFunReplyEveryFiveMinutesAndKeepsSyntaxWithoutPhrase()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service);
        Assert.NotNull((await service.IncomingAsync(Incoming("!presenza bho", "limit-1"))).ReplyText);
        clock.Utc = Now.AddMinutes(4).AddSeconds(59);
        var silent = await service.IncomingAsync(At(Incoming("!presenza bho", "limit-2"), clock));
        Assert.Equal(new WhatsAppIncomingResult(true), silent);
        Assert.Equal(new WhatsAppIncomingResult(true, true), await service.IncomingAsync(At(Incoming("!presenza bho", "limit-2"), clock)));
        var syntax = await service.IncomingAsync(At(Incoming("!riepilogo x/y", "limit-3"), clock));
        Assert.Equal(SummarySyntax, syntax.ReplyText); Assert.NotNull(syntax.ReplyOperationId);
        clock.Utc = Now.AddMinutes(5);
        Assert.Equal(ExpectedPresenceMisuse("limit-4"), (await service.IncomingAsync(At(Incoming("!presenza", "limit-4"), clock))).ReplyText);
        var db = p.GetRequiredService<TRobotDbContext>();
        Assert.Equal(4, await db.Set<WhatsAppCommandReceipt>().CountAsync());
        Assert.Equal(2, await db.Set<LocalAudit>().CountAsync(x => x.Action == WhatsAppService.FunReplyAction));
    }

    [Fact]
    public async Task GroupLimitAllowsSixFunRepliesEveryTenMinutes()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var clock = new Clock(); var service = Service(p, new FakeGateway(), clock); await Activate(p, f, service);
        var db = p.GetRequiredService<TRobotDbContext>();
        for (var i = 1; i <= 7; i++)
        {
            var person = new Person("Persona sintetica " + i);
            db.AddRange(person, new EventParticipant(f.EventId, person.Id), new WhatsAppIdentityAlias(f.EventId, Options().Fingerprint("sender", $"{i}00@lid"), person.Id));
        }
        await db.SaveChangesAsync();
        Assert.NotNull((await service.IncomingAsync(Incoming("!presenza bho", "group-0"))).ReplyText);
        clock.Utc = Now.AddMinutes(1);
        for (var i = 1; i <= 5; i++)
            Assert.NotNull((await service.IncomingAsync(At(Incoming("!presenza bho", "group-" + i), clock) with { SenderIds = [$"{i}00@lid"] })).ReplyText);
        Assert.Equal(new WhatsAppIncomingResult(true), await service.IncomingAsync(At(Incoming("!presenza bho", "group-6"), clock) with { SenderIds = ["600@lid"] }));
        Assert.Equal(SummarySyntax, (await service.IncomingAsync(At(Incoming("!riepilogo x/y", "group-7"), clock) with { SenderIds = ["700@lid"] })).ReplyText);
        clock.Utc = Now.AddMinutes(10);
        Assert.NotNull((await service.IncomingAsync(At(Incoming("!presenza bho", "group-8"), clock) with { SenderIds = ["700@lid"] })).ReplyText);
        Assert.Equal(7, await db.Set<LocalAudit>().CountAsync(x => x.Action == WhatsAppService.FunReplyAction));
    }
}
