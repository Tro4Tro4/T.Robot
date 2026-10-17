using System.Globalization;
using TRobot.Application.Local;

namespace TRobot.Application.Simulation;

public static class SimulationReplies
{
    public static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

    public static int ListPage(IReadOnlyList<string> parts)
    {
        if (parts.Count == 1) return 1;
        if (parts.Count == 2 && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var page)
            && page is >= 1 and <= 100000) return page;
        throw new LocalValidationException("Indica una pagina intera tra 1 e 100000, per esempio !menu 2 oppure !sondaggi 2.");
    }

    public static string PageText<T>(IReadOnlyList<T> items, int page, string command, Func<T, string> render)
    {
        const int size = 5;
        var pages = Math.Max(1, (items.Count + size - 1) / size);
        if (page > pages) throw new LocalValidationException($"Pagina inesistente: disponibili {pages} pagine. Usa {command} 1.");
        return $"\nPagina {page}/{pages}; {items.Count} elementi nell'elenco."
            + string.Concat(items.Skip((page - 1) * size).Take(size).Select(x => "\n" + render(x)))
            + (page < pages ? $"\nPagina successiva: {command} {page + 1}" : "");
    }
}
