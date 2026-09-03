# GitHub App setup

GitHub App registration cannot be completed from this repository because GitHub requires an owner account, public URLs, and generated credentials. Use one of the checked-in manifests as a registration template:

- `github-app-manifest.development.json` for a public HTTPS development tunnel.
- `github-app-manifest.production.json` for the production hostname.

Replace every `example.invalid` URL before submitting a manifest. The templates contain no credentials.

## URLs

For a deployment rooted at `https://needly.example.com`, configure:

| GitHub App field | URL |
| --- | --- |
| Homepage | `https://needly.example.com` |
| Callback URL | `https://needly.example.com/auth/github/callback` |
| Setup URL | `https://needly.example.com/github/setup` |
| Webhook URL | `https://needly.example.com/webhooks/github` |

Enable **Redirect on update** for the setup URL. `/github/setup` requires the Needly authentication cookie and accepts GitHub's `installation_id` and `setup_action` query parameters. `/webhooks/github` is mapped only when `GitHubApp:Enabled` is true.

## Webhook delivery behavior

GitHub must send `X-Hub-Signature-256`, `X-GitHub-Delivery`, and `X-GitHub-Event`. Needly computes HMAC-SHA256 over the exact request bytes and compares the hash in fixed time before parsing or persistence. Missing or invalid signatures return `401`; invalid headers or JSON metadata return `400`; payloads over `WebhookMaxPayloadBytes` return `413`.

Accepted deliveries are durably stored before a `202` response and signaled through a bounded in-process channel. A repeated delivery ID receives an idempotent `200` response and is not queued again. The stored record includes the raw JSON, GitHub installation and repository ordering identities, receipt/completion timestamps, processing status, attempt count, bounded error classification, and next retry time.

The background worker recovers pending, interrupted, and retryable records after restart. Events are consumed in receipt order and protected by an installation/repository ordering key. Transient HTTP and timeout failures use bounded exponential backoff up to `WebhookMaxAttempts`; unknown event names remain stored and are marked skipped. Logs include delivery/event identifiers and status only, never payloads, signatures, tokens, or secrets.

### Review feedback approximation

Needly durably tracks each reviewer's latest `CHANGES_REQUESTED`, `APPROVED`, or dismissed review state. It also maintains an approximate unresolved review-comment count from `pull_request_review_comment` create and delete webhooks while changes remain requested. GitHub's REST webhook payloads do not provide authoritative review-thread resolution state, and resolving a thread does not provide the same complete state available from the GraphQL review-thread connection. The count shown in Resolve action context is therefore explicitly labeled **approximately** and must not be interpreted as an exact count of unresolved GraphQL review threads.

### Ready-to-merge evaluation

Needly refreshes pull request, review, commit-status, and check-run state through an installation-authenticated GitHub REST client when a relevant webhook may have changed merge readiness. A Merge action is assigned only to the stored pull request author when the pull request is open and not a draft, the configured approval threshold is met, no reviewer's latest submitted review requests changes, all reported latest-head statuses and check runs are complete and successful, and GitHub reports the pull request mergeable without conflicts.

`GitHubActions:RequiredApprovals` defaults to `1` and is validated in the inclusive range `1..100`. This is a conservative repository-independent default; set it to match the repository's branch-protection policy. Incomplete API snapshots retract a Merge action. Transient HTTP failures fail the webhook transaction so the existing action state is preserved and normal webhook retry applies.

The REST lookup currently reads the first 100 reviews, commit statuses, and check runs. Repositories whose pull requests exceed one of those collections need pagination before the snapshot is authoritative. GitHub's REST responses also do not identify which checks are branch-protection requirements, so Needly requires every reported latest-head check to pass and requires at least one reported status or check run.

### Respond action coalescing

Issue comments and pull request review comments use exact GitHub `@login` tokens and case-insensitive matching against stored installation users. Mentions and comments on a stored user's own subject roll into one durable Respond action per subject and assignee, with a running activity count. A user's later comment resolves their Respond action; same-time or older activity does not. Bot-authored comments and conventional `[bot]` accounts are ignored. Closing the issue or pull request resolves its Respond actions.

## Permissions and events

Configure read-only access unless GitHub requires `metadata: read` automatically:

| Scope | Permission | Access |
| --- | --- | --- |
| Repository | Metadata | Read |
| Repository | Actions | Read |
| Repository | Contents | Read |
| Repository | Issues | Read |
| Repository | Pull requests | Read |
| Repository | Checks | Read |
| Organization | Members | Read |
| User | Email addresses | Read |

Subscribe to these webhook events:

- Installation
- Installation repositories
- Issues
- Issue comment
- Pull request
- Pull request review
- Pull request review comment
- Check run
- Check suite
- Workflow run
- Member
- Team
- Membership

The Email addresses permission allows the sign-in callback to resolve a verified email when `/user` does not expose one. Needly does not request write permissions for these workflows.

## Local secrets

The Web project has a `UserSecretsId`. From the repository root, set values generated by the GitHub App registration:

```powershell
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$project = '.\Needly.Web\Needly.Web.csproj'
$privateKey = Get-Content 'C:\secure\needly-development.private-key.pem' -Raw

& $dotnet user-secrets set 'GitHubApp:Enabled' 'true' --project $project
& $dotnet user-secrets set 'GitHubApp:AppId' '<app-id>' --project $project
& $dotnet user-secrets set 'GitHubApp:AppSlug' '<app-slug>' --project $project
& $dotnet user-secrets set 'GitHubApp:ClientId' '<client-id>' --project $project
& $dotnet user-secrets set 'GitHubApp:ClientSecret' '<client-secret>' --project $project
& $dotnet user-secrets set 'GitHubApp:PrivateKey' $privateKey --project $project
& $dotnet user-secrets set 'GitHubApp:WebhookSecret' '<generated-webhook-secret>' --project $project
```

The non-secret processing settings have defaults in `appsettings.json` and can be overridden when needed:

| Setting | Default | Purpose |
| --- | ---: | --- |
| `GitHubApp:WebhookMaxPayloadBytes` | `1048576` | Maximum raw request body size (hard validation limit: 10 MiB). |
| `GitHubApp:WebhookQueueCapacity` | `1024` | Bounded in-process channel capacity. |
| `GitHubApp:WebhookMaxAttempts` | `5` | Maximum transient processing attempts. |
| `GitHubActions:RequiredApprovals` | `1` | Minimum latest-review approvals required for a Merge action. |
| `ActionRisk:ReviewWaitingThreshold` | `08:00:00` | Review actions become at risk only after waiting more than eight hours. |
| `ActionRisk:InactivityThreshold` | `3.00:00:00` | Any open action becomes at risk only after more than three days without activity. |
| `ActionRisk:EvaluationInterval` | `00:15:00` | Period between open-action risk evaluations. |

Do not add a private key, client secret, webhook secret, or user-secrets file to source control. Use a secret manager in production and expose the same hierarchy with configuration-provider-specific names. Environment-variable providers use double underscores, for example `GitHubApp__ClientSecret`, `GitHubApp__PrivateKey`, and `GitHubApp__WebhookSecret`.

Enabled configuration is validated during startup. Disabled configuration does not require GitHub credentials.