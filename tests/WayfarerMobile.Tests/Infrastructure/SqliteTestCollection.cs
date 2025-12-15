namespace WayfarerMobile.Tests.Infrastructure;

/// <summary>
/// Defines a collection for tests that use SQLite in-memory databases.
/// Tests in this collection run sequentially to avoid database isolation issues.
/// </summary>
[CollectionDefinition("SQLite")]
public class SqliteTestCollection : ICollectionFixture<SqliteTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

/// <summary>
/// Shared fixture for SQLite tests. Currently empty but can be extended
/// to provide shared setup/teardown for all SQLite tests.
/// </summary>
public class SqliteTestFixture
{
    // Can be extended to provide shared resources for SQLite tests
}
