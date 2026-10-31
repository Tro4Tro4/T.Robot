using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class MealProposalWebTests
{
    [Fact]
    public async Task ArchiveRequiresAssignedOrganizerAndRemainsAvailableWithMenuSuspended()
    {
        await using var original = new WebFlowTests.LocalWebFactory();
        using var factory = original.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["PortalModules:Enabled:Menu"] = "false" })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var url = "/MealProposals?eventId=" + original.EventId;
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(url)).StatusCode);
        var login = await client.GetStringAsync("/Access/Login");
        var token = Regex.Match(login, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        await client.PostAsync("/Access/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = WebFlowTests.LocalWebFactory.UserName,
            ["Password"] = WebFlowTests.LocalWebFactory.Password,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token)
        }));
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(url));
        Assert.Contains("Nessuna proposta confermata", html);
        Assert.Contains("!proponi", html);
        Assert.Contains("5 minuti", html);
        Assert.Contains("Come usare questa pagina", html);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var person = await db.EventParticipants.SingleAsync(x => x.EventId == original.EventId);
            db.AddRange(
                new MealProposal(original.EventId, person.PersonId, new DateOnly(2030, 12, 30), MealKind.Dinner, "Lasagne <script>alert(1)</script>", true, DateTime.UtcNow),
                new MealProposal(original.EventId, person.PersonId, new DateOnly(2030, 12, 31), MealKind.Lunch, "Risotto", false, DateTime.UtcNow));
            var otherEvent = new Event("Evento non assegnato", DateTime.UtcNow);
            db.AddRange(otherEvent, new EventParticipant(otherEvent.Id, person.PersonId),
                new MealProposal(otherEvent.Id, person.PersonId, new DateOnly(2030, 12, 30), MealKind.Dinner, "Piatto altro evento", false, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }
        var populated = await client.GetStringAsync(url);
        Assert.Contains("Lasagne &lt;script&gt;alert(1)&lt;/script&gt;", populated);
        Assert.DoesNotContain("<script>alert(1)</script>", populated);
        Assert.DoesNotContain("Piatto altro evento", populated);
        var decoded = WebUtility.HtmlDecode(populated);
        Assert.Contains("30/12/2030", decoded);
        Assert.Contains("31/12/2030", decoded);
        Assert.Contains("Disponibile a prepararlo", decoded);
        Assert.Contains("Suggerimento da mangiare", decoded);
        Assert.Contains("Partecipante test HTTP", decoded);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/Menu?eventId=" + original.EventId)).StatusCode);
        var denied = await client.GetAsync("/MealProposals?eventId=" + Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("Denied", denied.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsync(url, new StringContent(""))).StatusCode);
    }
}
