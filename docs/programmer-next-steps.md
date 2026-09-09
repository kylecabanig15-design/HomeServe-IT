# What to do next as the programmer

Start by learning to explain and test one complete workflow. You do not need another framework or a long list of new features before your university demonstration.

## 1. Establish a repeatable baseline

From the repository root:

```bash
dotnet restore HomeServeIT.slnx
dotnet build HomeServeIT.slnx --no-restore
dotnet test HomeServeIT.slnx --no-restore
```

Then supply `HOMESERVE_TEST_CONNECTION` through your local secret configuration for a **local disposable MySQL server** whose account can create/drop test databases:

```bash
dotnet test HomeServeIT.slnx --no-restore
cd HomeServeIT.Web
npm install
npx playwright install
npm run test:phases
```

The browser runner generates its own database name and password and cleans up that database. Do not paste secrets into documentation, screenshots, commits, or chat. Without the MySQL variable, provider-specific .NET tests skip; a green result with skips is not a MySQL concurrency check. Ordinary `npm test` can start the app against its configured database, so prefer the disposable runner while learning.

Your first deliverable: a short text file recording the command, date, passed/failed/skipped counts, and one thing each suite does **not** cover.

## 2. Follow one request through the code

Use the Customer profile editor as your first exercise:

1. Find its form in `Areas/Customer/Views/ProfileAndSettings/Index.cshtml`.
2. Follow `UpdateProfile` into the controller and `ProfileViewModel`.
3. Step through `AccountProfileService.UpdateAsync` with a debugger.
4. Identify where Identity, Customer data, and the transaction are updated.
5. Reload the browser. Explain why the saved value survives and why a forged `Role=Administrator` field does not grant access.

Do this using fake data. Learn the difference between a failed field validation, an authorization rejection, a database exception, and a JavaScript exception. They require different fixes.

## 3. Practice the red–green testing loop

Spend three short sessions on this rather than watching tutorials without writing code.

**Session A — unit tests.** Read `CompletionTimingTests.cs`. Add a test for a missing completion date or an exactly-zero duration. Understand Arrange (data), Act (call), and Assert (expected result). Temporarily introduce a tiny error in the function, confirm the test fails, then restore the correct implementation. Never leave the deliberate bug in your changes. The project uses xUnit v2; follow the matching [xUnit introduction](https://xunit.net/docs/getting-started/v2/netcore/cmdline).

```bash
dotnet test HomeServeIT.Web.Tests --filter FullyQualifiedName~CompletionTimingTests
```

**Session B — integration tests.** Read `AccountProfileServiceTests.cs` and `MySqlConcurrencyTests.cs`. Add an invalid-password or email-collision case and verify both Identity and domain rows after rejection. Test failure/rollback, not only success. SQLite is useful for fast checks but does not reproduce every MySQL locking behavior. For a later improvement, learn ASP.NET's test-host approach and add real HTTP authorization tests using [Microsoft's integration-testing tutorial](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0).

**Session C — browser tests.** Read `lifecycle-uploads.spec.js` and `responsive-accessibility.spec.js`. Add a keyboard-only interaction, then run it in both configured browsers. Prefer selectors based on roles and labels, and assertions about saved outcomes—not just whether a button was clicked. Use the [Playwright debugging guide](https://playwright.dev/docs/debug) to learn the Inspector and breakpoints. Debug only against an explicitly provisioned fixture; authenticated traces can contain passwords, setup tokens, and account data.

## 4. Test like someone trying to break the workflow

For each important feature, write a small checklist:

- Happy path: the correct role completes a valid action.
- Wrong role and wrong owner: the operation is rejected even if the URL/ID is known.
- Invalid input: empty values, overlong text, special characters, and malformed files.
- Retry: double-click, refresh after submitting, repeat the same request.
- Concurrency: two independent connections attempt the same stock or assignment update.
- Failure: all related records roll back together.
- Browser: check console errors, failed network requests, focus, narrow-screen overflow, and visible feedback.

The [EF Core concurrency documentation](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) explains why detecting conflicts and handling retries are separate responsibilities. Do not assume an availability check performed before a write makes that write safe.

## 5. Resolve the database-history blocker before calling setup reproducible

The existing migration chain cannot create a clean database: `IncreaseStatusMaxLength` changes `SupportTickets` before the table exists. Current-model fixtures intentionally bypass migrations. Passing those tests does **not** prove a deployment can upgrade a database.

Your next database task is to reproduce that failure on a disposable database, compare the migration order and actual schema, and propose a reviewed repair/baseline strategy. Do not delete migration history or rewrite applied migrations just to make the error disappear. Keep the migration repair separate from UI edits. Study [applying EF Core migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying), including deployment scripts/bundles and their review requirements.

Normal non-Development app startup no longer initializes the schema. `--initialize-database` is an **explicit state-changing** migration/role-initialization command, not a workaround for broken history; use it only after checking the target database and reviewing the migration plan.

To identify completion-date problems without changing records, with a deliberately selected connection:

```bash
dotnet run --project HomeServeIT.Web --no-build --no-launch-profile -- --check-completion-dates
```

Investigate reported IDs using source records. Do not invent completion timestamps. The reported duration is scheduled-to-completion calendar time, not actual technician labor time.

## 6. Prepare your university demonstration

Use fictional data and demonstrate three roles. Show one successful workflow and one rejected action. Explain the difference between a local invoice marked paid and a real payment integration. Explain that this prototype does not verify an emailed code. Keep a list of known limitations beside your demo script.

Run keyboard checks on every screen you plan to demonstrate: Tab/Shift+Tab, Enter/Space, Escape, and focus returning after a dialog closes. Check 390, 768, and 1440 pixel widths. Automated axe checks help, but cannot establish complete accessibility; combine them with hands-on checks as described in [Playwright accessibility testing](https://playwright.dev/docs/accessibility-testing) and the [WAI modal-dialog pattern](https://www.w3.org/WAI/ARIA/apg/patterns/dialog-modal/).

## 7. Build a small, maintainable backlog

In priority order:

1. Fix and verify clean migration replay and upgrades.
2. Review any unfinished Phase 2–4 security, payment, state-transition, and audit requirements against actual tests; phase numbers alone are not evidence of completion.
3. Expand authenticated HTTP tests and responsive/accessibility coverage to every demo workflow and open dialog, including SignalR ownership and reconnection.
4. Add CI that restores/builds, runs .NET tests against a disposable MySQL service, and runs Chromium/WebKit fixtures. Store credentials only in CI secrets; retain sanitized reports.
5. Add a reviewed approach to file cleanup, data retention, and backup/restore before using real data. Keep this a university prototype until those decisions are made.

Before each small change: record expected behavior, add a failing test, implement the fix, run focused tests, run the broader suite, and review the diff. Commit only the files you understand and intend to include. Do not stage this entire already-dirty worktree in one unexplained commit.
