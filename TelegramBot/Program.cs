using System.Data;
using System.Data.Common;

namespace TelegramBot;

using System.Data.SQLite;

class Program
{
    static async Task Main()
    {
        var database = new BotDatabase("ConsumeDatabase.sqlite");

        if (!await database.InitializeAsync())
        {
            Console.WriteLine("Can't open the database");
            return;
        }

        Console.WriteLine("Database initialized");

        string? token = Environment.GetEnvironmentVariable("BOT_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("Environment variable 'BOT_TOKEN' is not set");
            return;
        }

        Console.WriteLine($"Token length is {token.Length}");

        var bot = new WeightBot(database);
        await bot.Run(token);

        Console.WriteLine("Done");
    }
}