---
name: full-app-audit
description: Perform a comprehensive, evidence-backed audit of this application's architecture, security, workflows, data integrity, and runtime behavior. Use only when the user asks for a full application, security, quality, or end-to-end audit—not for a narrow bug fix or ordinary feature work.
---

# Full App Audit

Produce a prioritized audit that distinguishes verified defects from risks, design debt, and untested areas. An audit is read-only by default: do not fix findings, alter configuration, apply migrations, modify data, or submit browser actions unless the user asks for that separate work.

## Establish the audit boundary

- Read the repository `AGENTS.md` before auditing. It is the source for repository-specific commands, data-safety requirements, roles, business invariants, and verification expectations.
- State what is in scope: repository revision, roles/workflows, runtime environment, browser coverage, and whether the audit includes security, correctness, performance, accessibility, or all of them.
- Capture `git status --short` first. Preserve unrelated and untracked work; never attribute an existing change to the audit.
- Treat source inspection, a successful build, and a browser walkthrough as complementary evidence. None proves the others.

## Audit workflow

1. **Build a system map.** Identify the startup path, routes/Areas, Identity and authorization boundaries, request/response models, services, EF Core data model and migrations, background/realtime components, JavaScript/CSS, and external integrations. Trace the highest-risk user journeys end to end rather than reviewing isolated files only.
2. **Establish a safe baseline.** Run the strongest non-mutating checks supported by the repository. In this application, build the solution and inspect its warnings/errors; do not claim tests passed when no automated test project exists. Before any runtime check, verify the configured database is safe: app startup may apply migrations and seed development data.
3. **Audit access control and input handling.** For every sensitive route, API endpoint, and hub method, verify authentication, role checks, object ownership/assignment checks, antiforgery/CSRF posture, overposting controls, validation, and error handling. Include negative customer/technician ownership cases when runtime testing is permitted.
4. **Audit workflow and data integrity.** Trace service-request state transitions, cancellation cleanup, technician assignment and scheduling conflicts, quotations, invoices, payments, inventory reservations/deductions, stock movements, notifications, and chat. Check server-side enforcement, idempotency, transaction boundaries, concurrency behavior, and auditability—not just UI restrictions.
5. **Audit runtime behavior.** When a safe environment and browser tooling are available, reproduce representative public, Customer, Technician, and Administrator flows. Inspect server output, browser console, failed network requests, redirects, validation output, responsive layouts, and SignalR behavior. For Safari/WebKit verification, load `$safari-mcp` when it is available and relevant.
6. **Audit operational quality.** Look for secret exposure, unsafe configuration defaults, migration/data-loss risk, file-upload weaknesses, logging of sensitive data, performance hazards (N+1 queries, unbounded queries, large payloads), and accessibility/regression risk in altered views.
7. **Cross-check findings.** Re-read the relevant call path and remove duplicates. A source-level suspicion is not a confirmed defect until its controlling validation or missing control is evidenced. Do not infer an exploit or data loss without a reproducible path or clear proof.

## Finding standard

Report each finding with:

- **Severity:** Critical (immediate compromise/data loss), High (material authorization, financial, data-integrity, or availability risk), Medium (meaningful correctness/security/operability problem), or Low (limited defect, maintainability, or UX risk).
- **Evidence:** exact file/symbol/route, relevant conditions, and runtime evidence when available.
- **Impact:** who or what is affected and why the behavior matters.
- **Reproduction:** the smallest safe sequence, including role and record state when applicable.
- **Recommendation:** a targeted remediation direction and the verification required after it.

Keep separate sections for **verified findings**, **risks needing confirmation**, and **coverage gaps**. Do not label an area secure, performant, or tested merely because it was not exercised.

## Completion

- Lead with the highest-severity verified findings, then summarize scope, evidence, and gaps.
- Cite local files directly and avoid exposing secrets, credentials, tokens, or private customer data in the report.
- Include commands actually run and their results. Clearly state whether runtime/browser/database checks were skipped and why.
- End with a concise prioritized remediation plan, but do not implement it unless asked.
