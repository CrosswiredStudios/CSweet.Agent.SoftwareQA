# C-Sweet Software QA

First-party protocol-v2 quality agent. It validates the exact PR commit assigned by C-Sweet,
traces evidence to acceptance criteria, and submits a structured `Passed`, `Failed`, or `Blocked`
verdict to the deterministic delivery coordinator.

The agent never receives repository credentials or Git metadata, edits product source, fixes
defects, pushes, merges, or deploys. C-Sweet owns source materialization, defect creation, rework
routing, governed merge, ticket completion, and
sprint sequencing.

`maxQaReworkCycles` defaults to 3 and is configurable from 0 through 20 in installation settings.

## Build and test

```powershell
dotnet test CSweet.Agents.SoftwareQA.slnx
dotnet run --project src/CSweet.Agents.SoftwareQA -- --self-test
```

Built with `CSweet.Agent.SDK` 3.1.1 and manifest protocol v2.
