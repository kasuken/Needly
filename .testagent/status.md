# Status

## Quality review

Verdict: **Strong** for the requested behavior. The generated xUnit tests use exact equality, positive and negative collection assertions, exception assertions, persisted state transitions, string/context assertions, timing boundaries, call counts, and cancellation side effects. No generated test is assertion-free, trivial-only, always true, or self-referential.

Pseudo-mutation review found and closed three credible gaps: risk reason selection, repeated evaluator idempotence/no FollowUp duplication, and evaluator-driven clearing below threshold. The final focused risk run passed 16 tests. The direct project and solution baselines passed before this final strengthening and are rerun after this artifact.

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| Ready-to-merge happy state using fake API | `MergeReady_AuthoritativeHappyState_CreatesMergeActionForAuthorOnly`; `GetAsync_TypedApiResponses_AggregatesLatestReviewsChecksAndMergeability` |
| Each readiness regression retracts | `MergeReady_WhenAnyConditionRegresses_ResolvesExistingAction` covers draft, approvals, changes requested, checks, mergeability, conflicts, and closed API state |
| Recovers/reactivates without duplicate | `MergeReady_AfterRegressionRecovers_ReactivatesWithoutDuplicate` |
| API insufficient state/failure behavior | `MergeReady_InsufficientApiState_ResolvesExistingAction`; `MergeReady_ApiFailure_RollsBackEventWithoutChangingExistingAction` |
| Close/merge resolves without unnecessary API | `MergeReady_PullRequestClose_ResolvesWithoutApiLookup` |
| Configurable required approval default >= 1 | `MergeReady_ConfiguredTwoApprovals_RejectsSingleApproval`; `ActionRiskOptions_Defaults_AreEightHoursThreeDaysAndFifteenMinutes` covers the other defaults |
| Typed lookup, latest reviews/checks, and cancellation | `GetAsync_TypedApiResponses_AggregatesLatestReviewsChecksAndMergeability`; `GetAsync_CheckRunsOnlyWithEmptyPendingCombinedStatus_IsPassing`; `GetAsync_PreCanceledToken_StopsBeforeApiRequest` |
| Mention exactness and case-insensitive mapping | `Respond_ExactMention_IsCaseInsensitiveWithoutPrefixMatches` |
| Author comments and no author double count | `Respond_PullRequestReviewCommentByAnotherUser_CreatesAuthorAction`; `Respond_AuthorAlsoMentioned_IsCountedOnce` |
| Own and bot comments ignored | `Respond_OwnAndBotAuthoredComments_DoNotCreateActions` independently covers GitHub Bot type and `[bot]` suffix |
| Reply after trigger resolves | `Respond_OwnReplyResolvesOnlyWhenAfterTrigger` covers equal-time non-resolution and later resolution |
| Multiple comments coalesce with durable count/context | `Respond_MultipleComments_UpdateOneActionWithDurableCount` |
| Issue and PR close resolve | `Respond_SubjectClose_ResolvesIssueAndPullRequestActions` |
| Review >8h and generic inactivity >3d before/at/after | `ReviewWaitingThreshold_BeforeAtAndAfterEightHours_UsesStrictBoundary`; `GenericInactivityThreshold_BeforeAtAndAfterThreeDays_UsesStrictBoundary` |
| Exclusion states and terminal clearing | `EvaluateAsync_NonOpenActions_AreExcludedAndRiskIsCleared` covers Snoozed, Archived, Muted, and Done |
| Risk clearing after activity and below criteria | `ApplyEvent_NewGenericActivity_ClearsRiskAndEvaluatorKeepsItClear`; `EvaluateAsync_PreviouslyAtRiskBelowThreshold_ClearsRisk` |
| Mark existing action risk without FollowUp duplicates and persist reason | `EvaluateAsync_ReviewPastEightHours_MarksExistingActionOnceWithReviewReason`; `EvaluateAsync_GenericPastThreeDays_UsesInactivityReason` |
| Deterministic background cancellation | `BackgroundService_Stop_CancelsInProgressDeterministicEvaluation` |
| Computed waiting duration projection | `GetVisibleAsync_ActiveMembership_ReturnsDirectAndTeamActionsOnlyFromThatInstallation` asserts the exact two-hour duration |

Static source pairing remains a parse-only heuristic and is not line or branch coverage evidence.# Test Generation Status

## Result

- Strategy: single pass.
- Test project: `Needly.Tests` (`net10.0`, xUnit 2.9.3).
- Test methods: 24.
- Discovered test cases: 53.
- Final result: 53 passed, 0 failed, 0 skipped through both direct and solution entry points.
- Production files changed: none.

## Completed checklist

