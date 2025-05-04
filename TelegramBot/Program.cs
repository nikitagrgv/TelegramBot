using System.Data;
using System.Data.Common;

namespace TelegramBot;

using System.Data.SQLite;

class Program
{
    static async Task Main()
    {
        string dbPath = @"C:\Users\nekita\Desktop\TelegramBot\identifier.sqlite";
        string connectionString = $"Data Source={dbPath};Version=3;";
        var connection = new SQLiteConnection(connectionString);
        await connection.OpenAsync();

        if (connection.State != ConnectionState.Open)
        {
            Console.WriteLine("Can't open the database");
            return;
        }
        
        // string insertSql = "INSERT INTO users (name, email) VALUES (@name, @email)";
        // var insertCmd = new SQLiteCommand(insertSql, connection);
        // insertCmd.Parameters.AddWithValue("@name", "Alice");
        // insertCmd.Parameters.AddWithValue("@email", "alice@example.com");
        // insertCmd.ExecuteNonQuery();
        
        string selectSql = "SELECT * FROM users";
        var selectCmd = new SQLiteCommand(selectSql, connection);
        DbDataReader reader = await selectCmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            Console.WriteLine($"{reader["id"]}: {reader["name"]} - {reader["email"]}");
        }

        return;
        
        string? token = Environment.GetEnvironmentVariable("BOT_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("Environment variable 'BOT_TOKEN' is not set");
            return;
        }

        Console.WriteLine($"Token length is {token.Length}");
        
        var bot = new WeightBot();
        await bot.Run(token);
    }
}