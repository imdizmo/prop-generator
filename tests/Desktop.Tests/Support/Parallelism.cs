using Xunit;

// Two test classes at a time. The tests share one easy-props engine host (EngineHost.Default: one build engine and one
// rules engine for the whole run, as the app has); more classes at once under the three-core cap the tests run with
// let V8's background tasks fall behind and the process grow.
[assembly: CollectionBehavior(MaxParallelThreads = 2)]
