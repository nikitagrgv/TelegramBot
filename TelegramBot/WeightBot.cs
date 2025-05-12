using System.Data.SQLite;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TelegramBot;

using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

public partial class WeightBot
{
    private static readonly Regex ParseCommandRegex = GetParseCommandRegex();
    private static readonly Regex AddConsumedRegex = GetAddConsumedRegex();

    private readonly SQLiteConnection _connection;

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
        if (update.Message is not { } message)
        {
            return;
        }

        long chatId = message.Chat.Id;

        if (!await RegisterChatIfNotRegisteredAsync(chatId, botClient, cancellationToken))
        {
            return;
        }

        if (message is not { Text: { } userText })
        {
            return;
        }

        await ParseAndDispatchUserMessage(chatId, userText.Trim(), botClient, cancellationToken);
    }

    private async Task ParseAndDispatchUserMessage(long chatId, string userText, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        Match m = ParseCommandRegex.Match(userText);

        if (!m.Success)
        {
            const string invalidCommandMessage =
                "Sorry, I didn't understand that. Type /help to see a list of available commands.";
            await botClient.SendMessage(chatId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        string cmd = m.Groups["cmd"].Value;
        string args = m.Groups["args"].Value;

        await DispatchUserMessage(cmd, args, chatId, botClient, cancellationToken);
    }

    private async Task DispatchUserMessage(string cmd, string args, long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        switch (cmd)
        {
            case "help":
                await PrintHelp(chatId, botClient, cancellationToken);
                break;
            case "add":
                await AddConsumed(args, chatId, botClient, cancellationToken);
                break;
            case "remove":
                await RemoveConsumed(args, chatId, botClient, cancellationToken);
                break;
            case "stat":
                await PrintStat(chatId, botClient, cancellationToken);
                break;
            default:
                string message = $"Unknown command: {cmd}. Type /help to see a list of available commands.";
                await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
                break;
        }
    }

    private async Task PrintHelp(long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        await botClient.SendMessage(chatId, GetHelpMessage(), cancellationToken: cancellationToken);
    }

    private async Task AddConsumed(string args, long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        Match m = AddConsumedRegex.Match(args);

        if (!m.Success)
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'add' command. Invalid arguments: '{args}'. Type /help to see a list of available commands.";
            await botClient.SendMessage(chatId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        string name = m.Groups["name"].Value;
        string kcalString = m.Groups["kcal"].Value;

        double kcal;
        if (!TryParseNumber(kcalString, out kcal))
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'add' command. Invalid kcal number: '{kcalString}'. Type /help to see a list of available commands.";
            await botClient.SendMessage(chatId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        long consumedId = 0;

        string message = $"""
                          Added product:
                          '{name}'
                          {kcal} kcal
                          ID = {consumedId}
                          """;
        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }

    private async Task RemoveConsumed(string args, long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
    }

    private async Task PrintStat(long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
    }

    private async Task<bool> RegisterChatIfNotRegisteredAsync(long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (await HasChatIdAsync(chatId))
        {
            return true;
        }

        bool registered = await RegisterChatIdAsync(chatId);
        if (!registered)
        {
            Console.WriteLine($"Failed to register chat. Id = {chatId}");
            return false;
        }

        Console.WriteLine($"Registered chat. Id = {chatId}");

        const string message = "You are registered! Welcome!";
        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
        await botClient.SendMessage(chatId, GetHelpMessage(), cancellationToken: cancellationToken);
        return true;
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

    private string GetHelpMessage()
    {
        return """
               ---- Bot commands ----

               Add a consumed product:
               /add porridge, 12

               Remove a consumed product by id:
               /remove 6

               Print all consumed products:
               /stat

               Print this help:
               /help
               """;
    }

    // TODO: shit?
    private static bool TryParseNumber(string s, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(s))
            return false;

        char sep = Convert.ToChar(
            CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator);

        var normalized = s
            .Replace(",", sep.ToString())
            .Replace(".", sep.ToString());

        return double.TryParse(normalized,
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out result);
    }

    private static string ToDatabaseTimeFormat(DateTime dateTime)
    {
        return dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"^\s*/(?<cmd>\w+)(?:\s+(?<args>\S(?:.*\S)?))?\s*$")]
    private static partial Regex GetParseCommandRegex();

    [GeneratedRegex(@"^\s*(?<name>.+?)\s*,?\s*(?<kcal>\d+[,.]?\d*)\s*$")]
    private static partial Regex GetAddConsumedRegex();
}