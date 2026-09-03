# Plan

## Phase 1: Ready-to-merge

- Add typed merge readiness contracts/options and an installation API lookup implementation.
- Extend PR durable state without duplicating the existing current-head CI failure table.
- Add an ordered merge detector and fake-API integration tests for happy, regression, recovery, close, insufficient, and failure paths.

## Phase 2: Respond

- Extend webhook DTOs and add durable per-subject/per-user response state.
- Add exact mention parsing and an ordered Respond detector.
- Add detector tests for exactness, case-insensitive mapping, author routing, own/bot ignores, after-trigger resolution, aggregation, deduplication, and close.

## Phase 3: Waiting and stale risk

- Add action risk fields/transitions and EF index.
- Add options, directly invokable evaluator, periodic hosted service, and computed projection waiting duration.
- Add threshold boundary, action-type, lifecycle exclusion, clearing, and deterministic cancellation tests.

## Phase 4: Persistence, configuration, and validation

- Generate an EF migration and update the model snapshot.
- Document/configure required approvals and risk thresholds.
- Run focused tests, direct project tests, solution build/test, formatting/error checks, and EF pending-model verification.# Test Implementation Plan

## Phase 1: Scaffold and registration

Create the single SDK-style `Needly.Tests` xUnit project targeting `net10.0`, reference only Domain and Infrastructure, register it in `Needly.sln`, and add one concrete Domain smoke test. Immediately run the direct project test command to verify restore, compilation, discovery, and the first production reference.

## Phase 2: Domain behavior

Add focused xUnit suites for:

- exact `ActionType` and `ActionState` members;
- deterministic and discriminating `ActionKey` composition;
- `NeedlyAction` creation, monotonic event timestamps, key mismatch rejection, four terminal/deferred transitions, and reactivation;
- malformed/non-GitHub deep links plus owner, repository, subject-type, and subject-number mismatches;
- valid and invalid `RawEvent.DeliveryId` values.

Run the direct test project after this phase.

## Phase 3: Relational infrastructure behavior

Add an in-memory SQLite fixture that keeps a real connection open and creates the production schema. Test:

- duplicate `RawEvent.DeliveryId` rejection;
- active `NeedlyAction.Key` uniqueness for open and snoozed records;
- archived, muted, and done historical actions with a repeated key;
- the open-actions-for-user query against matching user, other user, team, and non-open records.

Run the direct test project after this phase.

## Phase 4: Completion gates

Run solution registration listing, full non-incremental solution build, direct project tests, and solution-level tests. Review the final source/test pairs with `test-gap-analysis` and `assertion-quality`, close any in-scope gaps, rerun validation if tests change, and record exact counts and requirement evidence in `.testagent/status.md`.

## Issues #5 and #6 extension

### Phase 5: Installation lifecycle contract

Add explicit active/suspended/deleted installation state and timestamps, then immediately run a filtered lifecycle test before opening another implementation slice.

### Phase 6: GitHub integration and identity

Implement conditionally validated options, RSA app JWT generation, typed installation-token HTTP acquisition with expiration-aware shared caching, per-installation API client creation, and GitHub identity upsert/linking. Add deterministic tests with generated RSA keys, `TimeProvider`, SQLite, and a fake `HttpMessageHandler`.

### Phase 7: Inventory and authenticated web flow

Implement installation/repository webhook DTOs and handler services, durable setup linkage, cookie plus GitHub OAuth endpoints, authorized routing, setup callback, and a query service for Settings. Do not map an installation webhook endpoint; expose the handler for issue #7.

### Phase 8: Settings, documentation, and completion gates

Replace the Settings placeholder in the existing shell, add responsive operational styles, document GitHub App manifests/configuration and local secrets, generate the EF migration, then run direct tests, solution build, and solution tests. Update `.testagent/status.md` with exact issue #5/#6 evidence.

## Issues #7 and #8 extension

### Phase 9: Signed ingestion

Add SQLite-backed ingestion tests for valid, invalid, missing, and duplicate signatures. Use a recording queue and deterministic HMAC generation; assert persisted raw payload and queue side effects independently.

### Phase 10: Dispatch and recovery

Add dispatcher tests for unknown-event skipping, transient retry state, and installation inventory dispatch. Expose restart recovery through a small testable service boundary if the hosted loop cannot be exercised deterministically without timing.

### Phase 11: Membership and visibility

Add member/team/membership transition tests, an initial sync test using a fake HTTP handler through the installation API abstraction, team resolution, and cross-installation inbox isolation.

### Phase 12: Completion gates

Generate the EF migration, verify no pending model changes, update webhook configuration/event documentation, run direct tests, a non-incremental solution build, and solution tests. Re-read assertions against every #7/#8 checklist item and append exact counts/evidence to `.testagent/status.md`.

