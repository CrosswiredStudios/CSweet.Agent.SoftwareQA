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
        Assert.True(schema.Succeeded);
        Assert.Equal(
            [
                PlatformCapabilities.LlmChatStream,
                WorkItemCapabilities.Read,
                WorkItemCapabilities.QualitySubmit,
                GitWorkspaceCapabilities.Prepare,
                GitWorkspaceCapabilities.Inspect,
                GitWorkspaceCapabilities.Cleanup
            ],
            manifest.Requires.Select(x => x.Name).ToArray());
        Assert.Equal([WorkItemEvents.Assigned], manifest.Events.Subscribes);
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

    private static SoftwareQualityBrief Brief() => new(
        Guid.NewGuid(), "main", "csweet/ticket", new string('a', 40),
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
