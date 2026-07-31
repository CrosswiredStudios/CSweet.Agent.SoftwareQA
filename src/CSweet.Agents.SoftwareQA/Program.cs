using CSweet.Agent.SDK;
using CSweet.Agents.SoftwareQA;
using Microsoft.Extensions.Hosting;

if (args.Contains("--self-test", StringComparer.Ordinal))
{
    var manifest = await AgentManifestLoader.LoadAsync("csweet-plugin.json", CancellationToken.None);
    var agent = new SoftwareQaAgent();
    var schema = await new AgentTestRuntime().ExecuteCapabilityAsync(
        agent, AgentConfigurationCapabilities.Describe, new { });
    var ok = manifest.Id == agent.AgentId && manifest.Version == agent.Version &&
             manifest.Capabilities.Contains(SoftwareQaProfile.PrimaryCapability) && schema.Succeeded;
    Console.WriteLine(ok ? "Software QA manifest and configuration contract are valid." :
        "Software QA self-test failed.");
    Environment.ExitCode = ok ? 0 : 1;
    return;
}

var builder = Host.CreateApplicationBuilder(args);
var runtimeManifest = await AgentManifestLoader.LoadAsync("csweet-plugin.json", CancellationToken.None);
if (runtimeManifest.Id != SoftwareQaProfile.AgentId ||
    runtimeManifest.Version != SoftwareQaProfile.Version)
    throw new InvalidOperationException("Software QA identity does not match csweet-plugin.json.");
builder.AddCSweetAgent<SoftwareQaAgent>();
await builder.Build().RunAsync();