- [x] All `ActionType` values have explicit enum and key-discriminator evidence.
- [x] All `ActionState` values have explicit enum and lifecycle evidence.
- [x] `ActionKey` is stable for the same tuple and differs by type, subject, and assignee dimensions.
- [x] `NeedlyAction` creation and event updates preserve monotonic timestamps.
- [x] Mismatched event keys are rejected without action mutation.
- [x] Snooze, archive, mute, done, and reactivation transitions are covered.
- [x] Invalid, non-GitHub, and mismatched subject deep links are rejected.
- [x] Missing, oversized, boundary-length, and normalized webhook delivery IDs are covered.
- [x] SQLite rejects duplicate webhook delivery IDs.
- [x] SQLite rejects every open/snoozed duplicate active-action-key combination.
- [x] SQLite permits archived, muted, and done historical actions alongside an active action with the same key.
- [x] The open-actions-for-user query excludes another user, a team, snoozed state, and archived state.
- [x] Direct tests, solution tests, registration, and full non-incremental build pass.

## Requirement evidence

| Requirement | Evidence |
| --- | --- |
| All `ActionType` values | `ActionType_DeclaresEverySupportedWorkType`; `Create_EveryActionType_UsesExpectedIdentityDiscriminator` (9 cases) |
| All `ActionState` values | `ActionState_DeclaresEverySupportedLifecycleState`; lifecycle theories below |
| Key stability | `Create_SameIdentityTuple_ReturnsStableCanonicalKey` |
| Key differs by type/subject/assignee | `Create_WhenActionTypeChanges_ReturnsDifferentKey`; `Create_WhenSubjectChanges_ReturnsDifferentKeys`; `Create_WhenAssigneeChanges_ReturnsDifferentKeys` |
| Action create/update and monotonic timestamps | `CreateForUser_ValidValues_InitializesIdentityContentAndOpenTimestamps`; `ApplyEvent_NewerThenOlderActivity_UpdatesContentAndKeepsTimestampsMonotonic`; `ChangeState_StaleValidTimestamp_ChangesStateWithoutDecreasingUpdatedAt` |
| Key mismatch rejection | `ApplyEvent_MismatchedKey_RejectsWithoutMutatingAction` |
| Snooze/archive/mute/done transitions | `ChangeState_NonOpenState_TransitionsAndAdvancesUpdatedAt` (4 cases) |
| Reactivation when supported | `ChangeState_PreviouslyNonOpenAction_ReactivatesAndResetsWaitingSince` (4 cases) |
| Invalid/non-GitHub/deep-link subject mismatches | `Create_InvalidOrNonGitHubLink_ThrowsArgumentException` (4 cases); `Create_SubjectIdentityMismatch_ThrowsArgumentException` (owner/repository/type/number cases) |
| RawEvent delivery identity validation | `Create_ValidDeliveryIdentity_TrimsAndRetainsIdentity`; `Create_MissingDeliveryIdentity_ThrowsArgumentException` (3 cases); `Create_DeliveryIdentityOverMaximumLength_ThrowsArgumentException`; `Create_DeliveryIdentityAtMaximumLength_IsAcceptedWithoutTruncation` |
| Duplicate webhook delivery ID rejected | `SaveChanges_DuplicateWebhookDeliveryId_ThrowsUniqueConstraintViolation` using SQLite error 19/2067 |
| Unique active action key enforced | `SaveChanges_DuplicateActiveActionKey_ThrowsUniqueConstraintViolation` (Open/Open, Open/Snoozed, Snoozed/Open, Snoozed/Snoozed) |
| Terminal historical actions allowed | `SaveChanges_TerminalHistoricalActionAndActiveActionWithSameKey_PersistsBoth` (Archived, Muted, Done) |
| Open actions for a user query | `ActionsQuery_OpenActionsForUser_ReturnsOnlyMatchingUserOpenRecords` with five persisted inclusion/exclusion records |
| In-memory SQLite relational constraints | `SqliteTestDatabase` holds `Data Source=:memory:` open and calls `EnsureCreatedAsync`; no EF InMemory provider is referenced |
| Single registered xUnit project | `Needly.Tests/Needly.Tests.csproj`; `dotnet sln .\Needly.sln list` includes `Needly.Tests\Needly.Tests.csproj` |

## Quality gates

### Pseudo-mutation review

Verdict: Strong for the requested scope. The first pass identified three candidate survivors: replacing the action-type discriminator, narrowing the maximum delivery ID boundary from 100 to 99, and allowing stale state changes to decrease `UpdatedAt`. They are killed by `Create_EveryActionType_UsesExpectedIdentityDiscriminator`, `Create_DeliveryIdentityAtMaximumLength_IsAcceptedWithoutTruncation`, and `ChangeState_StaleValidTimestamp_ChangesStateWithoutDecreasingUpdatedAt`. No caller-visible candidate remains for the requested behaviors.

### Assertion-depth review

