using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TRobot.Web.Security;

namespace TRobot.Tests;

public sealed class PortalModuleTests
{
    [Theory]
    [InlineData("Polls")]
    [InlineData("Games")]
    [InlineData("Messages")]
    [InlineData("Menu")]
    [InlineData("Tasks")]
    public void SecondaryLinksUseTheSameAvailabilityAsEventTabs(string controller)
    {
        var modules = PortalModules.Load(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { [$"PortalModules:Enabled:{controller}"] = "false" }).Build());
        var helper = new PortalModuleLinkTagHelper(modules) { Controller = controller, Action = "Index" };
        var output = new TagHelperOutput("a", new TagHelperAttributeList { { "href", $"/{controller}?eventId=synthetic" } },
            (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent().SetContent("Voce esistente")));
        var context = new TagHelperContext(new TagHelperAttributeList(), new Dictionary<object, object>(), "synthetic-link");
        helper.Process(context, output);
        Assert.Equal("span", output.TagName);
        Assert.False(output.Attributes.ContainsName("href"));
        Assert.Equal("true", output.Attributes["aria-disabled"].Value);
    }

    [Theory]
    [InlineData("Unknown", "false")]
    [InlineData("0", "false")]
    [InlineData("Polls,Games", "false")]
    [InlineData("Polls", "maybe")]
    public void ConfigurationRejectsUnknownNamesAndInvalidFlags(string module, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { [$"PortalModules:Enabled:{module}"] = value }).Build();
        Assert.Throws<InvalidOperationException>(() => PortalModules.Load(configuration));
    }

    [Fact]
    public void ConfigurationUsesExplicitFlagsAndLeavesOperationalRoutesIndependent()
    {
        var modules = PortalModules.Load(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["PortalModules:Enabled:polls"] = "false", ["PortalModules:Enabled:Simulator"] = "false" }).Build());
        Assert.False(modules.IsRouteEnabled("pOlLs", "Save"));
        Assert.False(modules.IsRouteEnabled("HOME", "sImUlAtE"));
        Assert.True(modules.IsRouteEnabled("Home", "Index"));
        Assert.True(modules.IsRouteEnabled("WhatsAppIncoming", "Receive"));
        Assert.True(modules.IsRouteEnabled("WhatsApp", "Index"));
        Assert.True(modules.IsRouteEnabled("History", "Index"));
        Assert.True(modules.IsRouteEnabled("Menu", "Index"));
    }

    [Fact]
    public async Task SuspendedPortalActionsReturn404ForEveryGetAndPostWithoutWriting()
    {
        var writes = new WriteCounter();
        await using var original = new WebFlowTests.LocalWebFactory { SaveInterceptor = writes };
        await using var factory = SuspendedHost(original);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.False(factory.Services.GetRequiredService<IConfiguration>().GetValue<bool>("PortalModules:Enabled:Simulator"));
        Assert.False(factory.Services.GetRequiredService<PortalModules>().IsEnabled(PortalModule.Simulator));
        await Login(client);
        var dashboard = await client.GetStringAsync("/");
        var csrf = Token(dashboard);
        var countBefore = writes.Count;
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .Where(x => PortalModules.ModuleFor(x.ControllerName, x.ActionName).HasValue).ToList();
        Assert.NotEmpty(actions);
        foreach (var action in actions)
        {
            var methods = action.ActionConstraints!.OfType<HttpMethodActionConstraint>().SelectMany(x => x.HttpMethods)
                .Where(x => x is "GET" or "POST");
            foreach (var method in methods)
            {
                // No antiforgery token on purpose: suspended POSTs must stop before antiforgery/model binding.
                using var request = new HttpRequestMessage(new HttpMethod(method),
                    $"/{action.ControllerName.ToLowerInvariant()}/{action.ActionName.ToLowerInvariant()}?eventId={original.EventId}");
                if (method == "POST") request.Content = new FormUrlEncodedContent(new Dictionary<string, string>());
                using var response = await client.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.NotFound,
                    $"{method} {request.RequestUri}: {response.StatusCode}");
                if (method == "POST")
                {
                    using var validCsrf = await client.PostAsync(request.RequestUri, new FormUrlEncodedContent(new Dictionary<string, string>
                        { ["__RequestVerificationToken"] = csrf, ["EventId"] = original.EventId.ToString() }));
                    Assert.Equal(HttpStatusCode.NotFound, validCsrf.StatusCode);
                }
            }
        }
        foreach (var controller in new[] { "Tasks", "Menu", "Polls", "Games", "Messages" })
        {
            using var shortRoute = await client.GetAsync($"/{controller}?eventId={original.EventId}");
            Assert.Equal(HttpStatusCode.NotFound, shortRoute.StatusCode);
            using var idRoute = await client.GetAsync($"/{controller}/Index/{original.EventId}");
            Assert.Equal(HttpStatusCode.NotFound, idRoute.StatusCode);
        }
        Assert.Equal(countBefore, writes.Count);
    }

    [Fact]
    public async Task DashboardKeepsAttendanceAndHistoryAndRendersSuspendedLinksWithoutHref()
    {
        await using var original = new WebFlowTests.LocalWebFactory();
        await using var factory = SuspendedHost(original);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Login(client);
        var html = await client.GetStringAsync($"/Home/Index?eventId={original.EventId}");
        Assert.Contains("id=\"confirmed-count\"", html);
        Assert.DoesNotContain("Elabora messaggio simulato", html);
        Assert.DoesNotContain("action=\"/Home/Simulate", html);
        Assert.Contains("aria-disabled=\"true\"", html);
        Assert.Contains("I moduli in grigio sono temporaneamente non disponibili", html);
        foreach (var controller in new[] { "Tasks", "Menu", "Polls", "Games", "Messages" })
            Assert.DoesNotMatch($"(?i)<a\\b[^>]*href=\"/{controller}(?:/|\\?|\")", html);
        foreach (var controller in new[] { "History", "Events", "Stays", "Lodging" })
        {
            var path = controller == "Events" ? $"/Events/Details/{original.EventId}" : $"/{controller}/Index?eventId={original.EventId}";
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task VersionedPortalDefaultsBlockSuspendedSimulationCommandsButKeepAttendanceAndMessages()
    {
        var writes = new WriteCounter();
        await using var original = new WebFlowTests.LocalWebFactory { SaveInterceptor = writes };
        // Reload the actual versioned defaults after the legacy factory's explicit prototype opt-in.
        await using var factory = original.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((context, configuration) =>
            configuration.AddJsonFile(Path.Combine(context.HostingEnvironment.ContentRootPath, "appsettings.json"), optional: false)));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await Login(client);
        var flags = factory.Services.GetRequiredService<PortalModules>();
        foreach (var module in new[] { PortalModule.Tasks, PortalModule.Menu, PortalModule.Polls, PortalModule.Games })
            Assert.False(flags.IsEnabled(module));
        Assert.True(flags.IsEnabled(PortalModule.Messages));
        Assert.True(flags.IsEnabled(PortalModule.Simulator));
        var html = await client.GetStringAsync("/");
        Assert.Contains("Elabora messaggio simulato", html);
        Assert.DoesNotContain("!menu", html);
        Assert.DoesNotContain("!todo", html);
        Assert.DoesNotContain("!quiz", html);
        Assert.DoesNotContain("!sondaggi", html);
        var token = Token(html);
        var before = writes.Count;
        foreach (var command in new[] { "!menu", "!porto", "!todo", "!prendo", "!fatto", "!sondaggi", "!sondaggio", "!vota", "!quiz", "!rispondi" })
        {
            using var response = await Submit(" \t" + command.ToUpperInvariant() + "\t synthetic 1  ");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("Il modulo richiesto", await response.Content.ReadAsStringAsync());
        }
        using (var help = await Submit(" \t!AIUTO  "))
        {
            var helpHtml = await help.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, help.StatusCode);
            Assert.Contains("!presenza", helpHtml);
            Assert.DoesNotContain("!vota", helpHtml);
            Assert.DoesNotContain("!porto", helpHtml);
        }
        Assert.Equal(before, writes.Count);
        foreach (var controller in new[] { "Tasks", "Menu", "Polls", "Games" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/{controller}?eventId={original.EventId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/Messages?eventId={original.EventId}")).StatusCode);
        using var presence = await Submit("!presenza si");
        Assert.Equal(HttpStatusCode.Redirect, presence.StatusCode);
        Assert.True(writes.Count > before);
        Assert.Contains("id=\"confirmed-count\">1", await client.GetStringAsync(presence.Headers.Location));

        Task<HttpResponseMessage> Submit(string text) => client.PostAsync("/Home/Simulate", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["Input.EventId"] = original.EventId.ToString(),
            ["Input.SenderId"] = "synthetic-http-sender", ["Input.MessageId"] = Guid.NewGuid().ToString("N"),
            ["Input.Text"] = text, ["Input.ConfirmVote"] = "true"
        }));
    }

    private static WebApplicationFactory<Program> SuspendedHost(WebFlowTests.LocalWebFactory original) =>
        original.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(Enum.GetNames<PortalModule>().ToDictionary(
                x => $"PortalModules:Enabled:{x}", _ => (string?)"false"))));

    private static async Task Login(HttpClient client)
    {
        var html = await client.GetStringAsync("/Access/Login");
        var token = Token(html);
        using var response = await client.PostAsync("/Access/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["UserName"] = WebFlowTests.LocalWebFactory.UserName,
            ["Password"] = WebFlowTests.LocalWebFactory.Password,
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static string Token(string html) => WebUtility.HtmlDecode(
        Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);

    private sealed class WriteCounter : SaveChangesInterceptor
    {
        public int Count { get; private set; }
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            Count++;
            return result;
        }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
