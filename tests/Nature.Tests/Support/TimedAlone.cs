using Xunit;

namespace PropGenerator.Nature.Tests.Support;

/// <summary>
/// The tests that time something: they run on their own, after the parallel ones, so other tests' work on the same
/// cores does not slow them down.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimedAlone
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Timing";
}