- 24 test methods and 53 concrete discovered cases.
- 0 assertion-free methods.
- 0 trivial-only methods.
- 0 self-referential-only methods.
- Assertions cover concrete equality, negative identity, exceptions, runtime types, strings, collections, and state/side effects.
- No skips, sleeps, wall-clock reads, external URLs, or port binding occur in the suite.

## Validation

| Command | Result |
| --- | --- |
| `& 'C:\Program Files\dotnet\dotnet.exe' sln .\Needly.sln list` | `Needly.Tests\Needly.Tests.csproj` listed |
| `& 'C:\Program Files\dotnet\dotnet.exe' build .\Needly.sln --no-incremental` | Passed; all five projects succeeded |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj` | 53 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.sln` | 53 passed, 0 failed, 0 skipped |

The first full build attempt was blocked by a pre-existing running `Needly.Web` process locking its outputs. The process was stopped and the exact build command then passed cleanly.

## Issues #5 and #6 extension result

- Added test methods: 18.
- Added concrete test cases: 19.
- Final repository result: 72 passed, 0 failed, 0 skipped through direct and solution entry points.
- External GitHub calls in tests: 0; HTTP behavior uses fake `HttpMessageHandler` implementations.

| Requirement | Evidence |
| --- | --- |
| GitHub App options validate only when enabled | `Validate_DisabledWithMissingValues_Succeeds`; `Validate_EnabledWithMissingValues_FailsEveryRequiredSetting`; `Validate_EnabledWithValidValues_Succeeds`; `Validate_EnabledWithMalformedPrivateKey_FailsPrivateKeyValidation` |
| App JWT claims and RSA signature | `CreateToken_EnabledApp_ContainsExpectedClaimsAndValidSignature` |
| Token cache and expiration-aware refresh | `GetAsync_CachedThenNearExpiry_ReusesAndRefreshesToken` |
| Inactive installation refusal before HTTP | `GetAsync_InactiveInstallation_RefusesBeforeHttpRequest` for suspended and deleted states |
| Per-installation authenticated API client | `CreateAsync_Installation_ReturnsClientThatUsesInstallationBearerToken` |
| Identity persistence and update | `UpsertAsync_NewProfile_PersistsLinkedGitHubAndNeedlyUsers`; `UpsertAsync_ExistingProfile_UpdatesBothRecordsWithoutDuplicates` |
| Installation lifecycle | `Create_ValidInstallation_IsActive`; `Suspend_ActiveInstallation_IsInactiveAndAdvancesTimestamp`; `Activate_SuspendedInstallation_IsActiveAndUpdatesAccount`; `Delete_ActiveInstallation_IsInactiveAndRetainsRecord`; `HandleInstallationAsync_SuspendUnsuspendDelete_PersistsEveryStateTransition` |
| Installation account and initial repositories | `HandleInstallationAsync_Created_PersistsOrganizationAndSelectedRepositories` |
| Repository inventory add/remove | `HandleRepositoriesAsync_AddedAndRemoved_UpdatesSelectedRepositoryInventory` |
| Setup callback/webhook ordering and Settings projection | `LinkUserAsync_BeforeInstallationWebhook_RemainsAvailableToSettingsAfterCreation` |
| Auth route architecture and Settings UI | `Needly.Web/Components/App.razor`, `Routes.razor`, `RedirectToLogin.razor`, `Pages/Settings.razor`; clean Web and solution builds |
| Owner-operated registration and secrets | `docs/github-app.md`, development/production manifests, non-secret `appsettings.json`; both manifests parsed successfully |

### Extended quality review

Verdict: Strong for the requested issue #5/#6 behaviors. The new tests use equality, boolean/state, exception, string, collection, HTTP side-effect, and cryptographic signature assertions. There are no assertion-free, trivial-only, self-referential, skipped, sleeping, real-network, or real-file-I/O cases. The post-generation static pairing scan found the tested integration implementations paired directly or through their public interfaces; generated migrations, Web composition code, and trivial DI/configuration surfaces remain intentionally outside static unit pairing.

### Extended validation

