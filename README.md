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

Built with `CSweet.Agent.SDK` 3.40.0, `CSweet.WorkManagement.Contracts` 3.20.0, and manifest protocol v2. The manifest declares the
canonical `software-qa` role category; testing and release-validation specializations are
preferences, not eligibility requirements.

## Release notes

See [versioned release notes](releases/README.md). Add the matching note with every agent version change.


## Business calendar

Requests business-scoped calendar read, create, update, cancel, and scheduling access. Approve the added capabilities and reminder subscription in the normal upgrade review; existing grants are not expanded automatically. Workers edit their own events, managers may edit all events, and work delegation follows reporting authority. Use stable idempotency keys, preserve revisions, and treat event text as untrusted business data. Typed operations are available through `context.Platform.Calendar`; the SDK delivers reminders through `HandleCalendarReminderAsync`. Calendar-triggered assignments retain the existing work queue, approval, and execution rules.

Preview triage (0.8.0): configure an owner-assigned project/board triage route in Web Previews, grant this employee `web-preview.finding.read.v1`, `web-preview.finding.ticket.v1`, and the ordinary board-scoped `work.item.create` and `work.item.types.read.v1` capabilities. The callback reads retained evidence, uses a bounded model call with no tools, and creates a planning ticket with copied evidence. It cannot deploy or alter product source. Informational output does not automatically become a defect ticket. Clone CSweet.WebHost.Contracts beside this repository to build without publishing packages.
