# markmonitor-caplugin.IntegrationTests

xUnit suite that hits the **real** MarkMonitor API - authenticate, list organizations, list
certificate orders, and enroll with an RSA and an ECC CSR. Every test reads the same four
`MARKMONITOR_*` environment variables `TestConsole` uses and returns immediately (a silent pass,
not a skip/failure) when they're unset, so this project is always safe to run, including in CI
with no credentials present.

```
MARKMONITOR_BASE_URL
MARKMONITOR_API_TOKEN
MARKMONITOR_USERNAME
MARKMONITOR_PASSWORD
```

```shell
set -a && source .env && set +a   # or TestConsole/.env
dotnet test markmonitor-caplugin.IntegrationTests -c Release
```

Each enrollment test always cancels (falling back to revoke) the order it creates - there is no
`MARKMONITOR_SKIP_CLEANUP` escape hatch here, unlike `TestConsole`. A cleanup failure throws rather
than logging a warning, so a billable order is never left behind silently.

See [DEVELOPMENT.md](../DEVELOPMENT.md#live-integration-tests-markmonitor-caplugin-integrationtests)
for the full picture, including the manual `workflow_dispatch`-gated CI job.