| Command | Result |
| --- | --- |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj` | 72 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' build .\Needly.sln --no-incremental` | Passed; all five projects succeeded |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.sln` | 72 passed, 0 failed, 0 skipped |

## Issues #7 and #8 extension result

- Added test methods: 19.
- Added concrete test cases: 22.
- Final repository result: 94 passed, 0 failed, 0 skipped through direct and solution entry points.
- External GitHub calls in tests: 0; initial synchronization uses an installation-scoped client over a fake `HttpMessageHandler`.

| Requirement | Evidence |
| --- | --- |
| "valid signature persists/enqueues" | `IngestAsync_ValidSignature_PersistsExactPayloadAndEnqueuesEvent`; HTTP boundary: `Post_ValidThenDuplicateDelivery_ReturnsAcceptedThenOkAndEnqueuesOnce` |
| "invalid/missing signature does neither" | `IngestAsync_MissingOrInvalidSignature_DoesNotPersistOrEnqueue` (2 cases); HTTP boundary: `Post_MissingOrInvalidSignature_ReturnsUnauthorizedWithoutPersistence` (2 cases) |
| "duplicate id ack/no enqueue" | `IngestAsync_DuplicateDelivery_AcknowledgesExistingEventWithoutReenqueue`; HTTP `202` then `200` asserted by `Post_ValidThenDuplicateDelivery_ReturnsAcceptedThenOkAndEnqueuesOnce` |
| "bounded payload size" and valid delivery/event headers | `Post_PayloadOverConfiguredLimit_ReturnsPayloadTooLargeWithoutPersistence`; `Post_MissingDeliveryOrEventHeader_ReturnsBadRequestWithoutPersistence` (2 cases) |
| "unknown skip" | `DispatchAsync_UnknownEvent_MarksStoredEventSkipped` |
| Known action-event dispatch | `DispatchAsync_KnownActionEvent_InvokesActionHandlerAndMarksProcessed` proves a durable `pull_request` reaches `IGitHubActionEventHandler` |
| "retry status and restart recovery" | `DispatchAsync_TransientFailure_PersistsRetryStatusAndRequeues`; `DispatchAsync_TransientFailureAtMaximumAttempt_MarksFailedWithoutRequeue`; `RecoverAsync_PendingInterruptedAndRetryable_RepairsAndQueuesInReceiptOrder` |
| Per-repository ordering | `BackgroundService_SameRepositoryEvents_DispatchesInReceiptOrder` uses the real bounded channel, recovery service, and hosted worker |
| "installation event dispatch" | `DispatchAsync_InstallationEvent_UsesInstallationInventoryHandler` |
| "member/team/membership transitions" | `WebhookHandlers_MemberTeamAndMembership_PersistActiveAndInactiveTransitions` |
| "initial sync with fake HttpMessageHandler" | `SyncAsync_InstallationScopedApi_PersistsMembersTeamsAndTeamMembersWithoutNetwork`; pagination: `SyncAsync_MembersLinkHasNextPage_FetchesAndPersistsEveryPage` |
| "team resolution" | `ResolveAsync_RequestedInstallationTeam_ReturnsOnlyActiveMembers` also excludes an inactive member and same GitHub team ID from another installation |
| "per-installation visibility isolation" | `GetVisibleAsync_ActiveMembership_ReturnsDirectAndTeamActionsOnlyFromThatInstallation` includes direct/team routes and excludes another installation |
| Strongly typed conditional configuration | Existing `GitHubAppOptionsTests` remain green; startup registration and endpoint/worker mapping are conditional on `GitHubApp:Enabled` |
| Migration and webhook documentation | `20260902222309_GitHubWebhooksAndMemberships`; both manifests parse as JSON; `docs/github-app.md` documents secrets, status semantics, limits, retries, recovery, and subscribed events |

### Extended quality review

Verdict: Strong. Security denials assert both the HTTP result and absence of persistence/queue side effects. Idempotency, retry boundaries, restart state repair, ordered dispatch, membership transitions, active-only team resolution, and cross-installation authorization each have concrete state or collection assertions. The 18 new methods contain no assertion-free, trivial-only, self-referential, skipped, sleeping, real-network, or real-file-I/O tests. Assertion categories include equality, boolean, null, exception, collection, negative, HTTP status, state transition, and external side effect.

Pseudo-mutation candidates that remove signature rejection, enqueue duplicates, process unknown events, retry past the configured maximum, omit interrupted recovery, reorder same-repository deliveries, retain removed memberships, include inactive team members, or remove installation isolation are all killed by the tests cited above. No unaddressed high-risk caller-visible gap remains in the requested scope.

### Extended validation

| Command | Result |
| --- | --- |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj` | 94 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' build .\Needly.sln --no-incremental` | Passed; all five projects succeeded with no warnings or errors |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.sln --no-build` | 94 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' ef migrations has-pending-model-changes --project .\Needly.Infrastructure\Needly.Infrastructure.csproj` | No pending model changes |

## Issue #9 extension result

- Added test methods: 11.
- Added concrete test cases: 12.
- Final repository result: 106 passed, 0 failed, 0 skipped through direct and solution entry points.
- Concrete production detectors: 0; all detector behavior in this scope is synthetic and test-only as required.
- Missing installation, repository, or active assignee is a precise `InvalidOperationException`; update/resolve for a missing active action is an intentional no-op.

| Requirement | Evidence |
| --- | --- |
| "Application detector abstraction where a detector inspects a `GitHubStoredEvent` plus current repository/action state and emits explicit create/update/resolve operations; operation DTOs must be persistence-agnostic and deterministic." | `OperationRecords_SameInputs_HaveDeterministicValueEquality`; `HandleAsync_CreateThenUpdateThenResolve_TransitionsOneActionToDone` |
| "Infrastructure action engine replaces the no-op handler, loads the installation/repository/identity context, invokes registered detectors in deterministic order, and applies all operations transactionally." | `HandleAsync_DetectorsRegisteredOutOfOrder_InvokesByOrderThenKey`; `HandleAsync_LaterDetectorOperationFails_RollsBackEarlierActionAndReceipt` |
| "Upsert semantics guarantee one active action per (type, subject, assignee), updating matching Open/Snoozed action rather than duplicate" | `HandleAsync_TwoCreateEventsForActiveTarget_UpdatesOpenOrSnoozedWithoutDuplicate` (Open and Snoozed cases) |
| "resolved terminal actions can be reactivated by a later create/re-request operation when detector explicitly requests it." | `HandleAsync_DoneAction_ReactivatesOnlyWhenCreateExplicitlyRequestsIt` |
| "Auto-resolution operations mark Done when condition disappears/PR closes." | `HandleAsync_CreateThenUpdateThenResolve_TransitionsOneActionToDone` |
| "Reprocessing an event is idempotent even if handler is called directly or a crash happens after operations but before raw-event dispatcher status update" | `HandleAsync_SameEventTwice_InvokesDetectorAndCreatesActionExactlyOnce` asserts one invocation/action/receipt while the raw event remains Pending |
| "Preserve per-repository order from dispatcher." | Existing `BackgroundService_SameRepositoryEvents_DispatchesInReceiptOrder` remains green; the engine does not add parallel dispatch |
| "missing installation/repository/assignee behavior (skip vs precise failure documented)" | `HandleAsync_MissingInstallation_ThrowsPreciseFailureWithoutReceipt`; `HandleAsync_MissingRepository_ThrowsPreciseFailureWithoutReceipt`; `HandleAsync_MissingAssignee_ThrowsPreciseFailureWithoutActionOrReceipt` |
| "transaction rollback when a later operation fails" | `HandleAsync_LaterDetectorOperationFails_RollsBackEarlierActionAndReceipt` verifies through a fresh context |
| "cancellation" | `HandleAsync_CancelledDuringLaterDetector_RollsBackAndPropagatesCancellation` |
| "Use in-memory SQLite." | `ActionEngineTestDatabase` holds an open SQLite in-memory connection and creates independent contexts from production options |
| "Add migration." | `20260902223752_ActionEngine` creates the receipt table, unique event-detector index, and raw-event FK |

### Issue #9 quality review

Verdict: Strong. The focused suite has no assertion-free, trivial-only, self-referential-only, skipped, sleeping, network, or file-I/O tests. Assertions cover deterministic record equality, detector invocation order, exact counts, durable side effects, lifecycle state/content/timestamps, precise exceptions, negative persistence, and cancellation propagation.

Pseudo-mutations that remove receipt checks, create a duplicate active action, omit Snoozed matching, reactivate Done implicitly, skip resolution, reorder detectors, allow missing context, commit an earlier detector before a later failure, or swallow cancellation are killed by the tests cited above. No unaddressed high-risk caller-visible gap remains in issue #9 scope.

### Issue #9 validation

| Command | Result |
| --- | --- |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj --filter 'FullyQualifiedName~GitHubActionEventHandlerTests'` | 12 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' tool run dotnet-ef migrations has-pending-model-changes --project .\Needly.Infrastructure\Needly.Infrastructure.csproj --startup-project .\Needly.Infrastructure\Needly.Infrastructure.csproj` | No pending model changes |

