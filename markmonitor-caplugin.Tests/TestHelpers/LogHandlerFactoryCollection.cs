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
