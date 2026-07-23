# TestConsole

A manual integration-test console app that exercises `MarkMonitorClient`/`MarkMonitorCAPlugin` directly
against a live MarkMonitor API (sandbox or production). This is not a unit test suite - it's a
scripted smoke test you run by hand, and it makes real API calls that create real, billable orders.

## Required environment variables

```
MARKMONITOR_BASE_URL
MARKMONITOR_API_TOKEN
MARKMONITOR_USERNAME
MARKMONITOR_PASSWORD
```

A local `.env` (gitignored) can hold these; source it before running, e.g.:

```shell
set -a && source .env && set +a && dotnet run --project TestConsole
```

## Order cleanup

Every enrollment this console performs creates a real MarkMonitor order, which is billable. **By
default, each order is cancelled (falling back to revoke if cancel fails) immediately after it's
created**, so repeated runs don't accumulate charges.

To opt out and leave created orders in place instead - e.g. to inspect them manually in the
MarkMonitor portal, or to test something downstream of order creation - set:

```
MARKMONITOR_SKIP_CLEANUP=true
```

When this is set, the console prints a warning at startup and leaves every order it creates
untouched. **Anything created during such a run is your responsibility to clean up manually.**

A cleanup failure (cancel and revoke both fail) does not fail the test run - it's logged as a
warning so you know to clean that specific order up by hand, since letting one cleanup failure
abort the whole run wouldn't help the orders created before it either.
