using System.Data.Common;
using System.Data.SQLite;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TelegramBot;

using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

public partial class WeightBot : IDisposable
{
    private static readonly Regex ParseCommandRegex = GetParseCommandRegex();
    private static readonly Regex AddConsumedRegex = GetAddConsumedRegex();

    private bool _disposed;
    private readonly BotDatabase _database;
    private readonly CancellationTokenSource _cancelTokenSource;

    public WeightBot(BotDatabase database)
    {
        _database = database;
        _cancelTokenSource = new CancellationTokenSource();
    }

    public async Task Run(string token)
    {
        var botClient = new TelegramBotClient(token);


        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [],
        };

        User me = await botClient.GetMe(cancellationToken: _cancelTokenSource.Token);
        Console.WriteLine($"Start listening @{me.Username}");

        await botClient.ReceiveAsync(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: _cancelTokenSource.Token);
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
            case "timezone":
                await SetUserTimezoneOffsetAsync(args, chatId, botClient, cancellationToken);
                break;
            case "kill":
                await ShutdownBot(chatId, botClient, cancellationToken);
                break;
            default:
                string message = $"Unknown command: {cmd}. Type /help to see a list of available commands.";
                await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
                break;
        }
    }

    private async Task ShutdownBot(long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        string message = "Shutdown...";
        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
        _cancelTokenSource.CancelAfter(1000);
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

        ConsumedRowInfo? row = await _database.AddConsumedToDatabaseAsync(chatId, name, kcal);

        if (row == null)
        {
            string errorMessage =
                $"Database error. Can't add the row. Chat ID: '{chatId}', name: '{name}', kcal: '{kcal}'";
            await botClient.SendMessage(chatId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        int timeZone = await _database.GetUserTimezoneOffsetAsync(chatId);

        string message = $"""
                          ✅ Product added
                          🍽️ Name: {row.Text}
                          🔥 Kcal: {row.Kcal}
                          📅 Date: {FromDatabaseToUserTimeFormat(row.Date, timeZone)}
                          🆔 ID: {row.Id}
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

        ConsumedRowInfo? row = await _database.RemoveConsumedFromDatabaseAsync(consumedId);
        if (row == null)
        {
            string errorMessage =
                $"Database error. Can't remove the row. Chat ID: '{chatId}', consumed ID: '{consumedId}'";
            await botClient.SendMessage(chatId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        int timeZone = await _database.GetUserTimezoneOffsetAsync(chatId);

        string message = $"""
                          ❌ Product removed
                          🍽 Name: {row.Text}
                          🔥 Kcal: {row.Kcal}
                          📅 Date: {FromDatabaseToUserTimeFormat(row.Date, timeZone)}
                          🆔 ID: {row.Id}
                          """;
        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }

    private async Task PrintStatAsync(long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        string message = "";
        message += "<pre>";

        int timeZone = await _database.GetUserTimezoneOffsetAsync(chatId);
        message += $"User Time Zone: {timeZone:+#;-#;0}\n";

        double consumedToday = await _database.GetConsumedDayCalFromDatabaseAsync(chatId);
        message += $"Consumed Today: {consumedToday} kcal\n";


        const int budget = 36;

        int kcalSize = 0;
        int dateSize = 12;
        int idSize = 0;

        List<ConsumedRowInfo> dbRows = await _database.GetStatFromDatabaseAsync(chatId);
        List<ConsumedRowInfo> rows = await _database.GetStatFromDatabaseAsync(chatId);


        foreach (ConsumedRowInfo row in rows)
        {
            kcalSize = int.Max(kcalSize, row.Kcal.Length);
            idSize = int.Max(idSize, row.Id.Length);
        }

        int nameSize = int.Max(8, budget - kcalSize - dateSize - idSize);

        string format = $"{{0, -{nameSize}}}| {{1, {kcalSize}}}| {{2, {dateSize}}}| {{3, {idSize}}}\n";

        message += string.Format(format, "Name", "Kcal", "Date", "ID");

        foreach (ConsumedRowInfo row in rows)
        {
            string date = FromDatabaseToUserTimeFormat(row.Date, timeZone);

            string curName = row.Text;
            while (curName.Length > nameSize)
            {
                message += string.Format(format, curName.Substring(0, nameSize), string.Empty, string.Empty,
                    string.Empty);
                curName = curName.Substring(nameSize);
            }

            message += string.Format(format, curName, row.Kcal, date, row.Id);
        }

        message += "</pre>";

        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken, parseMode: ParseMode.Html);
    }

    private async Task SetUserTimezoneOffsetAsync(string args, long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(args.Trim(), out int timezone) || timezone < 0)
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'timezone' command. Invalid offset: '{args}'. Type /help to see a list of available commands.";
            await botClient.SendMessage(chatId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        bool success = await _database.SetUserTimezoneOffsetToDatabaseAsync(chatId, timezone);
        if (!success)
        {
            string errorMessage =
                $"Database error. Can't set the timezone. Chat ID: '{chatId}', timezone: '{timezone}'";
            await botClient.SendMessage(chatId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        string message = $"Timezone updated: {timezone:+#;-#;0}";
        await botClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
    }

    private async Task<bool> RegisterChatIfNotRegisteredAsync(long chatId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (await _database.HasChatIdAsync(chatId))
        {
            return true;
        }

        bool registered = await _database.RegisterChatIdAsync(chatId);
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

               Set the time zone offset:
               /timezone +7

               Print this help:
               /help
               """;
    }

    #region Dispose

    void IDisposable.Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _cancelTokenSource.Dispose();
        }

        _disposed = true;
    }

    #endregion

    private static ConsumedRowInfoStrings DbRowToStringRow(ConsumedRowInfo row, int timezone)
    {
        return new ConsumedRowInfoStrings(
            Id: row.Id.ToString(),
            Date: FromDatabaseToUserTimeFormat(row.Date, timezone),
            Text: row.Text,
            Kcal: row.Kcal.ToString("F"));
    }

    private static string FromDatabaseToUserTimeFormat(DateTime date, int timeZone)
    {
        date = date.AddHours(timeZone);
        return date.ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);
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

    [GeneratedRegex(@"^\s*/(?<cmd>\w+)(?:\s+(?<args>\S(?:.*\S)?))?\s*$")]
    private static partial Regex GetParseCommandRegex();

    [GeneratedRegex(@"^\s*(?<name>.+?)\s*,?\s*(?<kcal>\d+[,.]?\d*)\s*$")]
    private static partial Regex GetAddConsumedRegex();

    public record ConsumedRowInfoStrings(string Id, string Date, string Text, string Kcal);
}