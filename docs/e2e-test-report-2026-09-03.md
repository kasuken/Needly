# End-to-End Test Report - 2026-09-03

## Objective

Validate a complete first-user journey from an empty Needly database through GitHub OAuth, GitHub App installation, repository synchronization, and receipt of newly generated GitHub webhooks.

The browser portions of the flow were executed with Playwright against an authenticated GitHub session. The `kasuken/Needly` repository was used for the webhook lifecycle test.

## Environment

- Needly: local Development build
- Needly URL: `http://127.0.0.1:5084`
- Public webhook tunnel: ngrok to `http://localhost:5084`
- GitHub App: `Needly-Dev`
- GitHub account: `kasuken`
- Database: isolated `Needly.Infrastructure/needly-e2e.db`
- Historical bootstrap: disabled during the isolated webhook assertions, then enabled against the same fresh installation
- Pre-test database backup: `%TEMP%/Needly-E2E-Backups/needly-before-e2e-20260903-035528.db`

## Reset Procedure

1. Backed up the existing local database outside the repository.
2. Uninstalled the existing `Needly-Dev` GitHub App installation through Playwright.
3. Created a new isolated SQLite database.
4. Applied every EF Core migration from `InitialCreate` through `BootstrapHistoricalActions`.
5. Started Needly on port `5084`, matching the ngrok tunnel target.
6. Verified startup recovered zero durable events.

## User Flow Results

| Step | Test | Result | Evidence |
| --- | --- | --- | --- |
| 1 | Open Needly from an empty database | Passed | Protected app redirected through authentication; initial inbox contained zero actions. |
| 2 | GitHub OAuth authorization | Passed | GitHub displayed the `Needly-Dev` authorization request and returned to `http://127.0.0.1:5084`. One Needly user was persisted. |
| 3 | Disconnected Settings state | Passed | Settings displayed `No GitHub App installation linked` and an `Add installation` action. |
| 4 | Select installation owner | Passed | Playwright selected the personal `kasuken` account. |
| 5 | Install on all repositories | Passed after defect fix | GitHub returned through `/github/setup?status=installed`; installation `158646051` became active. |
| 6 | Receive installation webhook | Passed | `installation.created` was accepted, persisted, processed on attempt 1, and synchronized 216 repositories. |
| 7 | Verify repository inventory | Passed | Settings displayed 216 repositories and included `kasuken/Needly`. |
| 8 | Create a new GitHub item | Passed | Playwright created `kasuken/Needly#20`, titled `Needly E2E webhook verification 2026-09-03-0202`. |
| 9 | Receive new item webhook | Passed | `issues.opened` was accepted, persisted, passed through all action detectors, and completed on attempt 1. |
| 10 | Close the test item | Passed | Playwright closed issue #20 as cleanup. |
| 11 | Receive lifecycle webhook | Passed | `issues.closed` was accepted, persisted, passed through all action detectors, and completed on attempt 1. |
| 12 | Verify GitHub delivery response | Passed | GitHub Recent Deliveries reported HTTP `202` for `issues.closed`, completed in 0.32 seconds. |
| 13 | Verify final Needly UI | Passed | User remained signed in, installation remained active, `kasuken/Needly` remained visible, and the inbox remained empty as expected for issue open/close events without an actionable assignment. |
| 14 | Bootstrap current GitHub state | Passed | The repository-scoped worker fetched current open pull requests and issues and persisted 69 synthetic events while scanning the first 125 repositories. All 69 events completed with zero failures. |
| 15 | Verify historical data in Needly | Passed | After a Playwright reload, the inbox displayed `1 things need you` and a `Respond` action for `kasuken/stone.css` issue #2, including the original comment context and GitHub deep link. |

## Historical Bootstrap Validation

Historical bootstrap was deliberately disabled until the new-webhook assertions were complete so the event counts for that portion of the test remained isolated. It was then enabled against the same fresh user, installation, and 216-repository inventory.

At the first visible-action checkpoint:

