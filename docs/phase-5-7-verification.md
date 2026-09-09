# Phases 5–7 implementation and verification

## Changes

- Typed Customer/Technician profile forms persist allowlisted Identity and domain fields in one transaction. Admin editors and public registration use the same account/profile service. Email collisions and failed password/role updates roll back. Technician role changes with active assignments are rejected.
- Customer and Technician invitations create their domain profile immediately, without a shared password. The administrator receives a private, single-use Identity password-setup link; this is not an email-delivery integration. Normal startup no longer rewrites demo accounts, jobs, dates, or stock history.
- Uploads have a 5 MiB file limit and a 5 MiB + 256 KiB request limit. Extension/MIME/signature checks and decoding reject malformed input. Images are bounded to 16 million pixels and re-encoded as PNG; deliverable PDFs are parsed, limited to 100 pages, and served as attachments. Validation is not antivirus scanning or PDF sanitization.
- New files are stored under ignored `HomeServeIT.Web/App_Data/private-uploads`. Database failures remove staged files. Downloads verify the authenticated role and request ownership/assignment. Legacy `/uploads/...` requests are routed through the same checks. Existing legacy files are not moved or deleted: deployment proxies must not serve that directory independently of ASP.NET authorization.
- Quotation search and modal data, service-request drawers, and relevant administrator action buttons use Razor-encoded data attributes/JSON instead of embedding stored strings in Alpine expressions. Local jQuery loads before unobtrusive validation.

## Disposable fixture commands

Run a solution build first. Supply `HOMESERVE_TEST_CONNECTION` from a secret store for a **local disposable MySQL server account with create/drop database permission**. Never use a shared/staging/production server.

```bash
dotnet build HomeServeIT.slnx --no-restore
dotnet test HomeServeIT.slnx --no-restore
cd HomeServeIT.Web
npm run test:phases
```

The runner creates a random `homeserve_test_browser_*` database, generates a password in memory, starts an isolated local app, runs Chromium and desktop WebKit checks, stops its server, and drops only its fixture database in `finally`. It does not reset the ordinary application database. Logs and generated fixture upload files remain in ignored `App_Data`; treat these as local artifacts. Automated browser evidence for authenticated lifecycle tests disables screenshots, video, and traces.

For an explicitly chosen repeatable local fixture, set `ASPNETCORE_ENVIRONMENT=Development`, `ConnectionStrings__DefaultConnection` to a local database named `homeserve_test_<name>`, and `TestData__Password` to an Identity-compliant secret. These commands **delete that selected database and its data**:

```bash
# Current-model fixture only; not a migration replay.
export TestData__UseCurrentModel=true
dotnet run --no-build --no-launch-profile -- --reset-test-data
dotnet run --no-build --no-launch-profile --urls http://localhost:5160
# After stopping the fixture app:
dotnet run --no-build --no-launch-profile -- --delete-test-data
```

Fixture accounts are `admin@test.invalid`, `customer@test.invalid`, `other@test.invalid`, and `technician@test.invalid`. All use the supplied test-only password. The fixture has a scheduled diagnostic request and quotation containing quotes, a backslash, a newline, Unicode, and HTML-like text. Reset options reject non-Development, non-local, and incorrectly named databases before deletion.

## Verification and limitations

Verified locally on 2026-09-08: NuGet restore and non-incremental solution build succeeded with zero warnings/errors; all 31 xUnit tests passed with the MySQL fixture enabled (zero skips); all 24 Chromium/WebKit browser checks passed. NuGet transitive and full web-project npm advisory scans reported zero known vulnerabilities. `git diff --check` passed. Temporary fixture databases were dropped after verification; unrelated work was preserved and nothing was staged or committed.

- Solution build, xUnit regression suite, and opt-in provider concurrency tests are run separately from browser verification.
- The isolated Playwright suite checks profile persistence, collision/overposting rejection, Customer/Technician invitation setup, quotation filtering/review, local form validation, successful private image upload, owner/technician/admin access, anonymous/non-owner rejection, and malformed/MIME-mismatched/oversized HTTP uploads. Console/page errors and HTTP 5xx responses are monitored on the profile, login/registration and quotation paths.
- Additional administrator-page and Customer drawer checks reproduced and fixed null `drawerRequest.id` bindings in hidden forms; the final browser run passed with these paths included.
- Dependency scans use `dotnet list HomeServeIT.slnx package --vulnerable --include-transitive` and `npm audit` from the web project. They report known advisories, not a security guarantee.
- **Existing migration-history blocker:** clean replay fails at `20260901222409_IncreaseStatusMaxLength` because it alters `SupportTickets` before the later migration creates that table. Historical migrations were not rewritten. The browser fixture uses guarded `EnsureCreated` to verify the current MySQL model, not migration replay; a clean database upgrade/replay gate remains unresolved.
- Phase 10 subsequently removed the Development HTTP-profile HTTPS warning and made non-Development migration initialization explicit; see `phase-8-10-verification.md`.
- Existing accounts with missing historical domain records are not bulk-repaired; new account paths and accounts saved through the profile service create the required records.
