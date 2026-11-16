using Xunit;

namespace PropGenerator.Nature.Tests.Support;

/// <summary>
/// The tests that set <c>CliffRules.Reject</c>: the hook is static and every cliff build reads it, so they run on their
/// own, after the parallel ones, and put it back in a <c>finally</c>.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CliffRulesHook
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Cliff rules hook";
}
