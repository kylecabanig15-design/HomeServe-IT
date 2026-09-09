# Phases 8–10 implementation and verification

## Responsive and accessible UI

- Admin, Customer, and Technician layouts share an off-canvas mobile navigation pattern below 1024px. It has a labelled 44px trigger, backdrop, Escape support, focus return, focus containment, and inert page content while open. Header search and long profile labels collapse on narrow screens.
- Main content can shrink to the remaining viewport. Tables and wide data grids scroll inside labelled regions instead of forcing page-level horizontal overflow. Interactive controls use larger mobile targets and motion respects `prefers-reduced-motion`.
- Dialogs receive unique accessible names, initial focus, Tab/Shift+Tab containment, Escape closing, and focus return. Existing form labels and placeholder-only controls receive accessible associations at runtime. Table sorting uses valid column-header semantics and named controls.
- Registration has one `h1`, an accessible password visibility control, and an honest review step instead of a non-functional verification-code UI. Dead service links lead to registration; unimplemented Pricing/Testimonials navigation was removed. The data-use page clearly identifies the app as a university prototype and asks users to use fictional data.
- Playwright and axe exercise the public pages and authenticated role shells at 390px, 768px, and 1440px in Chromium and WebKit. Test-only screenshots are written to ignored `App_Data` for manual review; they contain only disposable fixture data.

## Reporting and background behavior

- `CompletionTiming` defines the current metric as elapsed calendar days from scheduled service to completion because the schema has no actual-start timestamp. Invalid or future completion dates are excluded from averages. Reports label the metric accordingly.
- All EF writes now reject a Completed service whose completion timestamp is missing, before its scheduled time, or in the future. Admin, Technician, and Customer completion actions also reject a future-scheduled job before changing state.
- `--check-completion-dates` is a read-only maintenance command that reports request IDs requiring source-record review. It never guesses or rewrites historical dates.
- Notification feed GET/render paths are read-only. A controlled background worker refreshes active users every 30 seconds in bounded batches. Per-recipient serializable transactions and a unique `(RecipientUserID, SourceKey)` index make synchronization idempotent and safe across concurrent worker instances.
- Non-Development startup no longer invokes migrations. `--initialize-database` is the explicit state-changing migration/role initialization path. Development retains automatic initialization for local work. The HTTP-only Development launch profile no longer invokes HTTPS redirection without an HTTPS listener.

## Commands and limitations

```bash
# Read-only date audit against the deliberately configured database.
dotnet run --project HomeServeIT.Web --no-build --no-launch-profile -- --check-completion-dates

# State-changing initialization: review the target and migrations first.
dotnet run --project HomeServeIT.Web --no-build --no-launch-profile -- --initialize-database
```

- Browser verification uses `npm run test:phases`; it owns a randomly named disposable local MySQL database, runs Chromium/WebKit with generated credentials, stops its server, and drops its database in `finally`.
- Axe finds common machine-detectable accessibility issues; it does not replace keyboard, screen-reader, zoom, contrast, or user testing. The included checks cover primary pages and representative dialogs, not every possible state of every modal.
- Screenshots are a manual comparison aid, not committed pixel baselines. Dynamic dates, charts, and platform fonts make a strict pixel gate unnecessarily brittle at present.
- The pre-existing migration-order blocker remains: `IncreaseStatusMaxLength` alters `SupportTickets` before a later migration creates the table. Current-model fixtures cannot prove clean migration replay. Historical migrations were preserved under repository instructions; repairing or baselining that chain remains the highest-priority database task.
- Background refresh is polling, not a durable queue. A production system should emit notification events in each committed workflow and use a durable/outbox-backed processor if delivery guarantees matter.

## Verification record — September 8, 2026

- `dotnet build HomeServeIT.slnx --no-restore`: passed with 0 warnings and 0 errors.
- `dotnet test HomeServeIT.slnx --no-restore` with the local disposable MySQL connection enabled: 37 passed, 0 failed, 0 skipped. This includes provider-specific concurrency plus application-settings persistence, audit, and stale-write tests.
- `npm run test:phases`: 58 passed across Chromium and WebKit. The run covered 390px, 768px, and 1440px authenticated shells; all primary role pages; public login, registration, home, and data-use pages; persisted settings and registration shutdown; representative keyboard-operated dialogs; console/page errors; page-level overflow; and serious/critical axe findings. The runner deleted its disposable database.
- The run generated 18 ignored screenshots (three roles × three widths × two engines). Representative screenshots spanning every role and every target width were manually reviewed from `App_Data`. They are evidence for this run, not stable visual-regression baselines.
- `dotnet list HomeServeIT.slnx package --vulnerable --include-transitive`: no known vulnerable NuGet packages from the configured source.
- `npm --prefix HomeServeIT.Web audit`: 0 vulnerabilities.
- `git diff --check`: passed. Unrelated tracked changes and untracked files were left in place.
- A clean migration replay was repeated against a uniquely named local disposable database. It failed, as expected, when `IncreaseStatusMaxLength` attempted to alter `SupportTickets` before that table existed. The disposable database was then deleted. This remains an explicit failed final-verification gate rather than being hidden by the current-model fixture.
