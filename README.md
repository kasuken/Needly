# Needly

<div align="center">
  <img src="Needly.Web/wwwroot/imgs/logo-with-text.png" alt="Needly" width="360" />
</div>

<h1 align="center">The Action Inbox for GitHub</h1>

<p align="center">Turn GitHub activity into focused work: what needs your attention, why it matters, and what to do next.</p>

<p align="center"><a href="#getting-started">Get started</a> · <a href="#github-app-integration">Connect GitHub</a> · <a href="#architecture">Architecture</a> · <a href="#development">Development</a></p>

Needly is a .NET 10 Blazor Web App for GitHub teams. It receives GitHub webhooks, turns relevant activity into durable actions, and presents an inbox organized around decisions and outcomes rather than an undifferentiated stream of notifications.

> [!NOTE]
> Needly is under active development. GitHub integration is disabled by default, and a fresh clone can be built and run locally without GitHub credentials.

## Why Needly?

GitHub produces events. Teams need a clear queue of work. Needly bridges that gap by creating actions such as **Review**, **Respond**, **Fix**, **Resolve**, and **Merge**, then applying visibility, risk, and lifecycle rules for each user.

## Features

- **Action Inbox**: group and filter actionable work across pull requests and issues.
- **GitHub App integration**: use installation-scoped access and signed webhooks instead of personal access tokens.
- **Action detection**: identify review requests, unresolved feedback, CI failures, response-worthy mentions, and pull requests ready to merge.
- **Durable event processing**: persist webhook deliveries before acknowledging them, deduplicate delivery IDs, preserve ordering, retry transient failures, and recover after restart.
- **Historical bootstrap**: import existing open work after an installation is connected, with resumable repository-level progress.
- **Saved Views**: create reusable filters for action type, state, repository, organization, author, assignee, waiting time, and bot involvement.
- **Automation Rules**: automatically pin, archive, mute, snooze, or mark matching actions as FYI.
- **Team-aware visibility**: distinguish work assigned directly to you from work assigned to your teams.
- **Risk and lifecycle controls**: flag stale work, snooze actions, archive completed attention, and keep an undo history where appropriate.

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Git](https://git-scm.com/downloads)
- PowerShell on Windows, or a shell capable of running the commands below

Clone the repository and enter its directory:

```powershell
git clone https://github.com/kasuken/Needly.git
cd Needly
```

Restore, build, and run the test suite:

```powershell
dotnet restore
dotnet build .\Needly.sln --no-restore --no-incremental
dotnet test .\Needly.sln --no-restore --no-build
```

Apply the existing EF Core migrations, then start the web application:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef database update `
	--project .\Needly.Infrastructure\Needly.Infrastructure.csproj `
	--startup-project .\Needly.Infrastructure\Needly.Infrastructure.csproj

dotnet run --project .\Needly.Web\Needly.Web.csproj
```

Open the URL printed by ASP.NET Core. In the default Development profile, the SQLite database is stored at `Needly.Infrastructure/needly.db`.

> [!IMPORTANT]
> Migrations are explicit operations. Needly does not call `EnsureCreated` or apply migrations during application startup.

## Using the application

With GitHub integration disabled, the application can be used to verify the local shell and database setup. To connect GitHub:

1. Register a GitHub App using the development or production manifest in `docs/`.
2. Configure the App ID, slug, client credentials, private key, and webhook secret through user secrets or environment variables.
3. Apply database migrations and start the Web project.
4. Sign in at `/auth/login` and install the App for a personal account or organization.
5. Select the repositories Needly should watch in **Settings**. Historical bootstrap begins in the background while new webhooks continue through the same action pipeline.

The main routes are:

| Route | Purpose |
| --- | --- |
| `/` or `/inbox` | Review open actions and apply lifecycle controls |
| `/views` | Manage saved filters and built-in team views |
| `/rules` | Manage ordered, per-user automation rules |
| `/settings` | Link GitHub installations and select repositories |

## GitHub App integration

Needly requests read-only access for repository metadata, Actions, Contents, Issues, Pull requests, Checks, organization members, and user email addresses. It subscribes to installation, repository, issue, comment, pull request, review, check, workflow, member, team, and membership events.

Webhook requests are verified with HMAC-SHA256 before parsing or persistence. Accepted deliveries are stored durably, acknowledged with `202`, and processed by bounded background workers. Duplicate delivery IDs are idempotent; unknown event names are retained and marked skipped.

Configure secrets with user secrets for local development:

```powershell
dotnet user-secrets set 'GitHubApp:Enabled' 'true' --project .\Needly.Web\Needly.Web.csproj
dotnet user-secrets set 'GitHubApp:AppId' '<app-id>' --project .\Needly.Web\Needly.Web.csproj
dotnet user-secrets set 'GitHubApp:AppSlug' '<app-slug>' --project .\Needly.Web\Needly.Web.csproj
dotnet user-secrets set 'GitHubApp:ClientId' '<client-id>' --project .\Needly.Web\Needly.Web.csproj
dotnet user-secrets set 'GitHubApp:ClientSecret' '<client-secret>' --project .\Needly.Web\Needly.Web.csproj
dotnet user-secrets set 'GitHubApp:PrivateKey' '<private-key>' --project .\Needly.Web\Needly.Web.csproj
dotnet user-secrets set 'GitHubApp:WebhookSecret' '<webhook-secret>' --project .\Needly.Web\Needly.Web.csproj
```

See [docs/github-app.md](docs/github-app.md) for callback and webhook URLs, manifests, permissions, event behavior, environment-variable names, and production guidance.

> [!WARNING]
> Never commit a private key, client secret, webhook secret, or user-secrets file. Use a secret manager in production. GitHub integration validates its configuration at startup when enabled.

## Configuration defaults

The defaults live in `Needly.Web/appsettings.json` and can be overridden through the normal ASP.NET Core configuration providers.

| Setting | Default | Description |
| --- | ---: | --- |
| `GitHubActions:RequiredApprovals` | `1` | Latest approvals required before a Merge action can be created |
| `GitHubHistoricalBootstrap:MaxRepositoriesPerBatch` | `25` | Repositories imported per background batch |
| `GitHubHistoricalBootstrap:BatchInterval` | `00:00:30` | Delay between bootstrap batches |
| `GitHubApp:WebhookQueueCapacity` | `1024` | In-process webhook queue capacity |
| `GitHubApp:WebhookMaxAttempts` | `5` | Maximum transient processing attempts |
| `ActionRisk:ReviewWaitingThreshold` | `08:00:00` | Review wait time before it is marked at risk |
| `ActionRisk:InactivityThreshold` | `3.00:00:00` | Inactivity time before an open action is marked at risk |

## Architecture

Needly is split into focused .NET projects:

```text
Needly.Web             Blazor UI, authentication, routes, and HTTP endpoints
Needly.Application     Application contracts and use-case services
Needly.Domain          Actions, filters, rules, users, installations, and invariants
Needly.Infrastructure  EF Core persistence, GitHub clients, detectors, workers, and migrations
Needly.Tests            xUnit tests for domain, infrastructure, web, and GitHub behavior
```

The runtime flow is:

```text
GitHub App -> signed webhook endpoint -> SQLite RawEvent
																			-> background dispatcher
																			-> action detectors and rules
																			-> Action Inbox
```

SQLite is the default persistence provider. The Infrastructure project owns the EF Core `DbContext`, design-time factory, and migrations.

## Development

Create a migration from the repository root with the Infrastructure project as both project and startup project:

```powershell
dotnet tool restore
dotnet tool run dotnet-ef migrations add <MigrationName> `
	--project .\Needly.Infrastructure\Needly.Infrastructure.csproj `
	--startup-project .\Needly.Infrastructure\Needly.Infrastructure.csproj
```

Run focused tests with the usual xUnit filters, or run the complete suite with `dotnet test .\Needly.sln`. The test project includes deterministic coverage for action detectors, webhook verification and recovery, GitHub API clients, persistence, saved views, rules, onboarding, and authentication.

## Product and implementation notes

- Saved Views and Rules share one versioned `ActionFilter` contract. Read [docs/saved-views-and-rules.md](docs/saved-views-and-rules.md) for filter semantics, effects, ordering, and team behavior.
- Merge readiness is intentionally conservative: incomplete API snapshots retract a Merge action, and the current REST lookups are limited to the first 100 reviews, statuses, and check runs. Details and caveats are documented in [docs/github-app.md](docs/github-app.md).
- Resolve action context reports an approximate unresolved review-comment count because GitHub REST webhook payloads do not expose authoritative GraphQL review-thread resolution state.

## Documentation

- [GitHub App setup and webhook behavior](docs/github-app.md)
- [Saved Views and automation Rules](docs/saved-views-and-rules.md)
- [Product direction](docs/productidea.md)

## Local database

Local development uses EF Core 10 with SQLite. The Development configuration points the Web host and the Infrastructure design-time factory at the same database:

```text
Needly.Infrastructure/needly.db
```

The relative `Data Source=../Needly.Infrastructure/needly.db` override in `Needly.Web/appsettings.Development.json` is resolved from the Web project's content root. Other environments should provide `ConnectionStrings__Needly` explicitly.

Restore the repository-local EF tool and create or apply migrations from the repository root:

```powershell
& 'C:\Program Files\dotnet\dotnet.exe' tool restore
& 'C:\Program Files\dotnet\dotnet.exe' tool run dotnet-ef migrations add <MigrationName> --project .\Needly.Infrastructure\Needly.Infrastructure.csproj --startup-project .\Needly.Infrastructure\Needly.Infrastructure.csproj
& 'C:\Program Files\dotnet\dotnet.exe' tool run dotnet-ef database update --project .\Needly.Infrastructure\Needly.Infrastructure.csproj --startup-project .\Needly.Infrastructure\Needly.Infrastructure.csproj
```

Database migrations are explicit development and deployment operations. The application does not call `EnsureCreated` or apply migrations during startup.

## GitHub App integration

Needly uses cookie authentication plus the GitHub App user authorization flow. GitHub integration is disabled by default, so a fresh clone starts without credentials. Registration, public callback URLs, and secrets remain owner-operated.

See [docs/github-app.md](docs/github-app.md) for the required permissions, webhook events, development and production manifests, callback URLs, and local/production secret configuration.

Action behavior defaults are configured in `appsettings.json`: one approval is required for Merge actions, Review actions are marked at risk after more than eight hours waiting, and all open actions are marked at risk after more than three days without activity. See the GitHub App guide for override keys and readiness limitations.

After configuring a GitHub App and applying database migrations, sign in at `/auth/login`. The post-install setup URL returns to `/github/setup`, which links the installation to the signed-in Needly user and redirects to `/settings`.

After an installation is linked, Needly gradually bootstraps actions from the installation's current open pull requests and issues. The bootstrap persists synthetic events through the same durable processing pipeline used by webhooks, so existing review requests, unresolved feedback, failed checks, and conversations appear without waiting for new GitHub activity. Settings shows progress while this import is running and removes the notice after all selected repositories have been checked. The bootstrap is repository-scoped and resumable; by default, the worker processes up to 25 repositories per 30-second batch and reads at most ten pages from each GitHub endpoint. Configure these limits under `GitHubHistoricalBootstrap`, or set `Enabled` to `false` to disable backfill.

## First-run onboarding

The first authenticated session opens a five-step MudBlazor wizard. It explains how Needly turns GitHub activity into actions, how to connect repositories, what the built-in focus sections mean, and how Saved Views, Rules, and action lifecycle controls shape the Inbox. The final step links directly to Settings or the Inbox.

Completing or explicitly skipping the introduction stores a completion timestamp on the Needly user, so the wizard does not reopen on later sessions or other devices.

## Saved Views and Rules

Saved Views and automation Rules use one versioned filter contract. Views filter the authorized Inbox and provide live open counts; Rules apply ordered, per-user effects as GitHub events create or update actions. See [docs/saved-views-and-rules.md](docs/saved-views-and-rules.md) for filter semantics, effects, ordering, team behavior, and persistence details.