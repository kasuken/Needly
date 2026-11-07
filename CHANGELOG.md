# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Notes for releases up to v0.0.10 are on [GitHub Releases](https://github.com/kasuken/Needly/releases).

## [Unreleased]

## [0.0.13] - 2026-11-07

### Fixed

- The app no longer crashes and restarts when a single background job or webhook event fails. A failed webhook event whose save left pending changes behind used to throw again while recording the failure, which stopped the host; recovery then replayed the same event on every start. The periodic risk, snooze and follow-up jobs and the webhook consumer now log failures and carry on.

### Changed

- The landing page and README now say that access is per user (each teammate signs in with GitHub) and that sign-in is unavailable until a GitHub App is configured.

## [0.0.12] - 2026-11-07

### Added

- Security headers on every response: Content-Security-Policy (per-request nonce, same-origin scripts, no framing), `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy` and `Cross-Origin-Opener-Policy`.
- Per-client-IP rate limits on `/auth/login`, `/github/setup` and `/webhooks/github` (20, 30 and 600 requests per minute by default, configurable under `RateLimiting`). Excess requests get `429` with `Retry-After`.

### Changed

- HSTS `max-age` is one year instead of the 30-day default.

### Fixed

- Closed and merged pull requests no longer stay in the Inbox. Late events (reviews, comments, check runs) can no longer reopen their actions, and rows left open by earlier versions are hidden.

## [0.0.11] - 2026-10-03

### Added

- AGPL-3.0-only license, Contributor License Agreement, and community health files (shared from [kasuken/.github](https://github.com/kasuken/.github)).
- In-app "Source code" link, configurable through `SourceCodeUrl`.
- Bug report form, CODEOWNERS, Dependabot configuration, `.editorconfig`, `.gitattributes`, and third-party notices.

### Changed

- Releases use the shared kasuken release workflow: run **Release** on `main` with a version bump (or publish a GitHub release). It waits for CI, builds once, runs migrations, deploys, smoke tests, and only then tags the release; `redeploy` rolls back to an earlier tag.

### Security

- Removed GitHub App development credentials from `appsettings.Development.json`. Configure them with user secrets.

### Removed

- Internal product and test report documents, and IDE and agent working files.

[Unreleased]: https://github.com/kasuken/Needly/compare/v0.0.12...HEAD
[0.0.12]: https://github.com/kasuken/Needly/compare/v0.0.11...v0.0.12
[0.0.11]: https://github.com/kasuken/Needly/compare/v0.0.10...v0.0.11
