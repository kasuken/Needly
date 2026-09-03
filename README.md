# Needly

Needly is an action inbox for GitHub teams.

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

## Saved Views and Rules

Saved Views and automation Rules use one versioned filter contract. Views filter the authorized Inbox and provide live open counts; Rules apply ordered, per-user effects as GitHub events create or update actions. See [docs/saved-views-and-rules.md](docs/saved-views-and-rules.md) for filter semantics, effects, ordering, team behavior, and persistence details.