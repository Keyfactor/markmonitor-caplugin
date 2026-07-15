# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Fixed

- `GetSingleRecord` no longer crashes on every call (`Convert.ToDateTime` was called on an `int`).
- Enrollment failures now surface MarkMonitor's real error detail instead of a generic message.
- Orders created via `Enroll` now correctly reach `EndEntityStatus.EXTERNALVALIDATION` instead of
  `INITIALIZED`, so a pending order no longer reports as a hard enrollment failure ([#2](../../issues/2)).
- `Cancel`/`Revoke` now send a valid JSON body (`{}`) instead of an empty string, which MarkMonitor's
  API rejected.
- Token refresh no longer throws `InvalidOperationException`; the client now tracks token expiry and
  re-authenticates automatically instead of relying on every call site authenticating first.
- A single authenticated client is now reused for the life of a plugin instance instead of a new one
  (and a fresh authentication round-trip) being built on every `Enroll`/`Revoke`/`Ping`/`GetSingleRecord`
  call.
- `OrgId` now resolves correctly when configured as a GUID, not just a friendly name.
- A single order with no `cert` details yet no longer aborts the rest of that page during
  `Synchronize`.
- `RenewOrReissue` enrollments now revoke the prior certificate (via `PriorCertSN`) instead of always
  performing a plain new enrollment.
- Enrollment requests are now deduplicated for a short window, so a Command retry after a lost
  response no longer creates a duplicate MarkMonitor order.
- Fixed a handful of smaller correctness issues: query string values are now URL-encoded, subject DN
  parsing uses a proper X.509 parser instead of naive string-splitting, `AdditionalEmails` parsing no
  longer produces empty entries, and the `X-API-KEY` header is no longer duplicated on repeated
  authentication.

### Security

- API error responses, full CSRs, and requester email addresses are no longer logged at Trace level;
  removed a debug `Console.WriteLine` that ran on every order creation.
- The MarkMonitor Base URL must now be `https://` - plaintext `http://` is rejected.
- Deleted a stale, unused `packages.config` referencing vulnerable dependency versions
  (`Newtonsoft.Json` 12.0.3, `BouncyCastle` 1.8.9); the actual build already used patched versions.
- Pinned `System.Runtime.Caching` to override a vulnerable transitive `System.Drawing.Common`
  dependency.
- Added `.gitignore` rules for local setup scripts and sandbox test fixtures that carried live-looking
  credentials and org/contact data.

### Added

- A dedicated xUnit test project (`markmonitor-caplugin.Tests`) covering the fixes above.
- A `justfile` with ad-hoc MarkMonitor order lookup/cancel/revoke recipes for manual cleanup.
- `TestConsole` now cancels every order it creates by default (opt out with
  `MARKMONITOR_SKIP_CLEANUP=true`), so repeated manual test runs don't accumulate billable orders.