| Metric | Value |
| --- | ---: |
| Repositories completed | 125 of 216 |
| Historical events persisted | 69 |
| Historical events completed | 69 |
| Historical events failed | 0 |
| Open actions | 1 |

Playwright observed the historical result directly in the inbox:

- Counter: `1 things need you`
- Action type: `Respond`
- Subject: `kasuken/stone.css` issue #2
- Title: `Add a form example in the demo page`
- Reason: `@JuergenGutsch added activity that needs a response.`
- Historical context: one unresolved comment, with the latest comment text shown in the action card

The visible action came from GitHub state that existed before this installation test. No new event was created to manufacture the result.

On the final Playwright recheck after restarting with the checked-in configuration, the inbox displayed `Needs me 3`. The three visible historical cards were:

- `kasuken/LearnStack` PR #31 - `feat: Donations feature with Stripe Checkout integration`
- `kasuken/stone.css` issue #2 - `Add a form example in the demo page`
- `kasuken/Passwordify` issue #2 - `Optimize the UI for Mobile`

To reduce first-run latency for large installations while retaining bounded work, the default cadence was changed from five repositories per minute to 25 repositories per 30 seconds.

## Durable State Evidence

Before historical bootstrap was enabled, the isolated database contained:

| Metric | Value |
| --- | ---: |
| Needly users | 1 |
| Active installations | 1 |
| Active installation members | 1 |
| Active repositories | 216 |
| Raw events | 3 |
| Processed events | 3 |
| Failed events | 0 |

Event records:

| Event | Action | Status | Attempts | Processed |
| --- | --- | ---: | ---: | --- |
| `installation` | `created` | 2 (`Processed`) | 1 | Yes |
| `issues` | `opened` | 2 (`Processed`) | 1 | Yes |
| `issues` | `closed` | 2 (`Processed`) | 1 | Yes |

GitHub Recent Deliveries showed the same successful flow:

- `68af8b40-a73b-11f1-958e-f83278cd7acc` - `installation.created`
- `890aab5e-a73b-11f1-90d7-f16db120256a` - `issues.opened`
- `91838260-a73b-11f1-835e-9ec347f92ffc` - `issues.closed`

## Defect Found and Fixed

The first live all-repositories installation attempt exposed a `NullReferenceException` in `InstallationInventoryService.UpsertRepositoriesAsync`.

GitHub's installation repository API returned at least one repository with `owner: null`, even though `full_name` remained populated. The wire model previously declared `owner` as non-null and repository synchronization dereferenced `owner.login` unconditionally. This permanently failed the installation event after the installation record had been created.

The fix:

- Makes `GitHubRepositoryPayload.Owner` nullable.
- Uses `owner.login` when available.
- Falls back to the owner portion of `full_name` when `owner` is missing.
- Rejects records that contain neither a usable owner nor an owner-qualified name.
- Adds a focused regression test using the live `owner: null` payload shape.

After the fix, the entire OAuth and all-repositories installation flow was repeated from a newly recreated database and completed successfully.

## Automated Validation

Command:

```powershell
dotnet test .\Needly.Tests\Needly.Tests.csproj --no-restore
```

Result:

- Build succeeded with no warnings.
- 234 tests passed.
- 0 tests failed.
- 0 tests skipped.

The focused installation inventory suite also passed 12 of 12 tests before the full run.

## Cleanup and Final State

- Test issue `kasuken/Needly#20` is closed.
- The successful `Needly-Dev` installation remains installed for `kasuken`.
- The isolated E2E database remains available for inspection.
- Historical bootstrap was re-enabled after the isolated webhook assertions and validated separately against the installed repository inventory.
- Needly is running at `http://127.0.0.1:5084` with the checked-in historical bootstrap configuration.
- The original local database backup was retained outside the repository.
- No webhook payloads, signatures, tokens, private keys, or client secrets were written to this report.

## Conclusion

The clean first-user flow is operational end to end: OAuth identity creation, installation setup callback, signed installation webhook ingestion, all-repository inventory synchronization, Settings visibility, durable processing of new GitHub activity, successful HTTP acknowledgement back to GitHub, and visible historical action creation from GitHub state that predates the installation.
