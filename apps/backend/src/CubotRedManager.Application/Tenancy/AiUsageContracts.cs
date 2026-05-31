using CubotRedManager.Domain.Enums;

namespace CubotRedManager.Application.Tenancy;

public sealed record AgentUsageDto(Guid? AgentId, int Calls, long InputTokens, long OutputTokens, long TotalTokens, decimal EstimatedCostUsd);

public sealed record AiUsageSummaryDto(
    int TotalCalls,
    long TotalTokens,
    long InputTokens,
    long OutputTokens,
    decimal EstimatedCostUsd,
    IReadOnlyList<AgentUsageDto> ByAgent);

/// <summary>Cupo mensual de tokens de IA del plan de la agencia y su consumo del mes en curso.</summary>
public sealed record AiQuotaDto(long MonthlyLimitTokens, long MonthlyUsedTokens, bool Hard)
{
    public bool HasLimit => MonthlyLimitTokens > 0;
    public long Remaining => HasLimit ? Math.Max(0, MonthlyLimitTokens - MonthlyUsedTokens) : 0;
    public bool Exceeded => HasLimit && MonthlyUsedTokens >= MonthlyLimitTokens;
    public int UsedPct => HasLimit ? (int)Math.Min(100, Math.Round(100.0 * MonthlyUsedTokens / MonthlyLimitTokens)) : 0;
}

/// <summary>
/// Modulo de consumo de tokens (capa 3). Punto unico por el que pasa TODO uso de IA de la agencia:
/// registra proveedor, modelo, tokens y costo estimado. Provee indicadores globales y por agente.
/// </summary>
public interface IAiUsageService
{
    Task RecordAsync(Guid? agentId, AiProvider provider, string model, int inputTokens, int outputTokens, string source, bool success, CancellationToken cancellationToken = default);
    Task<AiUsageSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<AiQuotaDto> GetQuotaAsync(CancellationToken cancellationToken = default);
    public const string MonthlyTokenLimitKey = "max_ai_tokens_monthly";
}

/// <summary>Tarifas aproximadas (USD por 1M tokens) para estimar costo.</summary>
public static class AiCostEstimator
{
    private static readonly Dictionary<AiProvider, (decimal In, decimal Out)> RatesPerMillion = new()
    {
        [AiProvider.Claude] = (3m, 15m),
        [AiProvider.Gemini] = (1.25m, 10m),
        [AiProvider.ChatGpt] = (2.5m, 10m),
        [AiProvider.DeepSeek] = (0.27m, 1.10m)
    };

    public static decimal Estimate(AiProvider provider, int inputTokens, int outputTokens)
    {
        if (!RatesPerMillion.TryGetValue(provider, out var r)) { return 0m; }
        return Math.Round((inputTokens * r.In + outputTokens * r.Out) / 1_000_000m, 6);
    }
}
