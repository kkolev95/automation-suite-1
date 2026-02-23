using Xunit;

namespace TestIT.ApiTests;

/// <summary>
/// Test collection definitions to control parallel execution.
/// Tests in the same collection run sequentially, not in parallel.
/// </summary>

// Data integrity tests - run isolated from cleanup
[CollectionDefinition("DataIntegrity", DisableParallelization = false)]
public class DataIntegrityCollection
{
}

// Cleanup tests - run sequentially after other tests
[CollectionDefinition("Cleanup", DisableParallelization = true)]
public class CleanupCollection
{
}

// Stress tests - can run in parallel with each other
[CollectionDefinition("Stress", DisableParallelization = false)]
public class StressCollection
{
}
