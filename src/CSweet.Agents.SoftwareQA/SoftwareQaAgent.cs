using System.Text.Json;
using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CSweet.Agents.SoftwareQA;

public sealed class SoftwareQaAgent : CSweetAgentBase
{
    private readonly IAgentLlmClientFactory? _llmFactory;
    private readonly ILogger<SoftwareQaAgent> _logger;

    public SoftwareQaAgent() => _logger = NullLogger<SoftwareQaAgent>.Instance;
    public SoftwareQaAgent(ILogger<SoftwareQaAgent> logger) => _logger = logger;
    public SoftwareQaAgent(
        IAgentLlmClientFactory llmFactory, ILogger<SoftwareQaAgent>? logger = null)
    {
        _llmFactory = llmFactory;
        _logger = logger ?? NullLogger<SoftwareQaAgent>.Instance;
    }

    public override string AgentId => SoftwareQaProfile.AgentId;
    public override string Version => SoftwareQaProfile.Version;

    protected override AgentConfigurationBuilder Configure(AgentConfigurationBuilder builder) =>
        builder
            .LlmProvider("llmProviderId", "LLM provider", required: true,
                description: "Approved provider used for software quality analysis.")
            .LlmModel("llmModel", "Model", "llmProviderId", required: true,
                description: "Quality-capable model from the approved provider.")
            .Number("maxContextWindowTokens", "Maximum context-window tokens", true,
                minimum: 16_000, maximum: 2_000_000, step: 1_000,
                defaultValue: SoftwareQaHarness.MaxContextWindowTokens)
            .Number("maxOutputTokens", "Maximum output tokens", true,
                minimum: 1_000, maximum: 200_000, step: 1_000,
                defaultValue: SoftwareQaHarness.MaxOutputTokens)
            .Number("maxQaReworkCycles", "Maximum QA rework cycles", true,
                description: "Pauses autonomous delivery after this many failed QA cycles.",
                minimum: 0, maximum: 20, step: 1, defaultValue: 3)
            .TextArea("customInstructions", "Custom instructions",
                description: "Optional QA conventions that cannot expand authority.");

    public override async Task HandleEventAsync(
        AgentEventEnvelope message, AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        if (message.EventType != WorkItemEvents.Assigned) return;
        var assigned = DeserializePayload<WorkItemAssignedEvent>(message.Data)
            ?? throw new InvalidOperationException("Assignment payload is missing.");
        if (!Guid.TryParse(context.InstallationId, out var installationId) ||
            installationId != assigned.AssignedInstallationId)
            throw new UnauthorizedAccessException("Assignment targets another installation.");

        var item = await context.Platform.Work.ReadItemAsync(
            new WorkItemReference(assigned.BoardId, assigned.ItemId), cancellationToken);
        if (item.AssignedInstallationId != installationId || item.Quality is null ||
            item.Status is WorkStatuses.Completed or WorkStatuses.Cancelled)
            return;

        try
        {
            await ExecuteAssignmentAsync(message, assigned, item, context, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "QA failed for work item {ItemId}.", item.Id);
            throw;
        }
    }

    protected override async Task<AgentWorkResult> ExecuteCapabilityCoreAsync(
        AgentCapabilityRequest request, AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        if (request.Capability != SoftwareQaProfile.PrimaryCapability)
            return AgentWorkResult.Failure($"Capability '{request.Capability}' is not supported.");
        SoftwareQaRequest? input;
        try { input = DeserializePayload<SoftwareQaRequest>(request.Arguments); }
        catch (JsonException) { return AgentWorkResult.Failure("The request payload is invalid."); }
        if (input is null || string.IsNullOrWhiteSpace(input.Repository) ||
            string.IsNullOrWhiteSpace(input.Objective) ||
            input.Requirements is not { Count: > 0 } ||
            input.AcceptanceCriteria is not { Count: > 0 })
            return AgentWorkResult.Failure(
                "repository, objective, requirements, and acceptanceCriteria are required.");
        var path = Path.GetFullPath(input.Repository);
        if (!Directory.Exists(path))
            return AgentWorkResult.Failure("The validation workspace does not exist.");
        try
        {
            var response = await RunHarnessAsync(
                path, JsonSerializer.Serialize(input), context, cancellationToken);
            return AgentWorkResult.Success(new SoftwareQaResponse(
                request.WorkId, "Reported", response, DateTimeOffset.UtcNow));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Direct QA validation failed.");
            return AgentWorkResult.Failure("Software QA could not complete validation.");
        }
    }

