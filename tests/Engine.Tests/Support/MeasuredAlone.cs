using Xunit;

namespace PropGenerator.Engine.Tests.Support;

/// <summary>
/// The tests that time something or watch the process's memory: they run on their own, after the parallel ones, so
/// other classes' engines on the same cores and in the same process neither slow them down nor move the RSS.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MeasuredAlone
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Measured alone";
}