## Issue #9 extension

### Phase 13: Detector contract

Add immutable Application records for detector context, current action and identity snapshots, and explicit create/update/resolve operations. Build `Needly.Application` immediately.

### Phase 14: Transactional action engine

Add the durable event-detector receipt entity and EF mapping, replace the no-op handler with ordered detector execution, resolve installation/repository/user/team identity context, and apply all operations inside an EF execution-strategy transaction.

### Phase 15: Focused lifecycle and failure tests

Use synthetic test-only detectors and in-memory SQLite to cover create-update-resolve, deterministic detector order, exact duplicate-event idempotency, active upsert, explicit Done reactivation, missing context failures, transaction rollback, and cancellation.

### Phase 16: Migration and completion gates

Generate the EF migration, verify no pending model changes, run the direct test project, full non-incremental solution build, and solution tests. Re-read all issue #9 assertions and append exact requirement evidence to `.testagent/status.md`.

## Issues #10, #11, and #12 extension

### Phase 17: Durable detector state contract

Extend `GitHubActionDetectionContext` with a persistence-neutral state-store contract and state records for PR identity/head, requested reviewers, reviewer feedback, and failing checks. Back it with the action handler's transactional DbContext. Build the Application project immediately after the first contract edit.

### Phase 18: Review-request detector

Implement typed pull-request/review parsing, user/team routing, draft/ready transitions, targeted resolution, explicit reactivation, timestamp selection, stable detector identity, and DI registration. Add realistic SQLite-backed tests including team visibility and independent reviewers, then run only that suite.

### Phase 19: Feedback detector

Implement durable reviewer feedback aggregation, same-reviewer approval/dismissal clearing, commit activity refresh, close resolution, and review-comment count approximation. Add multiple-reviewer and independent-resolution tests, then run only that suite.

### Phase 20: CI detector

Implement check-suite/check-run/workflow-run normalization, durable current-head failure aggregation, per-check green removal, synchronize head switching, authored-identity guards, and close resolution. Add multiple-failure, partial-green, new-SHA, irrelevant, and close tests, then run only that suite.

### Phase 21: Migration, docs, and completion gates

Generate the EF migration, document the REST-thread approximation, verify no pending model changes, run focused detector tests, all direct tests, a non-incremental solution build, and solution tests. Re-read every new assertion and append exact counts and evidence to `.testagent/status.md`.

## Issues #16 and #17 extension

### Phase 22: Lifecycle persistence and authorization

Add snooze deadlines, suppressions, durable undo records, authorized lifecycle orchestration, a deterministic due-snooze service, and focused SQLite tests for authorization, persistence, undo, and due boundaries.

### Phase 23: Engine significance and notifications

Integrate suppressions and significant create semantics into the transactional engine. Publish stateless change notifications only after successful engine/lifecycle/evaluator commits. Add focused rollback, routine/significant, and suppression tests.

### Phase 24: Inbox projection and leaf components

Expand the authorized projection, then build `ActionRow`, grouping, custom snooze dialog, and typed collocated keyboard interop bottom-up with stable responsive dimensions and action-specific accents.

### Phase 25: Page orchestration and completion gates

Replace the placeholder page with loading/error/empty/loaded states, authorized live re-query, lifecycle menus/shortcuts, and real snackbar undo. Generate the migration, run focused/direct/solution tests, non-incremental build, EF no-pending check, diagnostics, and record exact evidence in `.testagent/status.md`.

## Issues #18 and #19 extension

### Phase 26: Shared filter contract

Add one immutable persistence-neutral filter/candidate model and pure matcher in Domain. Cover every criterion, case normalization, boundaries, and AND/OR composition, then run only matcher tests.

### Phase 27: Saved Views persistence and querying

Add versioned structured serialization, Saved View entity/configuration/service, built-in definitions, authorized filtered inbox projection, user isolation, uniqueness, and ordered CRUD tests.

### Phase 28: Rules and per-user dispositions

Add Rule, RuleExecution, and ActionDisposition persistence. Implement ordered all-match evaluation with `TimeProvider`, all five effects, idempotency, authorization, team-member independence, and transactional engine integration tests.

### Phase 29: Scoped state and decomposed UI

Add scoped Saved View navigation state; build filter/editor/list/history leaf components before Inbox, Drawer, Views, and Rules orchestration. Include explicit loading, empty, error, and responsive states.

### Phase 30: Migration and completion gates

Generate the EF migration, document all-match and filter schema semantics, run focused/direct/solution tests, a non-incremental build, pending-model verification, diagnostics, and append exact test evidence and limitations to `.testagent/status.md`.
