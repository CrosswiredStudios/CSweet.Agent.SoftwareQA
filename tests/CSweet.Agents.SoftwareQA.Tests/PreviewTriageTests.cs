using CSweet.Agent.SDK;
using CSweet.WebHost.Contracts;
namespace CSweet.Agents.SoftwareQA.Tests;
public sealed class PreviewTriageTests
{
    [Theory]
    [InlineData("", "Investigate")]
    [InlineData("Reproduce", "")]
    public void Defect_assessment_requires_reproduction_and_acceptance(string reproduction, string acceptance) =>
        Assert.Throws<InvalidOperationException>(() => SoftwareQaAgent.ValidateAssessment(new(true,"Failure","Observed failure",reproduction,acceptance)));
    [Fact]
    public async Task Already_filed_finding_never_calls_a_model_or_creates_another_ticket()
    {
        var finding = Guid.NewGuid(); var project = Guid.NewGuid(); var board = Guid.NewGuid(); var ticket = Guid.NewGuid();
        var runtime = new AgentTestRuntime().RegisterCapability<PreviewFindingRequest, PreviewFindingDetail>(WebPreviewTriageCapabilities.ReadFinding,
            (input, token) => Task.FromResult(new PreviewFindingDetail(finding, Guid.NewGuid(), project, Guid.NewGuid(), Guid.NewGuid(),
                "fingerprint", new(Guid.NewGuid(), Guid.NewGuid(), project, Guid.NewGuid(), new string('a',40), "app", "runtime", "Crash", "Observed failure", DateTimeOffset.UtcNow, false), board, ticket)));
        var result = await runtime.ExecuteCapabilityAsync(new SoftwareQaAgent(), WebPreviewTriageCapabilities.Triage, new PreviewTriageAssignment(finding, project, board));
        Assert.True(result.Succeeded); // No model configuration or other capability exists in this test runtime.
    }
    [Fact]
    public async Task Assignment_cannot_redirect_evidence_to_a_different_project()
    {
        var finding = Guid.NewGuid(); var project = Guid.NewGuid(); var board = Guid.NewGuid();
        var runtime = new AgentTestRuntime().RegisterCapability<PreviewFindingRequest, PreviewFindingDetail>(WebPreviewTriageCapabilities.ReadFinding,
            (input, token) => Task.FromResult(new PreviewFindingDetail(finding, Guid.NewGuid(), project, Guid.NewGuid(), Guid.NewGuid(),
                "fingerprint", new(Guid.NewGuid(), Guid.NewGuid(), project, Guid.NewGuid(), new string('a',40), "app", "runtime", "Crash", "Observed failure", DateTimeOffset.UtcNow, false), board, null)));
        var result = await runtime.ExecuteCapabilityAsync(new SoftwareQaAgent(), WebPreviewTriageCapabilities.Triage, new PreviewTriageAssignment(finding, Guid.NewGuid(), board));
        Assert.False(result.Succeeded);
    }
}
