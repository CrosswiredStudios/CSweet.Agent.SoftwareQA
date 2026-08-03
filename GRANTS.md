# Software QA grants

The manifest requests only an approved model, assigned-item read, orchestration execution, and
read-only lifecycle access to the pinned source workspace. Repository credentials and Git metadata
remain outside the agent runtime.

| Capability | Scope | Purpose |
|---|---|---|
| `platform.llm.chat-stream.v1` | organization | Run the QA harness |
| `work.execution.run.v1` | orchestration attempt | Validate the exact assigned QA stage and return evidence without transitioning the card |
| `work.item.read` | work item | Read the assigned ticket and quality brief |
| `git.workspace.prepare.v2` | work item | Materialize the exact assigned commit without credentials or `.git` metadata |
| `git.workspace.inspect.v2` | work item | Detect source changes through the trusted GitHost boundary |
| `git.workspace.cleanup.v2` | work item | Dispose of the isolated workspace |

The package requests no item creation, transition, sprint, push, merge, deployment, release,
credential, database, Docker, or unrestricted-network authority.
