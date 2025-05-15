using System.Text.Json;

namespace TelegramBot;

class Program
{
    private static Config? GetConfig()
    {
        const string configPath = "botconf.json";

        if (!File.Exists(configPath))
        {
            Console.WriteLine("No config found");
            return null;
        }

        string json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize<Config>(json);
    }

    private static string? GetToken(Config? config)
    {
        string? str = Environment.GetEnvironmentVariable("BOT_TOKEN");
        if (!string.IsNullOrEmpty(str))
        {
            Console.WriteLine("Get token from environment variable 'BOT_TOKEN'");
            return str;
        }

        str = config?.BotToken;
        if (str != null)
        {
            Console.WriteLine("Get token from config");
            return str;
        }

        Console.WriteLine("No token found in config");
        return null;
    }

    private static string? GetAdminIdString(Config? config)
    {
        string? str = Environment.GetEnvironmentVariable("ADMIN_ID");
        if (!string.IsNullOrEmpty(str))
        {
            Console.WriteLine("Get admin ID from environment variable 'ADMIN_ID'");
            return str;
        }

        str = config?.AdminId;
        if (str != null)
        {
            Console.WriteLine("Get admin ID from config");
            return str;
        }

        Console.WriteLine("No admin ID found in config");
        return null;
    }

    static async Task Main()
    {
        Console.WriteLine("TelegramBot");

        Config? config = GetConfig();

        string? token = GetToken(config);
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        Console.WriteLine($"Token length is {token.Length}");

        string? adminIdString = GetAdminIdString(config);
        if (string.IsNullOrEmpty(adminIdString))
        {
            return;
        }

        if (!long.TryParse(adminIdString, out long adminId))
        {
            Console.WriteLine($"Admin ID is invalid: {adminIdString}");
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

        CancellationTokenSource cancelTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            Console.WriteLine("Canceling pressed...");
            e.Cancel = true;
            cancelTokenSource.Cancel();
        };

        var bot = new WeightBot(database, adminId, cancelTokenSource);
        await bot.Run(token);

        Console.WriteLine("Done");
    }

    private record Config(string? BotToken, string? AdminId);
}