using System.Globalization;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Simulation;

/// <summary>Public list presentation shared by the simulator and approved WhatsApp pages.</summary>
public static class OperationalListText
{
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    // Use visible equivalents for WhatsApp delimiters; preserve letters, emoji and punctuation.
    // Controls and directional overrides must never create commands or reorder references.
    public static string PublicText(string value) => string.Concat(value.Select(c => c switch
    {
        '*' => '＊', '_' => '＿', '~' => '～', '`' => '｀',
        _ when char.IsControl(c) || char.IsWhiteSpace(c) || c is '\u061c' or '\u200e' or '\u200f'
            or >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u206f' => ' ',
        _ => c
    }));

    public static IReadOnlyList<string> Menu(IReadOnlyList<MenuItem> source)
    {
        var items = source.Where(x => x.Availability != MenuAvailability.Cancelled)
            .OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Id).ToArray();
        var counts = $"{items.Length} voci · ✅ {items.Count(x => x.Availability == MenuAvailability.Ready)} pronte · 📦 {items.Count(x => x.Availability == MenuAvailability.Assigned)} assegnate · 🛒 {items.Count(x => x.Availability == MenuAvailability.Planned)} da organizzare";
        return Pages(items.Select(x => $"*{PublicText(x.Name)}*\n🍴 {x.Quantity.ToString("0.###", Italian)} {PublicText(x.Unit)} · {x.Availability switch { MenuAvailability.Ready => "✅ Pronto", MenuAvailability.Assigned => "📦 Assegnato", _ => "🛒 Da organizzare" }}\n"
            + (x.Availability == MenuAvailability.Planned && x.ResponsiblePersonId is null
                ? $"!porto {SimulationReferences.Short(x.Id)}" : $"Rif. {SimulationReferences.Short(x.Id)}")).ToArray(),
            "🍽️ *MENU", counts, "!menu", "Per offrirti, copia il comando sotto una voce libera.", "Io tengo la lista, voi scaldate i fornelli. 🍳");
    }

    public static IReadOnlyList<string> Todo(IReadOnlyList<OrganizationTask> source)
    {
        var items = source.Where(x => x.Status is OrganizationTaskStatus.Todo or OrganizationTaskStatus.InProgress)
            .OrderBy(x => x.Title, StringComparer.Ordinal).ThenBy(x => x.Id).ToArray();
        var counts = $"{items.Length} aperte · ⏳ {source.Count(x => x.Status == OrganizationTaskStatus.Todo)} da iniziare · 🔧 {source.Count(x => x.Status == OrganizationTaskStatus.InProgress)} in corso · ✅ {source.Count(x => x.Status == OrganizationTaskStatus.Completed)} completate";
        return Pages(items.Select(x => $"*{PublicText(x.Title)}*\n{(x.Status == OrganizationTaskStatus.Todo ? "⏳ Da iniziare" : "🔧 In corso")} · Rif. {SimulationReferences.Short(x.Id)}").ToArray(),
            "📋 *ATTIVITÀ", counts, "!todo", "Per prendere un'attività libera: !prendo <rif>\nPer completare solo una tua attività: !fatto <rif>",
            "La lista si accorcia, la festa si avvicina. 🎉");
    }

    public static string Page(IReadOnlyList<string> pages, int page, string command) => page <= pages.Count
        ? pages[page - 1] : throw new LocalValidationException($"ℹ️ Pagina inesistente.\nDisponibili {pages.Count} pagine. Riparti da:\n{command} 1");

    private static IReadOnlyList<string> Pages(string[] blocks, string title, string counts, string command, string instructions, string joke)
    {
        var pages = Math.Max(1, (blocks.Length + 4) / 5);
        return Enumerable.Range(1, pages).Select(page =>
            $"{title} · Pagina {page}/{pages}*\n{counts}\n\n"
            + (blocks.Length == 0 ? "Niente in elenco: tutto tranquillo per ora."
                : string.Join("\n\n", blocks.Skip((page - 1) * 5).Take(5)))
            + $"\n\n{instructions}"
            + (page < pages ? $"\n\n➡️ Pagina successiva:\n{command} {page + 1}" : "")
            + $"\n\n{joke}").ToArray();
    }
}