## Issues #18 and #19 extension result

- Added concrete test cases: 37.
- Final direct test result: 213 passed, 0 failed, 0 skipped.
- Shared semantics: one persistence-neutral `ActionFilterMatcher` is used by Saved View counts, filtered Inbox results, and automation Rules.
- Schema change: `SavedViewsAndAutomationRules` migration plus designer and updated model snapshot.
- UI automation: not run because the completion constraints prohibit starting the server; Razor, CSS isolation, DI, and component contracts compile in the clean solution build.

| Requirement | Evidence |
| --- | --- |
| "one shared `ActionFilter`/matcher" | `IsMatch_EmptyFilter_MatchesAnyCandidate`; `IsMatch_EachConfiguredCriterion_MatchesExpectedCandidate` (8 cases); `IsMatch_WhenAnyConfiguredCriterionDiffers_DoesNotMatch` (8 cases); `IsMatch_OptionCollectionsUseOrAndDifferentCriteriaUseAnd`; `IsMatch_WaitingThreshold_IsInclusive`; `IsMatch_BotFilter_DistinguishesBotAndHumanActivity` |
| "per-user persistence and isolation" for Saved Views | `CreateUpdateDeleteAsync_EnforcesPerUserIsolationAndNormalizedNameUniqueness`; `MoveAsync_ReordersOnlyTheOwningUsersViews` |
| Built-in views and authorized live counts | `GetAsync_BuiltInViews_AreAlwaysAvailableWithExplicitAuthorizedCounts`; `GetAsync_CustomView_UsesSharedMatcherForAuthorizedCount` |
| "ordered all-match automation" and event idempotency | `EvaluateAsync_AllMatchingRulesExecuteInOrderAndAreIdempotentPerEvent` |
| Disabled Rules do not execute | `EvaluateAsync_DisabledMatchingRule_DoesNotExecuteOrCreateDisposition` |
| Auto-archive, mute, snooze, mark FYI, and pin effects | `EvaluateAsync_EachEffect_PersistsPerUserDispositionAndExplanation` (5 cases) |
| Bot filtering drives FYI automation | `EvaluateAsync_BotRuleMarksFyi` |
| "per-user team-safe dispositions" | `EvaluateAsync_TeamMembersReceiveIndependentEffectsAndOutsiderReceivesNone` asserts independent archive/pin outcomes and no shared action mutation |
| Rule ownership, CRUD, enable/disable, and reorder | `RuleCrud_EnableDisableDeleteAndReorder_AreUserIsolated` |
| Rules run inside the detector transaction for create/update and roll back completely | `HandleAsync_CreateAndUpdate_InvokesRulesInsideTransactionAndDoesNotRepeatEvent`; `HandleAsync_InvalidRule_RollsBackActionAndDetectorReceipt` |
| Authorized Inbox filtering, FYI projection, pin ordering, archive/snooze hiding, and cross-user disposition isolation | `GetVisibleAsync_FilterAndPerUserDispositions_ReturnsPinnedThenFyiAndHidesDeferredActions` |
| MudBlazor Inbox, Drawer, Saved View, Rule, and execution-history UI | `InboxFilterBar.razor`, `SavedViewList.razor`, `RuleList.razor`, `RuleExecutionHistory.razor`, `Views.razor`, `Rules.razor`, and `MainLayout.razor`; all compile in the clean solution build |
| Migration and operational documentation | `20260903001122_SavedViewsAndAutomationRules`; `docs/saved-views-and-rules.md`; final EF drift check reports no changes |

