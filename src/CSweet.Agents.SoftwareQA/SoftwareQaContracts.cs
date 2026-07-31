using CSweet.WorkManagement.Contracts;

namespace CSweet.Agents.SoftwareQA;

public sealed record SoftwareQaRequest(
    string? Repository, string? Objective, IReadOnlyList<string>? Requirements,
    IReadOnlyList<string>? AcceptanceCriteria, string? SourceCommitSha = null);

public sealed record SoftwareQaResponse(
    Guid WorkId, string Verdict, string Report, DateTimeOffset CompletedAt);

public sealed record SoftwareQaOutcome(
    string Verdict,
    string Summary,
    IReadOnlyList<QualityCriterionResult> Criteria,
    IReadOnlyList<QualityValidation> Validations,
    IReadOnlyList<QualityFinding> Findings,
    IReadOnlyList<string> RemainingRisks);
