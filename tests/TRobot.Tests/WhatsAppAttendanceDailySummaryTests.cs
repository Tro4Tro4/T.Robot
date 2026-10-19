using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;
using LocalWebFactory = TRobot.Tests.WebFlowTests.LocalWebFactory;

namespace TRobot.Tests;

public sealed partial class WhatsAppIntegrationTests
{
    [Theory]
    [InlineData("29/12", 0)]
    [InlineData("30/12", 2)]
    [InlineData("01/01", 2)]
    [InlineData("02/01/2031", 2)]
    [InlineData("03/01", 0)]
    public async Task DailySummaryUsesInclusiveDaysAndEventYearAndExcludesMissingDates(string date, int expected)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        var db = p.GetRequiredService<TRobotDbContext>(); var family = new Household(f.EventId, "Nucleo *Dolci*", Guid.NewGuid()); db.Add(family);
        var adult = SummaryPerson(db, f.EventId, ParticipationStatus.Confirmed, family);
        var child = SummaryPerson(db, f.EventId, ParticipationStatus.Confirmed, family, HouseholdMemberKind.Child);
        var absent = SummaryPerson(db, f.EventId, ParticipationStatus.Absent, family);
        SummaryPerson(db, f.EventId, ParticipationStatus.Confirmed, family);
        foreach (var id in new[] { adult, child, absent }) { var stay = new AttendanceStay(f.EventId, id); stay.Set(new(2030, 12, 30), 4); db.Add(stay); }
        await db.SaveChangesAsync();
        var text = (await service.IncomingAsync(Incoming("!riepilogo " + date, "daily"))).ReplyText!;
        Assert.Contains("Confermati: " + expected, text); Assert.Contains("Adulti: " + expected / 2 + " · Bambini: " + expected / 2, text);
        Assert.Contains("soggiorno da completare: 1 (esclusi dal giorno)", text);
        Assert.DoesNotContain("NOME INDIVIDUALE", text); Assert.DoesNotContain("30/12/2030 al", text);
        Assert.Equal(3, await db.Set<AttendanceStay>().CountAsync()); Assert.Empty(await db.Set<AttendanceStayChange>().ToListAsync());
    }

    [Fact]
    public async Task DailySummaryPagesFiveHouseholdsWithinTransportLimitAndKeepsFullDateInFooter()
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service); await SetStayEvent(p, f);
        var db = p.GetRequiredService<TRobotDbContext>();
        for (var i = 0; i < 6; i++)
        {
            var family = new Household(f.EventId, $"Nucleo {i:D2} " + new string('x', 140), Guid.NewGuid()); db.Add(family);
            var id = SummaryPerson(db, f.EventId, ParticipationStatus.Confirmed, family);
            var stay = new AttendanceStay(f.EventId, id); stay.Set(new(2030, 12, 30), 4); db.Add(stay);
        }
        await db.SaveChangesAsync();
        var one = (await service.IncomingAsync(Incoming("!riepilogo 01/01", "daily-one"))).ReplyText!;
        var two = (await service.IncomingAsync(Incoming("!riepilogo 01/01/2031 2", "daily-two"))).ReplyText!;
        Assert.Equal(5, one.Split("🏡").Length - 1); Assert.Equal(1, two.Split("🏡").Length - 1);
        Assert.Contains("!riepilogo 01/01/2031 2", one); Assert.Contains("Nucleo 00", one); Assert.Contains("Nucleo 05", two);
        Assert.All(new[] { one, two }, text => Assert.True(text.Length <= 2000));
        Assert.Contains("!riepilogo 01/01/2031 1", (await service.IncomingAsync(Incoming("!riepilogo 01/01 3", "daily-three"))).ReplyText);
    }

    [Theory]
    [InlineData("31/02")]
    [InlineData("01/01 0")]
    [InlineData("01/01 1 extra")]
    [InlineData("01/01/00")]
    public async Task DailySummaryRejectsInvalidDateAndPage(string args)
    {
        await using var f = new LocalWebFactory(); using var scope = f.Services.CreateScope(); var p = scope.ServiceProvider;
        var service = Service(p, new FakeGateway(), new Clock()); await Activate(p, f, service);
        Assert.Contains("Sintassi", (await service.IncomingAsync(Incoming("!riepilogo " + args))).ReplyText);
    }
}
