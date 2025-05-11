using System.Data.SQLite;
using System.Globalization;

namespace TelegramBot;

using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

public class WeightBot
{
    private SQLiteConnection _connection;

    public WeightBot(SQLiteConnection connection)
    {
        _connection = connection;
    }

    public async Task Run(string token)
    {
        var botClient = new TelegramBotClient(token);

        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [],
        };

        User me = await botClient.GetMe(cancellationToken: cts.Token);
        Console.WriteLine($"Start listening @{me.Username}");

        await botClient.ReceiveAsync(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token);
    }


    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
        CancellationToken cancellationToken)
    {
        if (update.Message is not { Text: { } userText }) return;

        long chatId = update.Message.Chat.Id;
        if (!await HasChatIdAsync(chatId))
        {
            bool registered = await RegisterChatIdAsync(chatId);
            if (!registered)
            {
                Console.WriteLine($"Failed to register chat. Id = ${chatId}");
                return;
            }

            Console.WriteLine($"Registered chat. Id = ${chatId}");

            string message = "You are registered! Welcome!";
            await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);

            await botClient.SendMessage(chatId, GetInfoMessage(), cancellationToken: cancellationToken);
        }

        await botClient.SendMessage(chatId, userText, cancellationToken: cancellationToken);
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Error: {exception.Message}");
        return Task.CompletedTask;
    }

    private async Task<bool> HasChatIdAsync(long chatId)
    {
        string sql = "SELECT EXISTS(SELECT 1 FROM users WHERE id = @id)";
        var cmd = new SQLiteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("id", chatId);
        object? result = await cmd.ExecuteScalarAsync();
        return Convert.ToInt32(result) == 1;
    }

    private async Task<bool> RegisterChatIdAsync(long chatId)
    {
        string date = ToDatabaseTimeFormat(DateTime.UtcNow);

        string sql = """
                     INSERT INTO users (id, register_date)
                     VALUES (@id, @date);
                     """;
        var cmd = new SQLiteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("id", chatId);
        cmd.Parameters.AddWithValue("date", date);
        int result = await cmd.ExecuteNonQueryAsync();
        return result != 0;
    }

    private string GetInfoMessage()
    {
        return """
               Bot commands:
               /add porridge, 12 ---- add porridge, 12 kcal 
               /stat ---- print your consumed products
               /help ---- print this help
               """;
    }

    private static string ToDatabaseTimeFormat(DateTime dateTime)
    {
        return dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}