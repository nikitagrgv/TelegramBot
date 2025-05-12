using System.Data.Common;
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
    private record ConsumedRowInfo(string Id, string UserId, string Date, string Text, string Kcal);

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

        await ParseAndDispatchUserMessageAsync(chatId, userText.Trim(), botClient, cancellationToken);
    }

    private async Task ParseAndDispatchUserMessageAsync(long chatId, string userText, ITelegramBotClient botClient,
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

        await DispatchUserMessageAsync(cmd, args, chatId, botClient, cancellationToken);
    }

    private async Task DispatchUserMessageAsync(string cmd, string args, long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        switch (cmd)
        {
            case "help":
                await PrintHelpAsync(chatId, botClient, cancellationToken);
                break;
            case "add":
                await AddConsumedAsync(args, chatId, botClient, cancellationToken);
                break;
            case "remove":
                await RemoveConsumedAsync(args, chatId, botClient, cancellationToken);
                break;
            case "stat":
                await PrintStatAsync(chatId, botClient, cancellationToken);
                break;
            default:
                string message = $"Unknown command: {cmd}. Type /help to see a list of available commands.";
                await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
                break;
        }
    }

    private async Task PrintHelpAsync(long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        await botClient.SendMessage(chatId, GetHelpMessage(), cancellationToken: cancellationToken);
    }

    private async Task AddConsumedAsync(string args, long chatId, ITelegramBotClient botClient,
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

        if (!TryParseDouble(kcalString, out double kcal))
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'add' command. Invalid kcal number: '{kcalString}'. Type /help to see a list of available commands.";
            await botClient.SendMessage(chatId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        ConsumedRowInfo? row = await AddConsumedToDatabaseAsync(chatId, name, kcal);

        if (row == null)
        {
            string errorMessage =
                $"Database error. Can't add the row. Chat ID: '{chatId}', name: '{name}', kcal: '{kcal}'";
            await botClient.SendMessage(chatId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        string message = $"""
                          Product added:
                          Name: {row.Text}
                          Kcal: {row.Kcal}
                          Date: {row.Date}
                          ID: {row.Id}
                          """;
        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }

    private async Task RemoveConsumedAsync(string args, long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(args.Trim(), out long consumedId) || consumedId < 0)
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'remove' command. Invalid id: '{args}'. Type /help to see a list of available commands.";
            await botClient.SendMessage(chatId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        ConsumedRowInfo? row = await RemoveConsumedFromDatabaseAsync(consumedId);
        if (row == null)
        {
            string errorMessage =
                $"Database error. Can't remove the row. Chat ID: '{chatId}', consumed ID: '{consumedId}'";
            await botClient.SendMessage(chatId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        string message = $"""
                          Product removed:
                          Name: {row.Text}
                          Kcal: {row.Kcal}
                          Date: {row.Date}
                          ID: {row.Id}
                          """;
        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }

    private async Task PrintStatAsync(long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        List<ConsumedRowInfo> rows = await GetStatFromDatabaseAsync(chatId);

        string message = "";
        message += "<pre>";

        int nameSize = 20;
        int kcalSize = 0;
        int dateSize = 12;
        int idSize = 0;
        foreach (ConsumedRowInfo row in rows)
        {
            nameSize = int.Max(nameSize, row.Text.Length);
            kcalSize = int.Max(kcalSize, row.Kcal.Length);
            idSize = int.Max(idSize, row.Id.Length);
        }

        string format = $"{{0, -{nameSize}}}|{{1, {kcalSize}}}|{{2, {dateSize}}}|{{3, {idSize}}}\n";

        message += string.Format(format, "Name", "Kcal", "Date", "ID");
        foreach (ConsumedRowInfo row in rows)
        {
            string date = FromDatabaseToUserTimeFormat(row.Date);
            message += string.Format(format, row.Text, row.Kcal, date, row.Id);
        }

        message += "</pre>";

        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken, parseMode: ParseMode.Html);
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

    private async Task<ConsumedRowInfo?> AddConsumedToDatabaseAsync(long chatId, string name, double kcal)
    {
        string date = GetCurrentDatetime();

        string sql = """
                     INSERT INTO consumed (user_id, date, text, kcal)
                     VALUES (@user_id, @date, @text, @kcal)
                     RETURNING *;
                     """;
        var cmd = new SQLiteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("user_id", chatId);
        cmd.Parameters.AddWithValue("date", date);
        cmd.Parameters.AddWithValue("text", name);
        cmd.Parameters.AddWithValue("kcal", kcal);

        return await ExecuteConsumedAndGetOneAsync(cmd);
    }

    private async Task<ConsumedRowInfo?> RemoveConsumedFromDatabaseAsync(long id)
    {
        string sql = """
                     DELETE
                     FROM consumed
                     WHERE id = @id
                     RETURNING *;
                     """;
        var cmd = new SQLiteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("id", id);

        return await ExecuteConsumedAndGetOneAsync(cmd);
    }

    private async Task<List<ConsumedRowInfo>> GetStatFromDatabaseAsync(long id)
    {
        string sql = """
                     SELECT *
                     FROM consumed
                     WHERE user_id = @id
                     ORDER BY date;
                     """;
        var cmd = new SQLiteCommand(sql, _connection);
        cmd.Parameters.AddWithValue("id", id);

        return await ExecuteConsumedAndGetAllAsync(cmd);
    }

    private async Task<ConsumedRowInfo?> ExecuteConsumedAndGetOneAsync(SQLiteCommand cmd)
    {
        try
        {
            DbDataReader reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            return ReadConsumedRowInfo(reader);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private async Task<List<ConsumedRowInfo>> ExecuteConsumedAndGetAllAsync(SQLiteCommand cmd)
    {
        try
        {
            List<ConsumedRowInfo> rows = [];

            DbDataReader reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                ConsumedRowInfo info = ReadConsumedRowInfo(reader);
                rows.Add(info);
            }

            return rows;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static ConsumedRowInfo ReadConsumedRowInfo(DbDataReader reader)
    {
        string consumedId = reader["id"].ToString() ?? string.Empty;
        string userId = reader["user_id"].ToString() ?? string.Empty;
        string date = reader["date"].ToString() ?? string.Empty;
        string text = reader["text"].ToString() ?? string.Empty;
        string kcal = reader["kcal"].ToString() ?? string.Empty;

        return new ConsumedRowInfo(consumedId, userId, date, text, kcal);
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
        string date = GetCurrentDatetime();

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
    private static bool TryParseDouble(string s, out double result)
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

    private static string GetCurrentDatetime()
    {
        return ToDatabaseTimeFormat(DateTime.UtcNow);
    }

    private static string ToDatabaseTimeFormat(DateTime dateTime)
    {
        return dateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static DateTime FromDatabaseTimeFormat(string dateTime)
    {
        return DateTime.ParseExact(dateTime, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string FromDatabaseToUserTimeFormat(string dateTime)
    {
        DateTime date = FromDatabaseTimeFormat(dateTime);
        return date.ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"^\s*/(?<cmd>\w+)(?:\s+(?<args>\S(?:.*\S)?))?\s*$")]
    private static partial Regex GetParseCommandRegex();

    [GeneratedRegex(@"^\s*(?<name>.+?)\s*,?\s*(?<kcal>\d+[,.]?\d*)\s*$")]
    private static partial Regex GetAddConsumedRegex();
}