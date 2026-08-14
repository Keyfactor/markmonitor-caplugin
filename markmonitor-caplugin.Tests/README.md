# markmonitor-caplugin.Tests

xUnit unit-test suite for the MarkMonitor CA plugin. Exercises `MarkMonitorCAPlugin` and
`MarkMonitorClient` against a fake `HttpMessageHandler` (see `TestHelpers/FakeHttpMessageHandler.cs`)
and injected fakes (`FakeCertificateDataReader`, `FakeAnyCAPluginConfigProvider`,
`ManualTimeProvider`) - no live API or credentials required, safe to run in CI.

```shell
dotnet test markmonitor-caplugin.sln -c Release
```

See [DEVELOPMENT.md](../DEVELOPMENT.md#unit-tests) for what the suite covers and the pattern to
follow when adding new tests.
