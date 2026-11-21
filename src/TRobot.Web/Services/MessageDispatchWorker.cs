using TRobot.Application.Local;
using TRobot.Application.Messaging;

namespace TRobot.Web.Services;

/// <summary>
/// Persistent worker for confirmed WhatsApp messages. Registration is controlled by configuration,
/// so a deployment can install the code with automatic sends disabled and enable them deliberately.
/// </summary>
public sealed class MessageDispatchWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    TimeProvider clock, ILogger<MessageDispatchWorker> logger) : BackgroundService
{
    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue("Messaging:Dispatcher:Enabled", false);

    public static TimeSpan Interval(IConfiguration configuration) =>
        TimeSpan.FromSeconds(Math.Clamp(configuration.GetValue("Messaging:Dispatcher:IntervalSeconds", 30), 5, 300));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled(configuration)) return;
        using var timer = new PeriodicTimer(Interval(configuration), clock);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // Never log exception payloads: they may contain SQL or free-form private content.
                logger.LogWarning("Elaborazione degli invii WhatsApp non riuscita; nuovo tentativo al prossimo ciclo.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        if (!IsEnabled(configuration)) return;
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<Guid> candidates;
        DateTime now;
        await using (var discovery = scopes.CreateAsyncScope())
        {
            now = clock.GetUtcNow().UtcDateTime;
            candidates = await discovery.ServiceProvider.GetRequiredService<IOutboxDispatchStore>()
                .DispatchCandidatesAsync(now, now - MessageDispatchService.StaleClaim, ct);
        }
        foreach (var eventId in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var outcome = await scope.ServiceProvider.GetRequiredService<MessageDispatchService>()
                    .DispatchEventAsync(eventId, ct);
                if (outcome.Attempted > 0 || outcome.Recovered > 0)
                    logger.LogInformation("Invii WhatsApp: {Sent} confermati, {Failed} falliti, {Uncertain} incerti, {Recovered} recuperati.",
                        outcome.Sent, outcome.Failed, outcome.Uncertain, outcome.Recovered);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (LocalAccessException) { /* Access can be revoked after discovery. */ }
            catch (LocalConflictException) { /* Retry only on the next cycle, with a fresh scope. */ }
            catch (Exception)
            {
                logger.LogWarning("Invio WhatsApp non riuscito per evento {EventId}; nuovo tentativo al prossimo ciclo.", eventId);
            }
        }
    }
}
