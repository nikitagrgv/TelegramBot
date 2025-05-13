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

        await ExecuteNonQueryAsync("PRAGMA foreign_keys = ON;");
        
        
    }


    private async Task<int> ExecuteNonQueryAsync(string sql)
    {
        await using var cmd = new SQLiteCommand(sql, _connection);
        return await cmd.ExecuteNonQueryAsync();
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