### Issues #18/#19 quality review

Verdict: Strong. Every independently requested criterion and effect has a concrete positive or negative assertion. The tests verify exact counts, ordering, state flags, duration, execution explanations, ownership failures, durable idempotency, rollback absence, and team-member independence. There are no skipped, sleeping, network, or file-I/O cases in the new suites. The real SQLite Inbox regression test exposed and closed an unsupported server-side `DateTimeOffset` comparison and now verifies null/active snooze behavior explicitly.

### Issues #18/#19 validation

| Command | Result |
| --- | --- |
| Focused automation and Inbox suites | 18 passed, 0 failed |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj --no-build` | 213 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' build .\Needly.sln --no-incremental` | All five projects succeeded with no warnings or errors |
| `& 'C:\Program Files\dotnet\dotnet.exe' ef migrations has-pending-model-changes --project .\Needly.Infrastructure\Needly.Infrastructure.csproj --no-build` | No pending model changes |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj` | 106 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' build .\Needly.sln --no-incremental` | Passed; all five projects succeeded |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.sln --no-build` | 106 passed, 0 failed, 0 skipped |

## Issues #10, #11, and #12 extension result

- Added test methods: 14.
- Added concrete test cases: 18.
- Final repository result: 124 passed, 0 failed, 0 skipped through direct and solution entry points.
- Concrete production detectors: 3, registered with stable unique keys and orders 100, 200, and 300.
- Durable detector-state tables: 4, added by `20260902225337_GitHubActionDetectors`.
- External GitHub calls in detector tests: 0; all payloads are deterministic synthetic JSON and persistence uses open in-memory SQLite.

| Requirement | Evidence |
| --- | --- |
| "`review_requested` for a user emits Review action for stored GitHub user; for team emits team action resolved within installation, visible to members via existing visibility service." | `ReviewRequested_DraftThenReady_CreatesUserAndTeamActionsVisibleToTeamMember`; `TeamReviewRequested_WhenActiveTeamMemberSubmits_ResolvesOnlyTeamAction` |
| "Draft requests do nothing; `ready_for_review` must create actions for currently requested reviewers from payload." | `ReviewRequested_DraftThenReady_CreatesUserAndTeamActionsVisibleToTeamMember` |
| "Resolve appropriate review action when assignee submits pull_request_review, request is removed, or PR closes/merges. Re-request after Done explicitly reactivates. WaitingSince/activity uses GitHub event timestamp/request time when available. Avoid resolving every reviewer when one submits." | `PullRequestReview_SubmittedByOneReviewer_LeavesOtherReviewerOpenThenCloseResolvesIt`; `ReviewRequest_RemovedThenRequestedAgain_ReactivatesDoneActionAtRequestTime`; `TeamReviewRequested_WhenActiveTeamMemberSubmits_ResolvesOnlyTeamAction`; `PullRequestClosedAfterReviewFeedbackAndCiFailure_ResolvesEveryActionType` |
| "a CHANGES_REQUESTED pull_request_review creates/updates one Resolve action for PR author" and "multiple feedback reviewers aggregate and resolve independently" | `ResolveFeedback_MultipleReviewers_AggregatesAndClearsEachReviewerIndependently` |
| "review APPROVED by same reviewer or review dismissal resolves/recalculates and resolves only when no outstanding changes requested" | `ResolveFeedback_MultipleReviewers_AggregatesAndClearsEachReviewerIndependently` |
| "New commits update the existing action/activity/context without creating a reviewer Review action." | `ResolveFeedback_ReviewCommentsAndNewCommit_UpdateOneResolveActionWithApproximateCount` |
| "For unresolved review-thread count: GitHub REST webhooks do not provide authoritative thread resolution state. Implement a defensible MVP approximation ... do not falsely claim exact GraphQL thread state." | `ResolveFeedback_ReviewCommentsAndNewCommit_UpdateOneResolveActionWithApproximateCount`; `docs/github-app.md` Review feedback approximation section |
| "check_suite/check_run/workflow_run associated with PR head creates/updates one Fix action for PR author" and "context aggregates durable failing check/workflow names and best run links" | `CiFailures_MultipleKindsAggregateAndPartialGreenRemainsOpenUntilEveryCheckSucceeds` |
| "failure/timed_out/cancelled/action_required/stale" | `CiFailure_EachFailureConclusion_CreatesFixAction` (5 cases) |
| "Success removes that check and resolves Fix when latest head SHA has no failures" and "partial green remains open" | `CiFailures_MultipleKindsAggregateAndPartialGreenRemainsOpenUntilEveryCheckSucceeds` |
| "pull_request synchronize changes current head and re-evaluates without duplicate" and "new SHA handling" | `CiFailure_NewHeadResolvesThenReactivatesWithoutDuplicateAndOldHeadGreenCannotResolveCurrentFailure` |
| "close/merge resolves" Review, Resolve, and Fix actions | `PullRequestClosedAfterReviewFeedbackAndCiFailure_ResolvesEveryActionType`; `PullRequestReview_SubmittedByOneReviewer_LeavesOtherReviewerOpenThenCloseResolvesIt` |
| "Ignore check events lacking associated PR/install repo or authored identity." | `Detectors_IrrelevantAndUnassociatedEventsAreIgnoredWhileMalformedPullRequestFailsPrecisely`; `CiFailure_MissingRepositoryContextOrActiveAuthorIdentity_IsIgnored`; `CiFailure_UnknownInstallationRepositoryRecord_IsIgnored` |
| "All detector keys/orders stable and unique; register in DI." | `RegisteredDetectors_HaveStableUniqueKeysAndOrders` |
| "malformed/irrelevant payload is safely ignored (but malformed known payload may fail precisely per established policy)." | `Detectors_IrrelevantAndUnassociatedEventsAreIgnoredWhileMalformedPullRequestFailsPrecisely` |
| "Add EF migration(s)." | `20260902225337_GitHubActionDetectors`; final `has-pending-model-changes` reports no changes |

### Issues #10-#12 quality review

Verdict: Strong for the requested behavior. The 14 methods contain 68 explicit assertion calls across equality, collection, string, negative, exception, and durable state/side-effect categories; there are 0 assertion-free, trivial-only, self-referential, skipped, sleeping, network, or file-I/O tests. The pseudo-mutation review identified and closed two initial candidates with `TeamReviewRequested_WhenActiveTeamMemberSubmits_ResolvesOnlyTeamAction` and `CiFailure_EachFailureConclusion_CreatesFixAction`; no unaddressed high-risk requested outcome remains.

The offline Roslyn static-pairing scan found 58 source files, 19 test files, 32 paired files, and 26 unpaired files. It pairs `GitHubActionDetection.cs`, `GitHubActionEventHandler.cs`, and `InboxVisibilityService.cs` to `GitHubActionDetectorTests.cs`, but reports the three internal detectors as unpaired because tests resolve them through DI and never name their internal types. This is a static identifier-pairing limitation, not line or branch coverage evidence.

### Issues #10-#12 validation

| Command | Result |
| --- | --- |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj --filter 'FullyQualifiedName~GitHubActionDetectorTests'` | 18 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj` | 124 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' build .\Needly.sln --no-incremental` | Passed; all five projects succeeded |
| `& 'C:\Program Files\dotnet\dotnet.exe' tool run dotnet-ef migrations has-pending-model-changes --project .\Needly.Infrastructure\Needly.Infrastructure.csproj --startup-project .\Needly.Infrastructure\Needly.Infrastructure.csproj --no-build` | No pending model changes |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.sln --no-build` | 124 passed, 0 failed, 0 skipped |

### Known limitation

Review-comment counts are an explicitly labeled approximation. REST webhooks can update counts from review-comment creation/deletion and associate comments to a durable review ID, but they cannot prove authoritative GraphQL review-thread resolution state. Exact unresolved-thread counts would require GraphQL synchronization, which is intentionally outside this no-network MVP.

## Issues #16 and #17 extension result

- Final repository result: 176 passed, 0 failed, 0 skipped through direct and solution entry points.
- Focused lifecycle result: 6 passed; focused action-engine result: 17 passed.
- Schema change: one `ActionLifecycleInbox` migration plus its designer and updated model snapshot.
- UI automation: not run because the requested completion gates explicitly prohibited starting the final server.

| Requirement | Evidence |
| --- | --- |
| "Replace placeholder Inbox with authorized real data" | `GetVisibleAsync_ActiveMembership_ReturnsDirectAndTeamActionsOnlyFromThatInstallation`; `Inbox.razor` queries only through `IInboxVisibilityService` for the authenticated Needly user |
| "Archive hides actions" and persisted undo | `ArchiveAsync_VisibleAction_PersistsUndoAndUndoRestoresOpenState` |
| "Never mutate another user's invisible action" | `ArchiveAsync_ActionVisibleToAnotherInstallationMember_DoesNotChangeAction`; `UndoAsync_AnotherUserOwnsUndo_DoesNotRestoreAction` |
| "Persist SnoozedUntil" and due boundaries | `SnoozeAsync_BeforeAndAtDeadline_ResurfacesOnlyWhenDue` |
| Significant create/re-request wakes archived or snoozed work | `HandleAsync_ArchivedAction_OnlySignificantCreateReactivatesIt`; `HandleAsync_SnoozedAction_SignificantCreateCancelsSnoozeEarly` |
| "Persist per-user+installation+subject suppression" and real undo | `MuteAsync_VisibleAction_PersistsSuppressionAndUndoDeactivatesIt` |
| Team mute remains per-user | `MuteAsync_TeamAction_HidesOnlyMutingMemberAndUndoRestoresVisibility`; `HandleAsync_TeamSuppression_CreatesActionForUnsuppressedMembersOnly` |
| "Suppress future detector creates for matching subject/assignee" | `HandleAsync_ActiveMuteSuppression_PreventsFutureCreateForSubjectAndAssignee` |
| "Publish live updates only after committed engine/lifecycle/evaluator changes" | `HandleAsync_ActionChange_PublishesOnlyAfterSuccessfulCommit`; `EvaluateAsync_RiskChange_PublishesAfterPersistenceCompletes`; lifecycle tests assert no publish for unauthorized changes and publish after persisted changes |
| Expanded repository/subject/action/wait/risk projection | `GetVisibleAsync_ActiveMembership_ReturnsDirectAndTeamActionsOnlyFromThatInstallation` asserts repository, subject, action type/state, assignee, waiting start, and exact duration |
| Keyboard j/k, Enter, e/s/m with editable-control guards and disposal | Collocated `Inbox.razor.js` plus typed `InboxKeyboardInterop`; non-incremental Web/solution builds compile the interop callback surface |
| Loading, retry error, exact empty state, grouped stable rows, GitHub new-tab action | `Inbox.razor`, `ActionGroup.razor`, and `ActionRow.razor`; Razor compilation passed in the non-incremental solution build |

### Issues #16/#17 validation

| Command | Result |
| --- | --- |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj --filter 'FullyQualifiedName~ActionLifecycleServiceTests'` | 6 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj --filter 'FullyQualifiedName~GitHubActionEventHandlerTests'` | 17 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.Tests\Needly.Tests.csproj --no-restore` | 176 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' test .\Needly.sln --no-restore` | 176 passed, 0 failed, 0 skipped |
| `& 'C:\Program Files\dotnet\dotnet.exe' build .\Needly.sln --no-restore --no-incremental` | All five projects succeeded with no warnings or errors |
| `& 'C:\Program Files\dotnet\dotnet.exe' tool run dotnet-ef migrations has-pending-model-changes --project .\Needly.Infrastructure\Needly.Infrastructure.csproj --startup-project .\Needly.Infrastructure\Needly.Infrastructure.csproj` | No pending model changes |
