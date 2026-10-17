using TRobot.Application.Local;
using TRobot.Application.Messaging;

namespace TRobot.Web.Services;

/// <summary>Development-only local simulation. No external transport or durable operating-system job.</summary>
public sealed class AutomaticSchedulerWorker(IServiceScopeFactory scopes, TimeProvider clock,
    IConfiguration configuration, ILogger<AutomaticSchedulerWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Resolve at runtime so host/test configuration overrides take effect before any work.
        if (!configuration.GetValue("LocalScheduler:Enabled", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30), clock);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                // Never log exception payloads: they may contain SQL or free-form private content.
                logger.LogWarning("Discovery dello scheduler locale non disponibile; nuovo tentativo al prossimo ciclo.");
            }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        if (!configuration.GetValue("LocalScheduler:Enabled", true)) return;
        ct.ThrowIfCancellationRequested();
        IReadOnlyList<AutomaticScheduleCandidate> candidates;
        await using (var discovery = scopes.CreateAsyncScope())
            candidates = await discovery.ServiceProvider.GetRequiredService<IAutomaticSchedulerStore>().CandidatesAsync(ct);
        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<MessageService>()
                    .TickAutomaticAsync(candidate.ActorId, candidate.EventId, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (LocalAccessException) { /* Access can be revoked after discovery. */ }
            catch (LocalConflictException) { /* Retry only on the next cycle, with a fresh scope. */ }
            catch (Exception)
            {
                logger.LogWarning("Elaborazione automatica locale non riuscita per evento {EventId}; nuovo tentativo al prossimo ciclo.", candidate.EventId);
            }
        }
    }
}
