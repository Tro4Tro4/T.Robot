using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Domain;
using TRobot.Infrastructure.Persistence;

namespace TRobot.Tests;

public sealed class PortalUsabilityTests
{
    [Fact]
    public async Task SingleEventDashboardKeepsNativeNavigationAndSimulationValidationReachable()
    {
        await using var factory = new WebFlowTests.LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Login(client);
        var html = await client.GetStringAsync("/");
        Assert.DoesNotContain("class=\"event-switcher\"", html);
        Assert.Contains("class=\"event-menu\" open data-mobile-collapse", html);
        Assert.Contains("class=\"account-menu\" open data-mobile-collapse", html);
        Assert.Contains("/js/mobile-portal.js?v=", html);
        Assert.Contains("class=\"simulation-panel\"", html);
        var csrf = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var invalid = await client.PostAsync("/Home/Simulate", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.EventId"] = factory.EventId.ToString(), ["Input.SenderId"] = "synthetic-http-sender",
            ["Input.MessageId"] = "mobile-invalid-command", ["Input.Text"] = "!nonvalido",
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(csrf)
        }));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Matches("<details[^>]*class=\"simulation-panel\"[^>]* open(?:=\"[^\"]*\")?[^>]*>", await invalid.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task MultipleAssignedEventsKeepTheSelectorAvailable()
    {
        await using var factory = new WebFlowTests.LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Login(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TRobotDbContext>();
            var additional = new Event("Secondo evento mobile", DateTime.UtcNow.AddDays(1));
            db.AddRange(additional, new EventOrganizer(additional.Id, factory.OrganizerPersonId));
            await db.SaveChangesAsync();
        }
        var html = await client.GetStringAsync("/");
        Assert.Contains("class=\"event-switcher\"", html);
        Assert.Contains("Secondo evento mobile", html);
        Assert.Contains("name=\"eventId\"", html);
    }

    [Fact]
    public async Task EventNavigationIncludesStaysAndContextHelpOnTheMainOrganizerPaths()
    {
        await using var factory = new WebFlowTests.LocalWebFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Login(client);
        foreach (var controller in new[] { "Home", "Events", "Households", "Stays", "Lodging", "Accounting", "History" })
        {
            var url = controller == "Events" ? $"/Events/Details/{factory.EventId}" : $"/{controller}/Index?eventId={factory.EventId}";
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Contains("Come usare questa pagina", html);
            Assert.Matches($"<a[^>]*href=\"/Stays(?:/Index)?\\?eventId={factory.EventId}\"", html);
            Assert.DoesNotContain("Area organizzatori · Prototipo locale", html);
        }
    }

    [Fact]
    public async Task DashboardDoesNotPresentUnavailableModulesAsWorkToComplete()
    {
        await using var original = new WebFlowTests.LocalWebFactory();
        await using var factory = original.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PortalModules:Enabled:Tasks"] = "false", ["PortalModules:Enabled:Menu"] = "false",
                ["PortalModules:Enabled:Polls"] = "false", ["PortalModules:Enabled:Games"] = "false"
            })));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Login(client);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));
        foreach (var label in new[] { "Menu pronto / assegnato", "Attività aperte", "Sondaggi aperti", "Adesioni giochi / quiz aperti" })
            Assert.DoesNotContain(label, html);
        Assert.Contains("Gestisci partecipanti", html);
        Assert.Contains("Controlla i soggiorni", html);
        Assert.Contains("Organizza gli alloggi", html);
        Assert.Contains("id=\"confirmed-count\"", html);
    }

    private static async Task Login(HttpClient client)
    {
        var html = await client.GetStringAsync("/Access/Login");
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        var response = await client.PostAsync("/Access/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = WebFlowTests.LocalWebFactory.UserName,
            ["Password"] = WebFlowTests.LocalWebFactory.Password,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token)
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
