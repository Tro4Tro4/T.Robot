using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TRobot.Web.Security;

public enum PortalModule { Tasks, Menu, Polls, Games, Messages, Simulator }

// Portal availability only: Application services, the gateway and WhatsApp commands are independent.
public sealed class PortalModules
{
    public const string UnavailableMessage = "Temporaneamente non disponibile";
    private readonly HashSet<PortalModule> disabled;

    private PortalModules(HashSet<PortalModule> disabled) => this.disabled = disabled;

    public bool IsEnabled(PortalModule module) => !disabled.Contains(module);
    public bool HasDisabledModules => disabled.Count != 0;

    // Keep tokenization aligned with SimulationCommandService; no prefix or substring matching.
    private static string[] CommandParts(string? text) =>
        text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? [];

    public bool IsSimulatorCommandEnabled(string? text) => SimulatorModuleFor(text) is not { } module || IsEnabled(module);

    public static bool IsSimulatorHelp(string? text)
    {
        var parts = CommandParts(text);
        return parts.Length == 1 && string.Equals(parts[0], "!aiuto", StringComparison.OrdinalIgnoreCase);
    }

    public static PortalModule? SimulatorModuleFor(string? text) => CommandParts(text).FirstOrDefault()?.ToLowerInvariant() switch
    {
        "!menu" or "!porto" => PortalModule.Menu,
        "!todo" or "!prendo" or "!fatto" => PortalModule.Tasks,
        "!sondaggi" or "!sondaggio" or "!vota" => PortalModule.Polls,
        "!quiz" or "!rispondi" => PortalModule.Games,
        _ => null
    };

    public string SimulatorHelp
    {
        get
        {
            var commands = new List<string> { "!presenza si|forse|no" };
            if (IsEnabled(PortalModule.Menu)) commands.Add("!menu [pagina]; !porto <idvoce>");
            if (IsEnabled(PortalModule.Tasks)) commands.Add("!todo [pagina]; !prendo <idattività>; !fatto <idattività>");
            if (IsEnabled(PortalModule.Polls)) commands.Add("!sondaggi [pagina]; !sondaggio <id>; !vota <id> <numero opzione>");
            if (IsEnabled(PortalModule.Games)) commands.Add("!quiz; !quiz lista [pagina]; !quiz <id>; !rispondi <id> <numero opzione>");
            commands.Add("!aiuto");
            return "Comandi disponibili nel simulatore: " + string.Join("; ", commands) + ".";
        }
    }

    public bool IsRouteEnabled(string? controller, string? action) =>
        ModuleFor(controller, action) is not { } module || IsEnabled(module);

    public static PortalModule? ModuleFor(string? controller, string? action)
    {
        if (string.Equals(controller, "Home", StringComparison.OrdinalIgnoreCase))
            return string.Equals(action, "Simulate", StringComparison.OrdinalIgnoreCase) ? PortalModule.Simulator : null;
        return controller?.ToUpperInvariant() switch
        {
            "TASKS" => PortalModule.Tasks,
            "MENU" => PortalModule.Menu,
            "POLLS" => PortalModule.Polls,
            "GAMES" => PortalModule.Games,
            "MESSAGES" => PortalModule.Messages,
            _ => null
        };
    }

    public static PortalModules Load(IConfiguration configuration)
    {
        var disabled = new HashSet<PortalModule>();
        foreach (var value in configuration.GetSection("PortalModules:Enabled").GetChildren())
        {
            // Accept names only, never numeric enum values or comma-separated combinations.
            var name = Enum.GetNames<PortalModule>().SingleOrDefault(x => string.Equals(x, value.Key, StringComparison.OrdinalIgnoreCase));
            if (name is null || !bool.TryParse(value.Value, out var enabled) || value.GetChildren().Any())
                throw new InvalidOperationException($"Modulo portale non valido in {value.Path}.");
            if (!enabled) disabled.Add(Enum.Parse<PortalModule>(name));
        }
        return new(disabled);
    }
}

// Run before antiforgery/model binding so every request to a suspended action returns 404.
public sealed class PortalModuleFilter(PortalModules modules) : IAuthorizationFilter, IOrderedFilter
{
    public int Order => int.MinValue;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor is ControllerActionDescriptor action &&
            !modules.IsRouteEnabled(action.ControllerName, action.ActionName))
            context.Result = new NotFoundResult();
    }
}
