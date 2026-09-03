# Research

## Target inventory

- `Needly.Application/GitHub/GitHubActionDetection.cs`: detector contracts, durable PR/review/check state, action operations.
- `Needly.Application/GitHub/GitHubServices.cs`: installation-scoped API and inbox projection contracts.
- `Needly.Domain/NeedlyAction.cs`: action lifecycle, waiting/activity timestamps, new persisted risk state.
- `Needly.Infrastructure/GitHub/GitHubActionStateStore.cs`: repository-scoped PR/review/check state; extend for response state and merge snapshot fields.
- `Needly.Infrastructure/GitHub/GitHubActionWebhookPayloads.cs`: typed webhook DTOs for PR, issue, comment, actor type, and exact mentions.
- `Needly.Infrastructure/GitHub/GitHubActionEventHandler.cs`: ordered detectors and idempotent create/update/resolve operations.
- `Needly.Infrastructure/GitHub/CiFailureActionDetector.cs`: current-head CI failure durability shared with merge readiness.
- `Needly.Infrastructure/GitHub/InboxVisibilityService.cs`: application projection and computed waiting duration.
- `Needly.Infrastructure/ServiceCollectionExtensions.cs`: detectors, options, lookup, evaluator, and hosted-service registration.
- `Needly.Infrastructure/EntityConfigurations.cs` and migrations: durable state/risk schema and indexes.
- `Needly.Tests/Infrastructure/GitHubActionDetectorTests.cs`: existing SQLite integration harness for detector transitions.
- New focused risk evaluator tests: deterministic `TimeProvider`, threshold boundaries, lifecycle exclusions, clearing, and cancellation.

## Existing conventions

- SDK-style .NET 10, nullable enabled, xUnit 2.9.3, framework assertions, SQLite in-memory integration tests.
- Async I/O accepts and forwards `CancellationToken`; infrastructure awaits use `ConfigureAwait(false)`.
- Detector operations are idempotent by stable action target and per-event detector receipts.
- The required Roslyn static pairing scan found 58 source files, 19 test files, 32 paired, and 26 unpaired. The existing detector integration suite is the established behavioral test location even though parse-only pairing does not credit several internal detector files.

## Acceptance checklist

- #13: authoritative installation API lookup uses `IGitHubApiClientFactory`, cancellation, and typed `System.Text.Json` DTOs; tests use a fake only.
- #13: persist PR open/draft/head, approvals, active changes requested, latest-head checks, mergeability/conflicts, and freshness.
- #13: create one Merge action only for a stored PR author when open/non-draft, approvals meet configurable default >= 1, checks are green, and mergeability is clean.
- #13: retract on check failure, changes request, draft, conflict, close/merge; recover/reactivate without duplicate; insufficient API state fails closed and API failures do not partially commit.
- #13: reuse current CI failure state rather than creating duplicate CI tracking; document required approval default.
- #14: exact case-insensitive mention parsing for `issue_comment` and `pull_request_review_comment`.
- #14: create/update one durable Respond action per stored mentioned user and for a stored subject author when commenter differs.
- #14: ignore bot-authored comments and bot mentioned accounts (`type=Bot` or `[bot]` suffix); avoid author double count.
- #14: own comments resolve only after triggering activity; multiple comments aggregate count/context without duplicate; issue/PR close resolves.
- #15: persist `IsAtRisk` and reason, with useful index; domain activity/terminal transitions clear risk.
- #15: strongly typed defaults: review waiting strictly over 8 hours, generic inactivity strictly over 3 days, configurable evaluation interval.
- #15: evaluator directly invokable with `TimeProvider`, evaluates only Open actions, handles before/at/after boundaries, excludes all other states, and clears recovered risk.
- #15: hosted background loop is registered, periodic, cancellation-aware, and deterministically testable.
- #15: application action projection exposes computed waiting duration.
- Validation: focused tests after each first slice, direct test project, full solution build/test, and EF no-pending-model-changes check.

Static pairing is a parse-only heuristic, not line or branch coverage evidence.# Test Generation Research

## Scope and strategy

- Strategy: single pass (multiple production files across `Needly.Domain` and `Needly.Infrastructure`).
- Production scope: `Needly.Domain` and `Needly.Infrastructure` only.
- Build entry point: `C:\_GITHUB_\Needly\Needly.sln`.
- Tooling: SDK-style projects targeting `net10.0`; invoke `C:\Program Files\dotnet\dotnet.exe` because `dotnet` is not on PATH.
- Existing tests: none. The required Roslyn static-pairing scan found 21 source files, 0 test files, and 21 unpaired files. This is a static pairing heuristic, not line or branch coverage.

## Repository conventions

