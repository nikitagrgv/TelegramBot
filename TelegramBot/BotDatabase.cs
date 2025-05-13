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
        // await connection.OpenAsync();
    }
    
    

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
}