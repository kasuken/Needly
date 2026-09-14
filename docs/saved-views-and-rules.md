# Saved Views and Rules

Saved Views and automation Rules share the same `ActionFilter` matcher. A filter can select action types, states, repositories, organizations, authors, assignment scope, minimum waiting time, bot involvement, labels, pull request draft state, pull request size, milestone, and whether the review was requested through CODEOWNERS.

## Filter semantics

- Empty option arrays match any value.
- Multiple values within one criterion use OR semantics.
- Different configured criteria use AND semantics.
- Repository, organization, author, label, and milestone comparisons are case-insensitive.
- Repository values are owner-qualified, such as `octo-org/needly`.
- `waitingAtLeast` is inclusive: an action waiting exactly the threshold matches.
- `assigneeScope` is `Any`, `Me`, or `MyTeam` relative to the current user.
- `botInvolvement` is `Any`, `OnlyBots`, or `ExcludeBots`.

### Schema version 2 criteria (issue #32)

- `labels` accepts GitHub label names on the subject; multiple values use OR semantics.
- `isDraft` is `Any`, `OnlyDrafts`, or `ExcludeDrafts`, and applies to pull requests only. Issues, and
  pull requests whose draft state is not yet known, are treated as "not a draft" for `ExcludeDrafts`
  and never match `OnlyDrafts`.
- `sizeBuckets` accepts `XS`, `S`, `M`, `L`, and `XL`, derived from a pull request's total changed
  lines (additions plus deletions): `XS` &lt; 10, `S` &lt; 50, `M` &lt; 250, `L` &lt; 1000, otherwise `XL`.
  An action whose size is not yet known (issues, or pull requests without diff stats) never matches a
  non-empty `sizeBuckets` list.
- `milestones` accepts milestone titles; multiple values use OR semantics.
- `requestedViaCodeowners` is `Any`, `OnlyRequested`, or `ExcludeRequested`. CODEOWNERS parsing is not
  yet implemented (tracked as a follow-up to issue #32), so this fact is currently always `false` for
  every action; only `Any` and `ExcludeRequested` are useful until then.

Filters are persisted as structured, versioned JSON. Version 1 has this shape:

```json
{
  "schemaVersion": 1,
  "types": ["Review", "Fix"],
  "states": ["Open"],
  "repositories": ["octo-org/needly"],
  "organizations": ["octo-org"],
  "authors": ["octocat"],
  "assigneeScope": "Me",
  "waitingAtLeast": "1.00:00:00",
  "botInvolvement": "ExcludeBots"
}
```

Version 2 adds the criteria above:

```json
{
  "schemaVersion": 2,
  "types": ["Review", "Fix"],
  "states": ["Open"],
  "repositories": ["octo-org/needly"],
  "organizations": ["octo-org"],
  "authors": ["octocat"],
  "assigneeScope": "Me",
  "waitingAtLeast": "1.00:00:00",
  "botInvolvement": "ExcludeBots",
  "labels": ["bug"],
  "isDraft": "ExcludeDrafts",
  "sizeBuckets": ["S", "M"],
  "milestones": ["v2"],
  "requestedViaCodeowners": "Any"
}
```

The serializer accepts both schema versions 1 and 2. A version 1 document deserializes with every
version 2 criterion defaulted to "no constraint" (empty arrays, `Any` enum members), and the resulting
in-memory filter is stamped with the current schema version; re-saving it (for example, editing and
updating the view or rule) upgrades its stored JSON to version 2. The serializer rejects malformed
JSON, unsupported schema versions or enum values, null collections, empty names, and non-positive
waiting thresholds. It trims names, removes case-insensitive duplicates, and stores option arrays in
deterministic order.

## Saved Views

Every user always has the built-in `Needs me`, `Needs my team`, `Waiting on others`, and `FYI` views. Custom views are private to their owner, have normalized case-insensitive unique names, and can be created, edited, deleted, and reordered.

View counts and Inbox results are calculated only from actions the current user is authorized to see through an active installation membership and direct or active-team assignment. Per-user archive, mute, and active snooze dispositions are excluded before the shared filter runs. FYI and pin dispositions are projected into the visible result, with pinned work first.

## Automation Rules

Rules are private to one user and evaluate in that user's configured order. For each created or updated action, every enabled rule whose complete filter matches executes. Evaluation does not stop after the first match, so effects accumulate in order.

Available effects are:

- `AutoArchive`: hide the action from this user's active Inbox.
- `Mute`: hide this action and suppress future matching actions for this user, subject, and assignee.
- `Snooze`: hide the action until the configured positive duration expires.
- `MarkFyi`: present the action as informational for this user.
- `Pin`: place the action before unpinned work for this user.

Rule effects are stored as per-user `ActionDisposition` records. A rule on a team-assigned action therefore changes only the matching team member's Inbox; it does not mutate the shared action or another member's outcome.

Execution records capture the rule name and order, action, event, effect, timestamp, and explanation. Their action-event-rule idempotency key prevents the same event from applying the same rule twice. Rule evaluation, dispositions, suppression, execution history, detector receipts, and action changes commit in one transaction; a failure rolls the whole event back.