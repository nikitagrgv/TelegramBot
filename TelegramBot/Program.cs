namespace TelegramBot;

class Program
{
    static void Main(string[] args)
    {
        string? tokenVar = Environment.GetEnvironmentVariable("BOT_TOKEN");
        if (string.IsNullOrEmpty(tokenVar))
        {
            Console.WriteLine("Environment variable 'BOT_TOKEN' is not set");
            return;
        }

        string token = tokenVar;


        Console.WriteLine("Hello, World!");
    }
}