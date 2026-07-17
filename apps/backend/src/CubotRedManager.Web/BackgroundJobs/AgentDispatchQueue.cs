using System.Collections.Concurrent;
using CubotRedManager.Application.Tenancy;

namespace CubotRedManager.Web.BackgroundJobs;

/// <summary>
/// Procesador en background del agente de IA. Portado 1:1 desde CUBOT.travels.
/// Resuelve: respuesta rapida al webhook, serializacion por conversacion, debounce de rafagas.
/// </summary>
public sealed class AgentDispatchQueue : BackgroundService, IAgentDispatchQueue
{
    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MaxBurstWait = TimeSpan.FromSeconds(35);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentDispatchQueue> _logger;
    private readonly TimeProvider _timeProvider;

    private readonly ConcurrentDictionary<Guid, Pending> _pending = new();
    private readonly ConcurrentDictionary<Guid, byte> _processing = new();

    public AgentDispatchQueue(IServiceScopeFactory scopeFactory, ILogger<AgentDispatchQueue> logger, TimeProvider timeProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    private sealed class Pending
    {
        public Guid TenantId;
        public Guid? LineId;
        public readonly List<string> Bodies = new();
        public long ReadyAtTicks;
        public long DeadlineTicks;
        public readonly object Gate = new();
    }

    public void Enqueue(Guid tenantId, Guid conversationId, Guid? whatsAppLineId, string inboundBody)
    {
        var now = _timeProvider.GetUtcNow();
        var readyAt = now.Add(DebounceWindow).UtcTicks;
        _pending.AddOrUpdate(
            conversationId,
            _ =>
            {
                var p = new Pending { TenantId = tenantId, LineId = whatsAppLineId, ReadyAtTicks = readyAt, DeadlineTicks = now.Add(MaxBurstWait).UtcTicks };
                p.Bodies.Add(inboundBody ?? string.Empty);
                return p;
            },
            (_, p) =>
            {
                lock (p.Gate)
                {
                    p.TenantId = tenantId;
                    if (whatsAppLineId is not null) { p.LineId = whatsAppLineId; }
                    p.Bodies.Add(inboundBody ?? string.Empty);
                    p.ReadyAtTicks = readyAt;
                }
                return p;
            });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AgentDispatchQueue iniciado (debounce {Debounce}s).", DebounceWindow.TotalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            try { PumpOnce(stoppingToken); }
            catch (Exception ex) { _logger.LogError(ex, "AgentDispatchQueue: error en el bucle de despacho."); }

            try { await Task.Delay(PollInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void PumpOnce(CancellationToken stoppingToken)
    {
        var now = _timeProvider.GetUtcNow().UtcTicks;

        foreach (var kvp in _pending)
        {
            var conversationId = kvp.Key;
            var pending = kvp.Value;

            long readyAt, deadline;
            lock (pending.Gate) { readyAt = pending.ReadyAtTicks; deadline = pending.DeadlineTicks; }
            if (readyAt > now && deadline > now) { continue; }
            if (_processing.ContainsKey(conversationId)) { continue; }

            if (!_pending.TryRemove(conversationId, out var claimed)) { continue; }

            Guid tenantId; Guid? lineId; string body;
            lock (claimed.Gate)
            {
                tenantId = claimed.TenantId;
                lineId = claimed.LineId;
                body = string.Join("\n", claimed.Bodies);
            }

            _processing[conversationId] = 1;
            _ = Task.Run(() => ProcessAsync(tenantId, conversationId, lineId, body, stoppingToken), stoppingToken);
        }
    }

    private async Task ProcessAsync(Guid tenantId, Guid conversationId, Guid? lineId, string body, CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            // El dispatcher escribe en la BD como si fuera el tenant del webhook (sin claims).
            scope.ServiceProvider.GetRequiredService<CubotRedManager.Application.Abstractions.IAmbientTenantOverride>().Set(tenantId, null);
            var dispatcher = scope.ServiceProvider.GetRequiredService<IAgentDispatcher>();
            await dispatcher.DispatchAsync(tenantId, conversationId, lineId, body, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AgentDispatchQueue: dispatch fallo para conv {ConvId}.", conversationId);
        }
        finally
        {
            _processing.TryRemove(conversationId, out _);
        }
    }
}
