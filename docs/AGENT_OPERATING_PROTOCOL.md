# Agent Operating Protocol

## Purpose

This document is the shared contract for the MusicHub engineering team. Subagents have independent contexts and cannot speak to one another directly. The orchestrator is the communication hub and persists accepted findings in shared documentation.

## Ownership and Isolation

- Work only in assigned paths (see `AGENT_OWNERSHIP.md`).
- Read anything required for analysis.
- Do not edit another agent's production files. Report a required cross-owner change to the orchestrator.
- Avoid simultaneous edits to shared files (`Program.cs`, solution files, root configuration, shared contracts). Serialize through the orchestrator.
- Use small, verifiable increments. Preserve unrelated work.

## Mandatory Report Format

Return this exact structure:

```text
Task ID:
Objective:
Assigned files:
Dependencies:
Implementation summary:
Files created or modified:
Commands executed (exact):
Test results (command + observed result):
Discovered bugs:
Unresolved issues:
Security implications:
Next actions:
```

Use status labels `VERIFIED`, `IMPLEMENTED_NOT_TESTED`, `BLOCKED`, or `NOT_IMPLEMENTED`. Never use `VERIFIED` without reproducible evidence.

## Review Rules

- Code review and security audit agents are read-only against production implementation.
- Findings must include: ID, severity (Critical/High/Medium/Low/Informational), file, line, explanation, reproduction, expected, actual, suggested fix.
- QA agents may write tests and reports but must not change production code to conceal a failure.
- Runtime QA must operate the real application and capture console/network/API evidence.

## Definition of Done for a Feature

1. Implementation accepted by code review.
2. Security review completed where relevant.
3. Automated tests added and passing.
4. Runtime verification performed where environment permits.
5. Documentation/API contract updated.
6. Orchestrator acceptance recorded.

## Secrets

Never write real secrets to the repository, reports, browser storage, logs, or test artifacts. Use environment variables and placeholder values in examples. Redact tokens in evidence.
