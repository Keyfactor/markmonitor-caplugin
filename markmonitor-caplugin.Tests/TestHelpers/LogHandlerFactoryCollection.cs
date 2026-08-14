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

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

/// <summary>
/// Keyfactor.Logging's LogHandler.Factory is a single process-wide static. xUnit runs different test
/// classes concurrently by default, so any two test classes that both reassign it (to observe log
/// output via a fake ILoggerFactory) can race and clobber each other's factory mid-test. Every test
/// class that touches LogHandler.Factory declares [Collection(Name)] so they're all serialized against
/// one another instead of running in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class LogHandlerFactoryCollection
{
    public const string Name = "LogHandler.Factory";
}