    private async Task ExecuteAssignmentAsync(
        AgentEventEnvelope message, WorkItemAssignedEvent assigned, WorkItem item,
        AgentRuntimeContext context, CancellationToken cancellationToken)
    {
        var quality = item.Quality!;
        var branch = quality.SourceBranch;
        await context.ReportProgressAsync(
            new { stage = "preparing", itemId = item.Id, quality.SourceCommitSha },
            cancellationToken);
        var workspace = await context.Platform.Git.PrepareAsync(
            new PrepareGitWorkspaceRequest(
                item.Id, assigned.AssignmentRevision, quality.RepositoryConnectionId,
                branch, branch, Key(message.EventId, "prepare"))
            {
                ExpectedCommitSha = quality.SourceCommitSha,
                ResumePublishedBranch = true
            }, cancellationToken);
        if (!string.Equals(workspace.CheckoutCommitSha, quality.SourceCommitSha,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Prepared workspace does not match the QA source.");

        var path = Path.GetFullPath(workspace.Path);
        var expectedRoot = Path.GetFullPath("/workspace") + Path.DirectorySeparatorChar;
        if (!path.StartsWith(expectedRoot, StringComparison.Ordinal) || !Directory.Exists(path))
            throw new InvalidOperationException("Platform returned an invalid QA workspace.");

        var prompt = BuildPrompt(message.EventId, item, quality);
        await RunHarnessAsync(path, prompt, context, cancellationToken);
        var outcome = await ReadOutcomeAsync(path, cancellationToken);
        ValidateOutcome(outcome, quality);
        var inspection = await context.Platform.Git.InspectAsync(
            new InspectGitWorkspaceRequest(workspace.WorkspaceId), cancellationToken);
        if (inspection.HasTrackedChanges)
            throw new InvalidOperationException("QA modified tracked source files.");

        var result = await context.Platform.Work.SubmitQualityAsync(
            new SubmitQualityResultRequest(
                assigned.BoardId, assigned.ItemId, assigned.AssignmentRevision,
                quality.SourceCommitSha, outcome.Verdict, outcome.Summary,
                outcome.Criteria, outcome.Validations, outcome.Findings,
                outcome.RemainingRisks, Key(message.EventId, "quality")),
            cancellationToken);
        await context.Platform.Git.CleanupAsync(
            new CleanupGitWorkspaceRequest(workspace.WorkspaceId, RetainOnFailure: false),
            cancellationToken);
        await context.ReportProgressAsync(
            new { stage = "submitted", result.QualityRunId, result.Verdict, result.PipelineStatus },
            cancellationToken);
    }

    private async Task<string> RunHarnessAsync(
        string path, string prompt, AgentRuntimeContext context,
        CancellationToken cancellationToken)
    {
        var provider = Settings.GetGuid("llmProviderId")
            ?? throw new InvalidOperationException("Configure an approved LLM provider.");
        var model = Settings.GetString("llmModel");
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Configure an approved QA model.");
        var contextTokens = Settings.GetInt32(
            "maxContextWindowTokens", SoftwareQaHarness.MaxContextWindowTokens);
        var outputTokens = Settings.GetInt32(
            "maxOutputTokens", SoftwareQaHarness.MaxOutputTokens);
        if (outputTokens >= contextTokens)
            throw new InvalidOperationException("maxOutputTokens must be less than context tokens.");
        var selection = new AgentLlmSelection(provider, model);
        var chat = _llmFactory is null
            ? context.CreateChatClient(selection)
            : await _llmFactory.CreateChatClientAsync(selection, cancellationToken);
        await using var shell = SoftwareQaHarness.CreateShell(path);
        AIAgent harness = chat.AsHarnessAgent(SoftwareQaHarness.CreateOptions(
            context.Identity?.DisplayName ?? SoftwareQaProfile.DisplayName,
            path, shell, Settings.GetString("customInstructions"), contextTokens, outputTokens));
        var session = await harness.CreateSessionAsync(cancellationToken);
        var response = await harness.RunAsync(prompt, session, null, cancellationToken);
        return response.Text ?? throw new InvalidOperationException("QA harness returned no report.");
    }

    private static string BuildPrompt(Guid eventId, WorkItem item, SoftwareQualityBrief quality) =>
        $$"""
Validate the exact assigned revision. Run real repository checks and do not modify product source.
Before finishing, write `.csweet/qa-outcome.json` with:
{"verdict":"Passed|Failed|Blocked","summary":"...","criteria":[{"criterion":"...","status":"Passed|Failed|Blocked|NotRun","evidence":"..."}],"validations":[{"command":"...","status":"Passed|Failed|Flaky|Blocked","exitCode":0,"diagnosticExcerpt":null}],"findings":[{"title":"...","severity":"Low|Medium|High|Critical","description":"...","reproductionSteps":["..."],"expectedBehavior":"...","actualBehavior":"...","evidence":"..."}],"remainingRisks":[]}
Every entry must be bounded and supported by observed evidence.
<qa_assignment>
{{JsonSerializer.Serialize(new {
    eventId, workItemId = item.Id, item.Title, item.Description,
    quality.SourceBranch, quality.SourceCommitSha, quality.PullRequestUrl,
    quality.Requirements, quality.AcceptanceCriteria, quality.Constraints,
    maximumFailureReruns = 1
}, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true })}}
</qa_assignment>
""";

    private static async Task<SoftwareQaOutcome> ReadOutcomeAsync(
        string workspace, CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(Path.Combine(workspace, ".csweet", "qa-outcome.json"));
        if (!path.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !File.Exists(path))
            throw new InvalidOperationException("QA harness did not produce its structured outcome.");
        await using var stream = File.OpenRead(path);
        var outcome = await JsonSerializer.DeserializeAsync<SoftwareQaOutcome>(
            stream, new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken)
            ?? throw new InvalidOperationException("QA outcome is empty.");
        File.Delete(path);
        return outcome;
    }

    internal static void ValidateOutcome(
        SoftwareQaOutcome outcome, SoftwareQualityBrief brief)
    {
        if (outcome.Verdict is not (QualityVerdicts.Passed or QualityVerdicts.Failed or
            QualityVerdicts.Blocked) || string.IsNullOrWhiteSpace(outcome.Summary))
            throw new InvalidOperationException("QA outcome has an invalid verdict or summary.");
        if (outcome.Criteria.Count != brief.AcceptanceCriteria.Count ||
            outcome.Validations.Count == 0)
            throw new InvalidOperationException("QA outcome does not cover every criterion.");
        if (outcome.Verdict == QualityVerdicts.Passed &&
            (outcome.Criteria.Any(x => x.Status != QualityResultStatuses.Passed) ||
             outcome.Validations.Any(x => x.Status != QualityResultStatuses.Passed ||
                                           x.ExitCode != 0) ||
             outcome.Findings.Count != 0))
            throw new InvalidOperationException("A passing QA outcome contains failed evidence.");
        if (outcome.Verdict == QualityVerdicts.Failed && outcome.Findings.Count == 0)
            throw new InvalidOperationException("A failed QA outcome requires a confirmed finding.");
    }

    private static string Key(Guid eventId, string operation) => $"{eventId:N}:{operation}";
}
