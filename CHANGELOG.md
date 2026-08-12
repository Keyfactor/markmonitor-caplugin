# Changelog

All notable changes to this project will be documented in this file.

## [Unreleased]

### Added

- The MarkMonitor client now retries idempotent GETs, authentication, and cancel/revoke calls up to
  3 times on a network failure/timeout or an HTTP 5xx/429 response, with jittered exponential
  backoff (honoring `Retry-After` on 429). The order-create POST and reissue PATCH are deliberately
  excluded - retrying either risks creating a duplicate, billable MarkMonitor resource on an
  ambiguous failure.
- A new `TimeoutSeconds` CA connection field (default 120) sets the HTTP request timeout for calls
  to the MarkMonitor API.
- `Synchronize` now isolates per-record failures (a bad status string, bad date, or null field on
  one order is logged, counted, and skipped instead of aborting the entire sync), with an error-rate
  circuit breaker that aborts the sync outright if more than 25% of records fail once at least 50
  have been observed. Unchanged orders are now skipped rather than re-emitted on every sync
  (bypassed by the new `ForceCompleteSync` connection field, or Command's own full-sync flag). A new
  `PageSize` connection field (default 100, clamped to 1-500) replaces the hardcoded sync page size.
- A new `RenewalWindowDays` template parameter (default 90) gates whether a `RenewOrReissue`
  enrollment revokes the certificate it's replacing - only when that certificate's resolvable
  expiration falls within the window. A prior certificate with substantial life left outside the
  window is left unrevoked, and the enrollment behaves like a plain new issuance instead.
- `Enroll` now polls a freshly-created order for issuance (new `PickupRetries`/`PickupDelaySeconds`
  connection fields, defaults 5/10s) instead of always returning it in MarkMonitor's initial pending
  state - a product whose DCV/approval resolves quickly can now come back from the enroll call
  already issued rather than only picking up the certificate on the next sync. Polling happens
  inside the enrollment dedup reservation, so a concurrent duplicate request folded into it gets the
  polled result too. `PickupRetries=0` restores the original always-pending behavior.

### Fixed

- `ValidateCAConnectionInfo` now places a live call to MarkMonitor (authenticate, then list one
  organization) after its existing field checks pass, using a transient client built from exactly
  the credentials being saved - a wrong API key or password is now caught at connector-save time
  instead of surfacing only on the first real enroll/revoke/sync. Failures are summarized
  ("authentication failed" / "listing organizations failed") rather than forwarding the raw HTTP
  response. `ValidateProductInfo` now also rejects a `ProductID` that doesn't parse to a
  `CertOrderTypes` member, listing the valid values.
- Multi-SAN enrollments now issue with every requested DNS SAN instead of just the CN - `Enroll`'s
  `san` dictionary (and any SAN extension embedded directly in the submitted CSR) is now wired into
  the MarkMonitor order's `dnsNames` field. MarkMonitor issues CN ∪ `dnsNames` and does not honor a
  CSR's own SAN extension as authoritative, so previously a multi-SAN request silently issued
  CN-only. Non-DNS SAN types (IP/email/URI) have no field in MarkMonitor's order schema and are
  dropped with a logged warning rather than failing the enrollment.

### Breaking Changes

- Removed the `CertificateValidityInYears`, `Email`, and `OrganizationName` template enrollment
  parameters. They were surfaced in Command's UI but never actually read anywhere - MarkMonitor's
  API has no field to wire them up to - so setting them silently did nothing. If a template
  referenced these parameters, remove them; they have no effect and are no longer offered.

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
- The enrollment dedup cache now covers a retry that arrives while the first attempt is still in
  flight, not just one that arrives after it already succeeded - previously that window could still
  create a duplicate order.
- `GetSingleOrderAsync` no longer swallows every failure (auth expiry, network error, malformed
  response) to a silent `null`; it now throws, so `GetSingleRecord` reports the real error instead of
  a false "not found".
- `Revoke` now verifies the order actually belongs to the configured organization before revoking it,
  rather than trusting the caller - most relevant to `RenewOrReissue`, where the prior cert's request
  ID comes from Command's own certificate data rather than this org's enrollment history.
- Concurrent calls that both see an expired token no longer both re-authenticate at once, which could
  race writes to the shared bearer token and `HttpClient` headers.
- `GetOrganizationAsync` now rejects a non-GUID org ID before making a request, matching the
  validation already applied to order IDs.
- `Enroll` now rejects an ECC CSR that uses explicit curve parameters instead of a named curve (e.g.
  P-256), with an actionable error message. MarkMonitor silently fails such an order almost instantly,
  with no reason surfaced anywhere in its API.
- `FetchOrderAsync` (used by `GetSingleRecord` and `Revoke`'s ownership check) no longer re-sets the
  shared `HttpClient`'s Authorization header itself, unguarded, after calling
  `EnsureAuthenticatedAsync` - that bypassed the `_authLock` discipline the rest of the client relies
  on, letting a concurrent `FetchOrderAsync` call (or a concurrent re-authentication) race writes to
  the shared header ([#8](../../issues/8)).
- The enrollment dedup-hit log message now reports the resolved `CARequestID` instead of firing before
  the in-flight reservation resolves, so a dedup hit can actually be correlated with the order it was
  folded into ([#7](../../issues/7)).

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
- Organization name resolution (`ResolveOrganizationAsync`/`ResolveOrganizationIdAsync`) now requires
  an exact (case-insensitive) name match instead of taking the first result from MarkMonitor's
  `/certs/v1/organization` name search - a configured org name that happened to be a substring of
  another org's name could otherwise silently resolve to the wrong organization, undermining the
  cross-org ownership check in `Revoke` ([#9](../../issues/9)).

### Added

- A dedicated xUnit test project (`markmonitor-caplugin.Tests`) covering the fixes above.
- A `justfile` with ad-hoc MarkMonitor order lookup/cancel/revoke recipes for manual cleanup.
- `TestConsole` now cancels every order it creates by default (opt out with
  `MARKMONITOR_SKIP_CLEANUP=true`), so repeated manual test runs don't accumulate billable orders.
