using CSweet.Agent.SDK;
using CSweet.WorkManagement.Contracts;

namespace CSweet.Agents.SoftwareQA.Tests;

public sealed class SoftwareQaAgentTests
{
    [Fact]
    public async Task ManifestAndConfigurationAreValid()
    {
        var manifest = await AgentManifestLoader.LoadAsync(
            Path.Combine(RepositoryRoot(), "csweet-plugin.json"), CancellationToken.None);
        var agent = new SoftwareQaAgent();
        var schema = await new AgentTestRuntime().ExecuteCapabilityAsync(
            agent, AgentConfigurationCapabilities.Describe, new { });

        Assert.Equal(SoftwareQaProfile.AgentId, manifest.Id);
        Assert.Equal(SoftwareQaProfile.Version, manifest.Version);
        Assert.Contains(WorkManagementCapabilityNames.ExecutionRunV1, manifest.Capabilities);
        using var document = System.Text.Json.JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "csweet-plugin.json")));
        var configuration = document.RootElement.GetProperty("configuration").EnumerateArray().ToArray();
        Assert.Equal(
            SoftwareQaHarness.MaxContextWindowTokens,
            configuration.Single(field => field.GetProperty("key").GetString() == "maxContextWindowTokens")
                .GetProperty("defaultValue").GetInt32());
        Assert.Equal(
            SoftwareQaHarness.MaxOutputTokens,
            configuration.Single(field => field.GetProperty("key").GetString() == "maxOutputTokens")
                .GetProperty("defaultValue").GetInt32());
        Assert.All(configuration.Where(field => field.GetProperty("key").GetString() is
            "maxContextWindowTokens" or "maxOutputTokens"),
            field => Assert.False(field.TryGetProperty("maximum", out _)));
        Assert.Equal(
            3,
            configuration.Single(field => field.GetProperty("key").GetString() == "maxQaReworkCycles")
                .GetProperty("defaultValue").GetInt32());
        Assert.True(schema.Succeeded);
        var describedFields = schema.Value!.Value.GetProperty("fields").EnumerateArray().ToArray();
        Assert.All(describedFields.Where(field => field.GetProperty("key").GetString() is
            "maxContextWindowTokens" or "maxOutputTokens"),
            field => Assert.True(!field.TryGetProperty("maximum", out var maximum) ||
                maximum.ValueKind == System.Text.Json.JsonValueKind.Null));
        Assert.Equal(
            ["work.item.create", "work.item.types.read.v1", "work.calendar.read.v1", "work.calendar.create.v1", "work.calendar.update.v1", "work.calendar.cancel.v1", "work.calendar.schedule.v1",
                PersonalTodoCapabilities.Read,
                PersonalTodoCapabilities.Add,
                PersonalTodoCapabilities.Reorder,
                PersonalTodoCapabilities.Requeue,
                PersonalTodoCapabilities.Claim,
                PersonalTodoCapabilities.Complete,
                PersonalTodoCapabilities.Block,
                PersonalTodoCapabilities.Release,
                PlatformCapabilities.LlmChatStream,
                WorkItemCapabilities.Read,
                GitWorkspaceCapabilities.Prepare,
                GitWorkspaceCapabilities.Inspect,
                GitWorkspaceCapabilities.Cleanup, TaskDeliveryCapabilities.Read, TaskDeliveryCapabilities.Quality, PlatformGitWorkspaceClient.SyncCapability, TaskDeliveryCapabilities.List
            ],
            manifest.Requires.Select(x => x.Name).ToArray());
        Assert.Equal(
            ["com.csweet.calendar.reminder-due.v1", PersonalTodoEvents.Available, CommunicationEvents.MessageMentioned, TaskDeliveryCapabilities.ReviewRequested],
            manifest.Events.Subscribes);
    }

    [Fact]
    public void PassingOutcomeRejectsFailedEvidence()
    {
        var brief = Brief();
        var outcome = new SoftwareQaOutcome(
            QualityVerdicts.Passed, "Looks good.",
            [new("Works", QualityResultStatuses.Passed, "Observed")],
            [new("dotnet test", QualityResultStatuses.Failed, 1, "failure")],
            [], []);

        Assert.Throws<InvalidOperationException>(
            () => SoftwareQaAgent.ValidateOutcome(outcome, brief));
    }

    [Fact]
    public void FailedOutcomeRequiresFinding()
    {
        var outcome = new SoftwareQaOutcome(
            QualityVerdicts.Failed, "Failed.",
            [new("Works", QualityResultStatuses.Failed, "Observed")],
            [new("dotnet test", QualityResultStatuses.Failed, 1)], [], []);

        Assert.Throws<InvalidOperationException>(
            () => SoftwareQaAgent.ValidateOutcome(outcome, Brief()));
    }

    [Fact]
    public void CompletePassingOutcomeIsAccepted()
    {
        var outcome = new SoftwareQaOutcome(
            QualityVerdicts.Passed, "Passed.",
            [new("Works", QualityResultStatuses.Passed, "dotnet test")],
            [new("dotnet test", QualityResultStatuses.Passed, 0)], [], []);

        SoftwareQaAgent.ValidateOutcome(outcome, Brief());
    }

    [Fact]
    public async Task Stale_review_event_reads_current_state_and_does_not_rerun_completed_qa()
    {
        var task = Guid.NewGuid(); var root = Guid.NewGuid(); var reads = 0;
        var runtime = new AgentTestRuntime().RegisterCapability<ReadTaskReviewRequest, TaskReviewResult>(TaskDeliveryCapabilities.Read, (r, _) =>
        {
            Assert.Equal(task, r.TaskItemId); reads++;
            return Task.FromResult(new TaskReviewResult(Guid.NewGuid(), task, root, Guid.NewGuid(), "Task", "Scope", ["Works"], new string('a', 40), "Merged", "Passed", null, Guid.NewGuid(), 1, 10));
        });
        await runtime.DeliverEventAsync(new SoftwareQaAgent(), TaskDeliveryCapabilities.ReviewRequested, new TaskReviewChanged(task, root, 1));
        Assert.Equal(1, reads); // No workspace/model capability was registered: either would fail the test.
    }

    [Fact]
    public async Task Wrong_source_revision_is_reported_as_blocked_and_never_passed()
    {
        var task = Guid.NewGuid(); var root = Guid.NewGuid(); var id = Guid.NewGuid(); var reports = 0;
        var review = new TaskReviewResult(id, task, root, Guid.NewGuid(), "Task", "Scope", ["Works"], new string('a', 40), "Testing", "Pending", null, Guid.NewGuid(), 1, 2);
        var runtime = new AgentTestRuntime()
            .RegisterCapability<ReadTaskReviewRequest, TaskReviewResult>(TaskDeliveryCapabilities.Read, (_, _) => Task.FromResult(review))
            .RegisterCapability<PrepareGitWorkspaceRequest, GitWorkspaceResult>(GitWorkspaceCapabilities.Prepare, (r, _) =>
                Task.FromResult(new GitWorkspaceResult(Guid.NewGuid(), task, "/workspace/test", review.RepositoryId, "InternalGit", "Task", new string('b', 40), "Ready", false)))
            .RegisterCapability<ReportTaskQualityRequest, TaskReviewResult>(TaskDeliveryCapabilities.Quality, (r, _) =>
            {
                Assert.Equal("Blocked", r.Verdict); Assert.Equal(review.CommitSha, r.CommitSha); Assert.Empty(r.Validations);
                Assert.Contains("does not match", r.Summary); reports++;
                return Task.FromResult(review with { Status = "ChangesRequested" });
            });
        await runtime.DeliverEventAsync(new SoftwareQaAgent(), TaskDeliveryCapabilities.ReviewRequested, new TaskReviewChanged(task, root, 1));
        Assert.Equal(1, reports);
    }

    private static SoftwareQualityBrief Brief() => new(
        Guid.NewGuid(), new string('a', 40), "GitHub", "PullRequest",
        new Uri("https://github.com/example/repo/pull/1"),
        ["Requirement"], ["Works"], 1, 3);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "CSweet.Agents.SoftwareQA.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Root not found.");
    }
}
