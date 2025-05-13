using System.Data.SQLite;

namespace TelegramBot;

public class BotDatabase : IDisposable
{
    private readonly SQLiteConnection _connection;
    private bool _disposed;

    public BotDatabase(string databasePath)
    {
        string connectionString = $"Data Source={databasePath};Version=3;";

        _connection = new SQLiteConnection(connectionString);
    }

    public async Task<bool> InitializeAsync()
    {
        await _connection.OpenAsync();
        if (_connection.State != System.Data.ConnectionState.Open)
        {
            return false;
        }

        await using var cmd = new SQLiteCommand("PRAGMA foreign_keys = ON;", _connection);
        await cmd.ExecuteNonQueryAsync();

        int oldVersion = await GetDatabaseVersion();
        int newVersion = await MigrateDatabaseToLatestVersion(oldVersion);

        if (oldVersion != newVersion)
        {
            await SetDatabaseVersion(newVersion);
        }

        return true;
    }

    private async Task<int> MigrateDatabaseToLatestVersion(int oldVersion)
    {
        int newVersion = oldVersion;

        if (newVersion < 1)
        {
            await MigrateDatabaseToVersion1();
            newVersion = 1;
        }

        return newVersion;
    }

    private async Task MigrateDatabaseToVersion1()
    {
    }

    private async Task<int> GetDatabaseVersion()
    {
        await using var cmd = new SQLiteCommand("PRAGMA user_version;", _connection);
        object? ret = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(ret);
    }

    private async Task SetDatabaseVersion(int version)
    {
        await using var cmd = new SQLiteCommand("PRAGMA user_version = @version;", _connection);
        cmd.Parameters.AddWithValue("@version", version);
        await cmd.ExecuteNonQueryAsync();
    }

    #region Dispose

    void IDisposable.Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _connection.Dispose();
        }

        _disposed = true;
    }

    #endregion
}