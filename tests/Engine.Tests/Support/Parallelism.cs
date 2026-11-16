using Xunit;

// Two test classes at a time. Each heavy class runs an engine of its own; with three V8 runtimes busy under the
// three-core cap the tests run with, V8's background tasks (sweeping, freeing ArrayBuffers) fall behind and the
// process grows past 4 GB, where two stay near 600 MB. The app runs one engine for builds.
[assembly: CollectionBehavior(MaxParallelThreads = 2)]
