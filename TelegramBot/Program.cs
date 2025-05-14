using System.Data;
using System.Data.Common;

namespace TelegramBot;

class Program
{
    static async Task Main()
    {
        string? token = Environment.GetEnvironmentVariable("BOT_TOKEN");
        if (string.IsNullOrEmpty(token))
        {
            Console.WriteLine("Environment variable 'BOT_TOKEN' is not set");
            return;
        }

        Console.WriteLine($"Token length is {token.Length}");
        
        string? adminIdString = Environment.GetEnvironmentVariable("ADMIN_ID");
        if (string.IsNullOrEmpty(adminIdString))
        {
            Console.WriteLine("Environment variable 'ADMIN_ID' is not set");
            return;
        }

        if (!long.TryParse(adminIdString, out long adminId))
        {
            Console.WriteLine($"'ADMIN_ID' is invalid: {adminIdString}");
            return;
        }

        Console.WriteLine($"Admin ID is {adminId}");
        
        var database = new BotDatabase("ConsumeDatabase.sqlite");

        if (!await database.InitializeAsync())
        {
            Console.WriteLine("Can't open the database");
            return;
        }

        Console.WriteLine("Database initialized");

        var bot = new WeightBot(database, adminId);
        Task botTask = bot.Run(token);
        
        Console.WriteLine("Print anything to finish");
        Console.ReadLine();

        await botTask;

        Console.WriteLine("Done");
    }
}