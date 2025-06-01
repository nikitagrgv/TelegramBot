using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace TelegramBot;

public struct Config
{
    public Config()
    {
    }

    public Config(string? botToken, string? adminId)
    {
        BotToken = botToken;
        AdminId = adminId;
    }

    public string? BotToken { get; init; } = null;
    public string? AdminId { get; init; } = null;
}

internal class Program
{
    private static Config GetConfig()
    {
        const string configPath = "botconf.json";

        if (!File.Exists(configPath))
        {
            Console.WriteLine("No config found");
            return new Config();
        }

        string json = File.ReadAllText(configPath);
        Config? config = JsonSerializer.Deserialize<Config>(json);
        return config ?? new Config();
    }

    private static string? GetToken(Config config)
    {
        string? str = Environment.GetEnvironmentVariable("BOT_TOKEN");
        if (!string.IsNullOrEmpty(str))
        {
            Console.WriteLine("Get token from environment variable 'BOT_TOKEN'");
            return str;
        }

        str = config.BotToken;
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


    private static async Task Main()
    {
        Console.WriteLine("TelegramBot");

        Config config = GetConfig();

        string? token = GetToken(config);
        if (string.IsNullOrEmpty(token)) return;

        Console.WriteLine($"Token length is {token.Length}");

        string? adminIdString = GetAdminIdString(config);
        if (string.IsNullOrEmpty(adminIdString)) return;

        if (!long.TryParse(adminIdString, out long adminId))
        {
            Console.WriteLine($"Admin ID is invalid: {adminIdString}");
            return;
        }

        Console.WriteLine($"Admin ID is {adminId}");

        await using var dbContext = new BotDbContext();

        await dbContext.Database.MigrateAsync();

        var database = new BotDatabase(dbContext);

        Console.WriteLine("Database initialized");

        var cancelTokenSource = new CancellationTokenSource();

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
}