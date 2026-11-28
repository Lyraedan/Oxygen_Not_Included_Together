using Xunit;

// ServerConfiguration and DedicatedServer are process-wide; serialize tests to avoid cross-test interference.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
