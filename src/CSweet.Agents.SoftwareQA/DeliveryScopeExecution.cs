using System.Text.Json;
using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agents.SoftwareQA;

public sealed partial class SoftwareQaAgent
{
    protected override async Task<AgentWorkResult> ExecuteDeliveryScopeAsync(WorkExecutionAssignmentV2 assignment,
        AgentRuntimeContext context, CancellationToken ct)
    {
        try
        {
            if (assignment.StageKey != "quality" || assignment.Candidate is not { } candidate)
                throw new InvalidOperationException("QA requires the assigned aggregate quality candidate.");
            if (candidate.Repositories.Count == 0)
            {
                var selection = new AgentLlmSelection(Settings.GetGuid("llmProviderId") ??
                    throw new InvalidOperationException("Configure an approved QA provider."), Settings.GetString("llmModel"));
                var client = _llmFactory is null ? context.CreateChatClient(selection) : await _llmFactory.CreateChatClientAsync(selection, ct);
                return await DeliveryScopeReview.ExecuteAsync(assignment, context, client, ct);
            }
            var input = assignment.Input.Deserialize<WorkExecutionInputV1>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            await using var workspace = await DeliveryCandidateWorkspace.MaterializeAsync(assignment, context, ct);
            await RunHarnessAsync(workspace.Path,
                "Run full aggregate testing and regression for EVERY repository under repositories/<repositoryId N format>, and QA all exact documents under documents/. " +
                "Do not change source. Every validation command must include the repository identifier in its path. " +
                "Write .csweet/qa-outcome.json with {deliveryCandidateDigest,verdict,summary,criteria:[{criterion,status,evidence}]," +
                "validations:[{command,status,exitCode,diagnosticExcerpt}],findings:[{title,severity,description,reproductionSteps,expectedBehavior,actualBehavior,evidence}],remainingRisks:[]}. " +
                "Use Passed|Failed|Blocked statuses, cover every exact criterion and retain the supplied complete candidate digest.\n" +
                JsonSerializer.Serialize(new { assignment.Scope, assignment.Instructions, Candidate = candidate, input.Planning }), context, ct);
            var report = await ReadOutcomeAsync(workspace.Path, ct);
            if (report.DeliveryCandidateDigest != candidate.Digest) throw new InvalidOperationException("QA did not identify the exact complete candidate digest.");
            await workspace.VerifySourceUnchangedAsync(ct);
            var approved = report.Verdict == QualityVerdicts.Passed;
            var result = new WorkDeliveryReviewResult(candidate.Digest, approved, report.Summary,
                report.Criteria.Select(x => new WorkDeliveryCriterionResult(x.Criterion, x.Status == QualityResultStatuses.Passed, x.Evidence)).ToArray(),
                approved ? [] : report.Findings.Count > 0 ? report.Findings.Select(x => x.Title + ": " + x.Description).ToArray() : [report.Summary])
            {
                Validations = candidate.Repositories.SelectMany(repository => report.Validations.Where(x => x.Command.Contains(repository.RepositoryId.ToString("N"), StringComparison.OrdinalIgnoreCase))
                    .Select(x => new WorkDeliveryValidationEvidence(repository.RepositoryId, repository.CandidateCommitSha, x.Command, x.ExitCode,
                        x.Status == QualityResultStatuses.Passed, x.DiagnosticExcerpt ?? ""))).ToArray()
            };
            if (approved && (candidate.Repositories.Any(r => result.Validations.All(v => v.RepositoryId != r.RepositoryId)) ||
                result.Validations.Any(x => !x.Succeeded || x.ExitCode != 0) || report.Findings.Count > 0))
                throw new InvalidOperationException("Passing release QA requires successful identified validation for every repository and no findings.");
            DeliveryScopeReview.Validate(result, candidate.Digest, input.Planning!.AcceptanceCriteria);
            return DeliveryScopeReview.Outcome(assignment, result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        { return AgentWorkResult.Success(new WorkExecutionOutcomeV1(assignment.StageExecutionId, assignment.AttemptId,
            WorkExecutionDispositions.Blocked, "blocked", error.Message, JsonSerializer.SerializeToElement(new { }), [], [error.Message])); }
    }
}
