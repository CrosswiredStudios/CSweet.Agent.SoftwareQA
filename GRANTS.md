# Software QA grants

The manifest requests only an approved model, assigned-item read, orchestration execution, and
read-only lifecycle access to the pinned Git workspace. Repository credentials remain brokered.

| Capability | Scope | Purpose |
|---|---|---|
| `platform.llm.chat-stream.v1` | organization | Run the QA harness |
| `work.execution.run.v1` | orchestration attempt | Validate the exact assigned QA stage and return evidence without transitioning the card |
| `work.item.read` | work item | Read the assigned ticket and quality brief |
| `git.workspace.prepare.v1` | work item | Check out the exact approved commit |
| `git.workspace.inspect.v1` | work item | Detect tracked source changes |
| `git.workspace.cleanup.v1` | work item | Dispose of the isolated workspace |

The package requests no item creation, transition, sprint, push, merge, deployment, release,
credential, database, Docker, or unrestricted-network authority.
