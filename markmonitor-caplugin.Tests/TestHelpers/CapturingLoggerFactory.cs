// Copyright 2026 Keyfactor
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Concurrent;
using Keyfactor.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

/// <summary>A minimal ILoggerFactory that records every formatted log message it's given, so tests
/// can assert on log output emitted via Keyfactor.Logging's LogHandler (which only exposes a
/// settable Factory, not the concrete Microsoft.Extensions.Logging.LoggerFactory type).</summary>
public sealed class CapturingLoggerFactory : ILoggerFactory
{
    /// <summary>Points LogHandler.Factory at a fresh CapturingLoggerFactory and returns an
    /// IDisposable that restores it to a NullLoggerFactory - use with a `using` statement so a test
    /// doesn't need its own try/finally around the swap.</summary>
    public static IDisposable Install(out CapturingLoggerFactory factory)
    {
        factory = new CapturingLoggerFactory();
        LogHandler.Factory = factory;
        return new Restorer();
    }

    private sealed class Restorer : IDisposable
    {
        public void Dispose() => LogHandler.Factory = new NullLoggerFactory();
    }

    public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

    /// <summary>Formatted message text only, for the (more common) callers that don't need to filter
    /// by level.</summary>
    public IEnumerable<string> Messages => Entries.Select(e => e.Message);

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((logLevel, formatter(state, exception)));

        private sealed class NoopScope : IDisposable
        {
            public static readonly NoopScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