- Nullable reference types and implicit global usings are enabled.
- No `Directory.Packages.props`, `Directory.Build.props`, or `global.json` exists.
- `Needly.Infrastructure` uses EF Core SQLite `10.0.11`.
- Locally available conventional xUnit packages are `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, and `Microsoft.NET.Test.Sdk` 17.14.1.
- FluentAssertions is not present and will not be introduced.
- Create one `Needly.Tests` project referencing only `Needly.Domain` and `Needly.Infrastructure`, then register it in `Needly.sln`.

## Bounded target inventory

| Target | Owned behavior |
| --- | --- |
| `Needly.Domain/ActionEnums.cs` | Complete `ActionType` and `ActionState` contracts |
| `Needly.Domain/ActionKey.cs` | Stable composite action identity |
| `Needly.Domain/NeedlyAction.cs` | Creation, event updates, timestamps, key matching, lifecycle transitions |
| `Needly.Domain/GitHubDeepLink.cs` | Canonical GitHub link and subject matching validation |
| `Needly.Domain/RawEvent.cs` | Delivery identity and receipt validation |
| `Needly.Domain/Repository.cs` | Valid repository fixture creation for action tests |
| `Needly.Domain/Users.cs` | Valid user fixtures and user-assigned action identity |
| `Needly.Infrastructure/NeedlyDbContext.cs` | Relational persistence and action query surface |
| `Needly.Infrastructure/EntityConfigurations.cs` | Filtered active-action uniqueness and webhook-delivery uniqueness |

Migrations, design-time factory, DI registration, Application, and Web are outside the behavioral scope.

## Acceptance checklist

- [ ] All `ActionType` values have explicit behavioral evidence.
- [ ] All `ActionState` values have explicit behavioral evidence.
- [ ] `ActionKey` is stable for the same tuple.
- [ ] `ActionKey` differs when type, subject, or assignee identity changes.
- [ ] `NeedlyAction` creation initializes identity, content, open state, and timestamps.
- [ ] `NeedlyAction.ApplyEvent` updates content and advances timestamps monotonically.
- [ ] `NeedlyAction.ApplyEvent` rejects a mismatched key without mutation.
- [ ] Snooze, archive, mute, and done transitions are covered.
- [ ] Reactivation to open is covered because `ChangeState(ActionState.Open, ...)` is public.
- [ ] Invalid and non-GitHub links are rejected.
- [ ] Repository owner/name, subject type, and subject number deep-link mismatches are rejected.
- [ ] `RawEvent` validates webhook delivery identity.
- [ ] SQLite rejects duplicate webhook delivery IDs.
- [ ] SQLite enforces unique active action keys for open/snoozed records.
- [ ] SQLite permits terminal archived/muted/done historical records with the same key.
- [ ] An open-actions-for-user query returns only the matching user's open user-assigned actions.
- [ ] Direct test-project and solution-level tests pass with exact discovered counts.
- [ ] Full non-incremental solution build passes.
- [ ] Final pseudo-mutation and assertion-depth reviews find no unaddressed in-scope gap.

## Commands

- Narrow: `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj`
- Registration: `& 'C:\Program Files\dotnet\dotnet.exe' sln .\Needly.sln list`
- Final build: `& 'C:\Program Files\dotnet\dotnet.exe' build .\Needly.sln --no-incremental`
- Final tests: `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj` and `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.sln`

## Issues #5 and #6 extension

- Production scope: GitHub App authentication/token infrastructure, GitHub identity persistence, installation inventory, authenticated setup/settings flow, and the existing Blazor shell.
- Static pairing baseline: 21 source files, 7 test files, 10 paired and 11 unpaired. `Installation` was only incidentally paired through persistence tests, so lifecycle behavior needs a dedicated suite. This remains a static pairing heuristic, not line or branch coverage.
- Authentication mode: globally interactive Blazor Server. Cookie challenge/sign-out and OAuth callbacks must remain HTTP endpoints; interactive components consume `AuthenticationState` only.
- Existing persistence separates `GitHubUser` and `NeedlyUser`, but installation lifecycle, user-to-installation linkage, and repository selection transitions are not modeled yet.
- Existing Settings UI is a placeholder within the IBM Plex Sans/Newsreader Needly shell and must be evolved in place.

### Acceptance checklist extension

- [ ] GitHub App options validate only when integration is enabled.
- [ ] App JWT claims, lifetime, issuer, algorithm, and RSA signature are verified.
- [ ] Installation token acquisition caches valid tokens and refreshes near expiry without external HTTP.
- [ ] Inactive installations are refused before downstream GitHub API access.
- [ ] GitHub identity persistence creates and updates linked `GitHubUser`/`NeedlyUser` records.
- [ ] Installation created/deleted/suspend/unsuspend transitions persist.
- [ ] Installation repository add/remove transitions persist selected repositories.
- [ ] Setup callback links the signed-in user and returns to `/settings` with status.
- [ ] Auth routing uses cascading auth state and `AuthorizeRouteView`; interactive components do not use `HttpContext`.
- [ ] Settings presents account, installation state, repository inventory, and GitHub configuration action responsively.
- [ ] Documentation contains no secrets and identifies owner-operated GitHub App registration, permissions, events, URLs, and local secret configuration.

## Issues #7 and #8 extension

- Production scope: signed webhook ingestion, bounded queue, durable dispatcher/recovery, installation-scoped organization/team membership, team resolution, and inbox visibility.
- Security boundary: signature verification must precede JSON parsing and all EF access; malformed signatures use the same authentication failure as mismatches.
- Persistence tests use an open in-memory SQLite connection and the production EF model.
- API synchronization tests use `IGitHubApiClient`/`IGitHubApiClientFactory` fakes backed by a fake `HttpMessageHandler`; no external network is permitted.

### Acceptance checklist extension

- [ ] Valid signature persists exact payload metadata and enqueues once.
- [ ] Invalid and missing signatures neither persist nor enqueue.
- [ ] Duplicate delivery IDs acknowledge the persisted ID without re-enqueue.
- [ ] Unknown events are marked skipped.
- [ ] Transient failures persist retry attempt/error/next-attempt state.
- [ ] Restart recovery queues pending, interrupted, and retryable stored events.
- [ ] Installation events dispatch through the issue #6 inventory handler.
- [ ] Member, team, and membership webhooks persist active/inactive transitions.
- [ ] Initial organization synchronization uses installation-scoped fake HTTP responses.
- [ ] Team review resolution returns only active team members in the requested installation.
- [ ] Inbox visibility excludes actions from another installation and inactive membership.
- [ ] Direct tests, solution build, migration consistency, and solution tests pass.

## Issue #9 extension

- Production scope: persistence-agnostic detector contracts in Application and a transactional EF Core action engine in Infrastructure.
- Existing boundary: `IGitHubActionEventHandler` is already called in per-repository receipt order by `GitHubWebhookDispatcher`; its implementation is currently a logging no-op.
- Existing model: `NeedlyAction.ApplyEvent` accepts only Open/Snoozed actions, `ChangeState` supports Done and reactivation, and SQLite partial unique indexes enforce one active action key.
- Idempotency design: one durable receipt per event and detector key, committed in the same transaction as every operation emitted for the event.
- Missing installation, repository, or assignee context will fail with a precise `InvalidOperationException`; it will not silently acknowledge and lose action work.
- The required Roslyn pairing scan was run before implementation. It is a static source-to-test heuristic, not line or branch coverage.

### Acceptance checklist extension

- [ ] Detector operations are immutable, persistence-agnostic, and deterministic.
- [ ] Registered detectors run in deterministic order.
- [ ] Create, update, and resolve operations produce the expected action lifecycle.
- [ ] Reprocessing the same event is exactly idempotent through durable event-detector receipts.
- [ ] Duplicate creates update one Open/Snoozed action rather than insert another.
- [ ] Done actions reactivate only when a create operation explicitly requests reactivation.
- [ ] Missing installation, repository, and assignee context fail precisely without partial persistence.
- [ ] A later detector or operation failure rolls back earlier action changes and receipts.
- [ ] Cancellation propagates and rolls back action changes and receipts.
- [ ] Migration, no-pending-model check, direct tests, solution build, and solution tests pass.

## Issues #10, #11, and #12 extension

- Production scope: three concrete `IGitHubActionDetector` implementations, persistence-neutral durable detector state, transactional handler integration, EF Core mappings/migration, DI registration, and feedback-thread approximation documentation.
- Event parsing: typed `System.Text.Json` webhook DTOs; irrelevant event names/actions return no operations, while malformed payloads for handled event shapes may throw `JsonException` and remain subject to the existing dispatcher failure policy.
- Persistence design: the detection context exposes an Application-layer state-store contract. Infrastructure backs it with the handler's current DbContext so state and action operations commit or roll back together without network access.
- Timestamp policy: prefer review `submitted_at`, pull request `updated_at`, and check/workflow `updated_at`/`completed_at`; fall back to the stored event receipt timestamp.
- Feedback limitation: REST review and review-comment webhooks cannot authoritatively report GraphQL review-thread resolution. The MVP tracks outstanding CHANGES_REQUESTED reviewer state and an approximate live review-comment count from create/delete events; it must be labeled as approximate.

### Acceptance checklist extension

- [ ] User and installation-scoped team review requests create one Review action for a non-draft PR.
- [ ] Team Review actions are visible to active team members through the existing visibility service.
- [ ] Draft requests do nothing; ready-for-review creates actions for all currently requested users and teams.
- [ ] Review submitted, request removed, and PR close/merge resolve only the appropriate Review action(s); one reviewer submission does not resolve another.
- [ ] A re-request after Done explicitly reactivates the Review action with the event timestamp.
- [ ] CHANGES_REQUESTED creates one Resolve action for the stored PR author and durably aggregates multiple reviewer names/counts.
- [ ] APPROVED or dismissed review clears only that reviewer's feedback and resolves Resolve only when none remain.
- [ ] New commits update the existing Resolve action/activity/context without creating a reviewer Review action.
- [ ] Review-comment create/delete events maintain a clearly approximate unresolved-comment count.
- [ ] Failing check suite/run/workflow events create one Fix action for the stored PR author and aggregate stable names/links per PR and head SHA.
- [ ] Partial green removes only the matching failure; Fix resolves only when the current head has no failures.
- [ ] Pull-request synchronize switches current head without duplicates and re-evaluates current-head failures.
- [ ] PR close/merge resolves Review, Resolve, and Fix actions.
- [ ] Check events without associated PR, installed repository context, or stored/authored identity are ignored.
- [ ] Detector keys/orders are stable and unique, and all three detectors are registered in DI.
- [ ] Malformed/irrelevant synthetic payload coverage follows the established known-payload policy.
- [ ] Migration, pending-model check, focused/direct/solution tests, and solution build pass with exact counts.

## Issues #16 and #17 extension

- Production scope: authorized inbox projection, action lifecycle persistence/orchestration, detector significance and suppression, after-commit live notifications, and decomposed interactive Blazor inbox UI.
- Lifecycle authorization must reuse active installation membership plus direct-user or active-team assignment visibility; unavailable actions return the same result regardless of whether they exist.
- Persistence requires action snooze deadlines, per-user/installation/subject/assignee suppressions, and one-shot durable undo records.
- UI state belongs to the interactive Inbox page; repeated rows and the custom snooze form are leaf components; keyboard behavior uses collocated JavaScript through a typed wrapper.

### Acceptance checklist extension

- [ ] Expanded authorized projection includes repository, subject, action, assignee/trigger, waiting, and risk data.
- [ ] Archive, snooze, mute, and undo persist and reject invisible actions.
- [ ] Due snoozes resurface; future snoozes do not; significant creates wake archived/snoozed actions while routine updates preserve user choices.
- [ ] Active suppressions prevent future detector creates for the subject and assignee.
- [ ] Engine, lifecycle, snooze, and risk changes publish only after a successful commit.
- [ ] Inbox renders grouped dense rows, exact empty state, retryable error, skeleton loading, responsive action controls, and clear GitHub deep links.
- [ ] Keyboard j/k, Enter, e/s/m ignores editable controls, maintains visible focus, and disposes module and .NET references.
- [ ] Custom snooze validates an explicit future instant; snackbar Undo calls the persisted undo service.

## Issues #18 and #19 extension

- Production scope: one Domain `ActionFilter` and matcher, per-user Saved Views and Rules persistence/services, per-user action dispositions, transactional rule evaluation, scoped circuit navigation state, and decomposed MudBlazor management/filter UI.
- Filter semantics: non-empty values within one criterion are OR; distinct criteria are AND. Matching is case-insensitive for GitHub names, waiting thresholds are inclusive, and bot involvement is explicit.
- Rule semantics: every enabled matching rule executes in user order. Effects that would corrupt shared team actions are represented by per-user dispositions; execution receipts are idempotent per action update/event and rule.
- UI mode: globally interactive Blazor Server. Pages inject server services directly; authenticated per-user navigation state is scoped, never singleton.

### Acceptance checklist extension

- [ ] Shared filter matcher covers type, state, repository, organization, author, me/team assignment, waiting threshold, bot involvement, and combinations.
- [ ] Structured versioned JSON rejects unsupported or malformed filter schemas without ad hoc parsing.
- [ ] Built-in views remain available with explicit filters and authorized open counts.
- [ ] Saved Views enforce user isolation, normalized unique names, ordered CRUD, routes, active indication, and live count refresh.
- [ ] Inbox filters only authorized projections and exposes active view/query state.
- [ ] Rules enforce user isolation, ordered all-match behavior, enable/disable, delete, reorder, and every effect.
- [ ] Rule execution is idempotent, explanatory, and transactional with detector create/update processing.
- [ ] Team actions evaluate independently per visible member and persist per-user effects.
- [ ] Rules and Views pages handle loading, empty, loaded, and error states responsively.
- [ ] Migration, direct/solution tests, non-incremental build, pending-model check, and diagnostics pass.
