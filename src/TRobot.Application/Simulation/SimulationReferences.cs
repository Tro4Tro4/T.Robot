using System.Security.Cryptography;
using System.Text;
using TRobot.Application.Local;
using TRobot.Domain;

namespace TRobot.Application.Simulation;

public static class SimulationReferences
{
    public static string Short(Guid id) => id.ToString("N")[..8];

    public static T Resolve<T>(IReadOnlyList<T> records, string reference) where T : class, ILocalRecord
    {
        if (string.IsNullOrWhiteSpace(reference)) throw new LocalValidationException("Riferimento obbligatorio.");
        if (Guid.TryParse(reference, out var full) && full != Guid.Empty)
            return records.SingleOrDefault(x => x.Id == full) ?? throw new LocalValidationException("Riferimento non trovato in questo evento.");
        if (reference.Length != 8 || !reference.All(Uri.IsHexDigit))
            throw new LocalValidationException("Usa il riferimento di 8 caratteri mostrato nell'elenco oppure il GUID completo.");
        var matches = records.Where(x => Short(x.Id).Equals(reference, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new LocalValidationException("Riferimento non trovato in questo evento."),
            _ => throw new LocalValidationException("Riferimento breve ambiguo: usa il GUID completo dalla pagina di dettaglio.")
        };
    }

    // Keep the v1 namespace to preserve existing vote receipts across upgrades.
    public static Guid MessageId(Guid eventId, string messageId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"TRobot:simulation-vote:v1:{eventId:N}:{messageId}"))[..16]);
}
