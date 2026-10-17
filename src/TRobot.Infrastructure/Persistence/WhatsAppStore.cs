using Microsoft.EntityFrameworkCore;
using TRobot.Application.WhatsApp;
using TRobot.Domain;

namespace TRobot.Infrastructure.Persistence;

public sealed class WhatsAppStore(TRobotDbContext db) : IWhatsAppStore
{
    public Task<bool> IsAdministratorAsync(Guid personId, CancellationToken ct) => db.Users.AnyAsync(x => x.PersonId == personId && x.IsEnabled && x.IsSystemAdministrator, ct);
    public Task<WhatsAppConnection?> ActiveConnectionAsync(CancellationToken ct) => db.Set<WhatsAppConnection>().SingleOrDefaultAsync(x => x.Enabled, ct);
    public Task<EventParticipant?> ParticipantAsync(Guid eventId, Guid personId, CancellationToken ct) => db.EventParticipants.SingleOrDefaultAsync(x => x.EventId == eventId && x.PersonId == personId, ct);
    public Task<WhatsAppIncomingReceipt?> ReceiptAsync(Guid eventId, string messageHash, CancellationToken ct) => db.Set<WhatsAppIncomingReceipt>().SingleOrDefaultAsync(x => x.EventId == eventId && x.MessageHash == messageHash, ct);
}
