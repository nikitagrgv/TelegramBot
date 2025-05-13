using System.Data;
using System.Data.Common;

namespace TelegramBot;

using System.Data.SQLite;

class Program
{
    private static async Task<int> RunSqliteNonQueryAsync(string sql, SQLiteConnection connection)
    {
        await using var cmd = new SQLiteCommand(sql, connection);
        return await cmd.ExecuteNonQueryAsync();
    }

    static async Task Main()
    {
        string dbPath = "database_v1.sqlite";
        string connectionString = $"Data Source={dbPath};Version=3;";
        await using var connection = new SQLiteConnection(connectionString);
        await connection.OpenAsync();

        if (connection.State != ConnectionState.Open)
        {
            Console.WriteLine("Can't open the database");
            return;
        }

        // TODO: check versions
        await RunSqliteNonQueryAsync("PRAGMA foreign_keys = ON;", connection);

        await RunSqliteNonQueryAsync("""
                                     CREATE TABLE IF NOT EXISTS users
                                     (
                                         id            INTEGER PRIMARY KEY,
                                         register_date TEXT NOT NULL,
                                         timezone      INTEGER NOT NULL DEFAULT 0,
                                         min_kcal      REAL,
                                         max_kcal      REAL
                                     );
                                     """, connection);

        await RunSqliteNonQueryAsync("""
                                     CREATE TABLE IF NOT EXISTS consumed
                                     (
                                         id      INTEGER PRIMARY KEY AUTOINCREMENT,
                                         user_id INTEGER NOT NULL,
                                         date    TEXT    NOT NULL,
                                         text    TEXT    NOT NULL,
                                         kcal    REAL,
                                         FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
                                     );
                                     """, connection);

        // string insertSql = "INSERT INTO users (name, email) VALUES (@name, @email)";
        // var insertCmd = new SQLiteCommand(insertSql, connection);
        // insertCmd.Parameters.AddWithValue("@name", "Alice");
        // insertCmd.Parameters.AddWithValue("@email", "alice@example.com");
        // insertCmd.ExecuteNonQuery();

        // string selectSql = "SELECT * FROM users";
        // var selectCmd = new SQLiteCommand(selectSql, connection);
        // DbDataReader reader = await selectCmd.ExecuteReaderAsync();
        //
        //
        // while (await reader.ReadAsync())
        // {
        //     Console.WriteLine($"{reader["id"]}: {reader["name"]} - {reader["email"]}");
        // }
        //

        string? token = Environment.GetEnvironmentVariable("BOT_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("Environment variable 'BOT_TOKEN' is not set");
            return;
        }

        Console.WriteLine($"Token length is {token.Length}");

        var bot = new WeightBot(connection);
        await bot.Run(token);

        Console.WriteLine("Done");
    }
}