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
        
        var bot = new WeightBot();
        await bot.Run(token);
    }
}