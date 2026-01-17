using SQLite;

namespace WayfarerMobile.Data.Repositories;

/// <summary>
/// Base class for all repositories providing shared database connection access.
/// </summary>
public abstract class RepositoryBase
{
    private readonly Func<Task<SQLiteAsyncConnection>> _connectionFactory;

    /// <summary>
    /// Initializes a new instance of the repository with the specified connection factory.
    /// </summary>
    /// <param name="connectionFactory">Factory function that provides the database connection.</param>
    protected RepositoryBase(Func<Task<SQLiteAsyncConnection>> connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    /// <summary>
    /// Gets the database connection for executing queries.
    /// </summary>
    /// <returns>The SQLite async connection.</returns>
    protected Task<SQLiteAsyncConnection> GetConnectionAsync()
        => _connectionFactory();
}
