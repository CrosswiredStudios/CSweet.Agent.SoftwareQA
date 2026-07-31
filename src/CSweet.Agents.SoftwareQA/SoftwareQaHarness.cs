using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;

namespace CSweet.Agents.SoftwareQA;

internal static class SoftwareQaHarness
{
    internal const int MaxContextWindowTokens = 128_000;
    internal const int MaxOutputTokens = 16_000;

    internal static HarnessAgentOptions CreateOptions(
        string name, string workspace, LocalShellExecutor shell, string? customInstructions,
        int contextTokens = MaxContextWindowTokens, int outputTokens = MaxOutputTokens)
    {
        var instructions = SoftwareQaProfile.SystemPrompt;
        if (!string.IsNullOrWhiteSpace(customInstructions))
            instructions += $"\n<installation_instructions>\n{customInstructions.Trim()}\n</installation_instructions>";
        var options = new HarnessAgentOptions
        {
            Id = SoftwareQaProfile.AgentId,
            Name = name,
            Description = "Validates an immutable software revision in a confined workspace.",
            MaximumIterationsPerRequest = 40,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions,
                Tools = [shell.AsAIFunction("execute_validation_command",
                    "Run an inspection, restore, build, test, static-analysis, headless E2E, or local Git command.",
                    requireApproval: false)]
            },
#pragma warning disable MAAI001
            FileAccessStore = new FileSystemAgentFileStore(workspace),
#pragma warning restore MAAI001
            DisableAgentModeProvider = true,
            DisableAgentSkillsProvider = true,
            DisableFileMemory = true,
            DisableToolAutoApproval = true,
            DisableWebSearch = true
        };
#pragma warning disable MAAI001
        options.MaxContextWindowTokens = contextTokens;
        options.MaxOutputTokens = outputTokens;
#pragma warning restore MAAI001
        return options;
    }

    internal static LocalShellExecutor CreateShell(string workspace) =>
        new(new LocalShellExecutorOptions
        {
            WorkingDirectory = workspace,
            ConfineWorkingDirectory = true,
            CleanEnvironment = true,
            Environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["PATH"] = Environment.GetEnvironmentVariable("PATH"),
                ["HOME"] = "/tmp/csweet-qa",
                ["DOTNET_CLI_HOME"] = "/tmp/csweet-qa/dotnet",
                ["NUGET_PACKAGES"] = "/tmp/csweet-qa/nuget",
                ["HTTP_PROXY"] = Environment.GetEnvironmentVariable("HTTP_PROXY"),
                ["HTTPS_PROXY"] = Environment.GetEnvironmentVariable("HTTPS_PROXY"),
                ["ALL_PROXY"] = Environment.GetEnvironmentVariable("ALL_PROXY"),
                ["NO_PROXY"] = Environment.GetEnvironmentVariable("NO_PROXY"),
                ["CI"] = "true"
            },
            Timeout = TimeSpan.FromMinutes(15),
            MaxOutputBytes = 128 * 1024,
            AcknowledgeUnsafe = true,
            Policy = new ShellPolicy(
                denyList:
                [
                    @"(^|[;&|]\s*)(sudo|su|doas|mount|umount|nsenter|docker|podman|kubectl)\b",
                    @"(^|[;&|]\s*)(ps|top|htop|pstree|lsof|env|printenv)\b",
                    @"(^|[\s""'])(/proc|/sys|/dev|/run/secrets)(/|[\s""']|$)",
                    @"\bgit\s+(push|rebase|reset\s+--hard|merge|cherry-pick)\b",
                    @"\bgit\b.*\s(--force|-f|--delete)\b",
                    @"(^|[;&|]\s*)(shutdown|reboot|kill|pkill|killall)\b"
                ], allowList: null, custom: null)
        });
}
