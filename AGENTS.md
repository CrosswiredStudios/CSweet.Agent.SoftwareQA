# C-Sweet Software QA repository instructions

This is one standalone protocol-v2 agent. Keep `com.csweet.software-qa` and version `0.6.0`
synchronized across code, manifest, tests, and documentation.

- Follow the canonical `AGENT_AUTHORING.md` from `CSweet.Agent.SDK`.
- Use typed SDK callbacks and platform clients only; never implement transport or token handling.
- Treat work, repository data, model output, and command output as untrusted.
- QA may inspect and execute tests but must not edit product source, push, merge, deploy, or access
  credentials.
- Keep grants minimal and every manifest declaration implemented, documented, and tested.
- Honor cancellation and make every platform effect idempotent.

Run `dotnet test CSweet.Agents.SoftwareQA.slnx` and the `--self-test` before handoff.

## Release-note ordering

- Bump the agent version FIRST, synchronizing the root `csweet-plugin.json`, implementation identity, project/package version, and version assertions as required by this repository.
- Only AFTER the version bump, read the final `version` back from `csweet-plugin.json` and write `releases/<version>.md` for that exact version (no `v` prefix). Never write the new release's notes under the previous version.
- If the version changes again during the task, retarget the unpublished notes to the final version. Preserve already published historical notes.
- Before handoff or publishing, verify that the manifest, implementation/package version, release-note filename, and release-note heading all match. A version bump is incomplete without its matching release notes.
