using System.Data.Common;
using System.Data.SQLite;
using System.Globalization;
using System.Text.RegularExpressions;
using Telegram.Bot.Types.ReplyMarkups;

namespace TelegramBot;

using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

public partial class WeightBot : IDisposable
{
    private const string ShortUserTimeFormat = "HH:mm";
    private const string LongUserTimeFormat = "dd MMM HH:mm";

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
        if (update.Message is { } message)
        {
            await HandleMessageAsync(message, botClient, cancellationToken);
        }

        if (update.CallbackQuery is { } callbackQuery)
        {
            await HandleCallbackQueryAsync(callbackQuery, botClient, cancellationToken);
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (callbackQuery.From is not { Id: var userId }) return;

        if (!await RegisterUserIfNotRegisteredAsync(userId, botClient, cancellationToken))
        {
            return;
        }

        if (callbackQuery.Data is not { } queryData) return;

        await DispatchCallbackQueryAsync(userId, queryData, botClient, cancellationToken);
    }

    private async Task DispatchCallbackQueryAsync(long userId, string queryData, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        await ParseAndDispatchUserMessageAsync(userId, queryData, botClient, cancellationToken);
    }

    private async Task HandleMessageAsync(Message message, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (message.From is not { Id: var userId }) return;

        if (!await RegisterUserIfNotRegisteredAsync(userId, botClient, cancellationToken))
        {
            return;
        }

        if (message.Text is not { } userText) return;

        await ParseAndDispatchUserMessageAsync(userId, userText.Trim(), botClient, cancellationToken);
    }

    private async Task ParseAndDispatchUserMessageAsync(long userId, string userText, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        Match m = ParseCommandRegex.Match(userText);

        if (!m.Success)
        {
            const string invalidCommandMessage =
                "Sorry, I didn't understand that. Type /start to see a list of available commands.";
            await botClient.SendMessage(userId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        string cmd = m.Groups["cmd"].Value;
        string args = m.Groups["args"].Value;

        await DispatchUserMessageAsync(cmd, args, userId, botClient, cancellationToken);
    }

    private async Task DispatchUserMessageAsync(string cmd, string args, long userId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        switch (cmd)
        {
            case "start":
            case "help":
                await PrintHelpAsync(userId, botClient, cancellationToken);
                break;
            case "add":
            case "добавить":
                await AddConsumedAsync(args, userId, botClient, cancellationToken);
                break;
            case "remove":
            case "удалить":
                await RemoveConsumedAsync(args, userId, botClient, cancellationToken);
                break;
            case "stat":
            case "стат":
                await PrintShortStatAsync(userId, botClient, cancellationToken);
                break;
            case "daystat":
            case "дейстат":
                await PrintDayStatAsync(userId, botClient, cancellationToken);
                break;
            case "longstat":
            case "лонгстат":
                await PrintAllStatAsync(userId, botClient, cancellationToken);
                break;
            case "timezone":
            case "зона":
                await SetUserTimezoneOffsetAsync(args, userId, botClient, cancellationToken);
                break;
            case "limit":
            case "лимит":
                await SetMaxKcalAsync(args, userId, botClient, cancellationToken);
                break;
            case "killmeplease":
                await ShutdownBot(userId, botClient, cancellationToken);
                break;
            default:
                string message = $"Unknown command: {cmd}. Type /start to see a list of available commands.";
                await botClient.SendMessage(userId, message, cancellationToken: cancellationToken);
                break;
        }
    }

    private async Task ShutdownBot(long userId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        string message = "Shutdown...";
        await botClient.SendMessage(userId, message, cancellationToken: cancellationToken);
        _cancelTokenSource.CancelAfter(1000);
    }

    private async Task PrintHelpAsync(long userId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        await botClient.SendMessage(userId, GetHelpMessage(), cancellationToken: cancellationToken);
    }

    private async Task AddConsumedAsync(string args, long userId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        Match m = AddConsumedRegex.Match(args);

        if (!m.Success)
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'add' command. Invalid arguments: '{args}'. Type /start to see a list of available commands.";
            await botClient.SendMessage(userId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        string name = m.Groups["name"].Value;
        string kcalString = m.Groups["kcal"].Value;

        if (!TryParseDouble(kcalString, out double kcal))
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'add' command. Invalid kcal number: '{kcalString}'. Type /start to see a list of available commands.";
            await botClient.SendMessage(userId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        DateTime now = DateTime.UtcNow;

        ConsumedRowInfo? row = await _database.AddConsumedAsync(userId, name, kcal, now);

        if (row == null)
        {
            string errorMessage =
                $"Database error. Can't add the row. User ID: '{userId}', name: '{name}', kcal: '{kcal}'";
            await botClient.SendMessage(userId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        int timeZone = await _database.GetUserTimezoneOffsetAsync(userId);

        string message = $"""
                          🍽️ Added
                          🍽️ {row.Text}
                          🔥 {row.Kcal} kcal
                          📅 {FromDatabaseToUserTimeFormat(row.Date, timeZone, LongUserTimeFormat)}
                          🆔 {row.Id}
                          """;
        await botClient.SendMessage(userId, message, cancellationToken: cancellationToken);

        await PrintShortStatAsync(userId, botClient, cancellationToken);
    }

    private async Task RemoveConsumedAsync(string args, long userId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(args.Trim(), out long consumedId) || consumedId < 0)
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'remove' command. Invalid id: '{args}'. Type /start to see a list of available commands.";
            await botClient.SendMessage(userId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        ConsumedRowInfo? row = await _database.RemoveConsumedAsync(consumedId);
        if (row == null)
        {
            string errorMessage =
                $"Database error. Can't remove the row. User ID: '{userId}', consumed ID: '{consumedId}'";
            await botClient.SendMessage(userId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        int timeZone = await _database.GetUserTimezoneOffsetAsync(userId);

        string message = $"""
                          🗑️ Removed
                          🍽 {row.Text}
                          🔥 {row.Kcal} kcal
                          📅 {FromDatabaseToUserTimeFormat(row.Date, timeZone, LongUserTimeFormat)}
                          🆔 {row.Id}
                          """;
        await botClient.SendMessage(userId, message, cancellationToken: cancellationToken);

        await PrintShortStatAsync(userId, botClient, cancellationToken);
    }

    private async Task PrintShortStatAsync(long userId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        int timeZone = await _database.GetUserTimezoneOffsetAsync(userId);
        DateTime dayBeginUtc = GetUserDayBeginUtc(timeZone);

        double consumed = await _database.GetConsumedCalAsync(dayBeginUtc, null, userId);
        double? limit = await _database.GetMaxKcalAsync(userId);

        string message;
        if (limit != null)
        {
            int consumedPercents = (int)(consumed / limit * 100);
            message = $"🔥 Consumed Today: {consumed} / {limit} kcal ({consumedPercents} %)\n";
            if (consumed < limit)
            {
                message += $"✅ {limit - consumed} kcal left\n";
            }
            else
            {
                message += $"❌ {consumed - limit} kcal overeat!\n";
            }
        }
        else
        {
            message = $"🔥 Consumed Today: {consumed} kcal (no limit set)\n";
        }

        await botClient.SendMessage(userId, message, cancellationToken: cancellationToken);
    }

    private async Task PrintDayStatAsync(long userId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        int timeZone = await _database.GetUserTimezoneOffsetAsync(userId);
        DateTime dayBeginUtc = GetUserDayBeginUtc(timeZone);
        await PrintStatAsync(dayBeginUtc, null, "📊 Day Statistics", ShortUserTimeFormat, timeZone, userId, botClient,
            cancellationToken);
    }

    private async Task PrintAllStatAsync(long userId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        int timeZone = await _database.GetUserTimezoneOffsetAsync(userId);
        await PrintStatAsync(null, null, "📈 Total Statistics", LongUserTimeFormat, timeZone, userId, botClient,
            cancellationToken);
    }

    private async Task PrintStatAsync(DateTime? begin, DateTime? end, string titleMessage, string timeFormat,
        int timeZone, long userId,
        ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        List<ConsumedRowInfo> dbRows = await _database.GetStatAsync(begin, end, userId);

        // TODO: refactor shit

        List<ConsumedRowInfoStrings> strRows = dbRows
            .Select(row => DbRowToUserStringRow(row, timeZone, timeFormat))
            .ToList();
        int kcalSize = strRows.Any() ? strRows.Max(row => row.Kcal.Length) : 0;
        int idSize = strRows.Any() ? strRows.Max(row => row.Id.Length) : 0;
        double consumedToday = dbRows.Any() ? dbRows.Sum(row => row.Kcal) : 0;
        int dateSize = timeFormat.Length;

        const int tableLengthBudget = 36;
        int nameSize = int.Max(8, tableLengthBudget - kcalSize - dateSize - idSize);

        string rowFormat = $"{{0, {idSize}}}| {{1, -{nameSize}}}| {{2, {kcalSize}}}| {{3, {dateSize}}}\n";

        string message = "";
        message += titleMessage + '\n';
        message += $"🌍 Time Zone: {timeZone:+#;-#;0}\n";
        message += $"🔥 Consumed: {consumedToday} kcal\n";

        message += "<pre>";
        message += string.Format(rowFormat, "ID", "Name", "Kcal", "Date");

        foreach (ConsumedRowInfoStrings row in strRows)
        {
            string curName = row.Text;
            while (curName.Length > nameSize)
            {
                message += string.Format(rowFormat, curName[..nameSize], string.Empty, string.Empty,
                    string.Empty);
                curName = curName[nameSize..];
            }

            message += string.Format(rowFormat, row.Id, curName, row.Kcal, row.Date);
        }

        message += "</pre>";

        await botClient.SendMessage(userId, message, cancellationToken: cancellationToken, parseMode: ParseMode.Html);
    }

    private async Task SetUserTimezoneOffsetAsync(string args, long userId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(args.Trim(), out int timezone) || timezone < 0)
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'timezone' command. Invalid offset: '{args}'. Type /start to see a list of available commands.";
            await botClient.SendMessage(userId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        bool success = await _database.SetUserTimezoneOffsetAsync(userId, timezone);
        if (!success)
        {
            string errorMessage =
                $"Database error. Can't set the timezone. User ID: '{userId}', timezone: '{timezone}'";
            await botClient.SendMessage(userId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        string message = $"Timezone updated: {timezone:+#;-#;0}";
        await botClient.SendMessage(userId, message, cancellationToken: cancellationToken);
    }

    private async Task SetMaxKcalAsync(string args, long userId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (!double.TryParse(args.Trim(), out double limit) || limit < 0)
        {
            string invalidCommandMessage =
                $"Sorry, I didn't understand your 'limit' command. Invalid value: '{args}'. Type /start to see a list of available commands.";
            await botClient.SendMessage(userId, invalidCommandMessage, cancellationToken: cancellationToken);
            return;
        }

        bool success = await _database.SetMaxKcalAsync(userId, limit);
        if (!success)
        {
            string errorMessage =
                $"Database error. Can't set the limit. User ID: '{userId}', limit: '{limit}'";
            await botClient.SendMessage(userId, errorMessage, cancellationToken: cancellationToken);
            return;
        }

        string message = $"Limit updated: {limit} kcal";
        await botClient.SendMessage(userId, message, cancellationToken: cancellationToken);
    }

    private async Task<bool> RegisterUserIfNotRegisteredAsync(long userId, ITelegramBotClient botClient,
        CancellationToken cancellationToken)
    {
        if (await _database.HasUserIdAsync(userId))
        {
            return true;
        }

        DateTime registerDate = DateTime.UtcNow;

        bool registered = await _database.RegisterUserIdAsync(userId, registerDate);
        if (!registered)
        {
            Console.WriteLine($"Failed to register user. Id = {userId}");
            return false;
        }

        Console.WriteLine($"Registered user. Id = {userId}");

        const string message = "You are registered! Welcome!";
        await botClient.SendMessage(userId, message, cancellationToken: cancellationToken);
        await botClient.SendMessage(userId, GetHelpMessage(), cancellationToken: cancellationToken);
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
               🍽️ Add a consumed product:
               add молочная каша, 12
               добавить молочная каша, 12
               добавить молочная каша 12

               🗑️ Remove a consumed product by id:
               remove 6
               удалить 6

               🔥 Print consumed kcal by the current day
               /stat
               стат

               📅 Print all consumed products by the current day starting from 00:00:
               /daystat
               дейстат

               📊 Print all consumed products by all the time
               /longstat
               лонгстат

               🎯 Set kcal limit
               limit 123.45
               лимит 123.45

               🌍 Set the time zone offset:
               timezone +7
               пояс 7

               ❓ Print this help:
               /start
               help
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

    private static DateTime GetUserDayBeginUtc(int timeZone)
    {
        DateTime curDateUser = DateTime.UtcNow.AddHours(+timeZone);
        DateTime dayBeginUser = new DateTime(curDateUser.Year, curDateUser.Month, curDateUser.Day, 0, 0, 0);
        return dayBeginUser.AddHours(-timeZone);
    }

    private static ConsumedRowInfoStrings DbRowToUserStringRow(ConsumedRowInfo row, int timezone, string timeFormat)
    {
        return new ConsumedRowInfoStrings(
            Id: row.Id.ToString(),
            Date: FromDatabaseToUserTimeFormat(row.Date, timezone, timeFormat),
            Text: row.Text,
            Kcal: row.Kcal.ToString("F"));
    }

    private static string FromDatabaseToUserTimeFormat(DateTime date, int timeZone, string format)
    {
        date = date.AddHours(timeZone);
        return date.ToString(format, CultureInfo.InvariantCulture);
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

    [GeneratedRegex(@"^\s*/?(?<cmd>[а-яА-Яa-zA-Z0-9_]+)(?:\s+(?<args>\S(?:.*\S)?))?\s*$")]
    private static partial Regex GetParseCommandRegex();

    [GeneratedRegex(@"^\s*(?<name>.+?)\s*,?\s*(?<kcal>\d+[,.]?\d*)\s*$")]
    private static partial Regex GetAddConsumedRegex();

    public record ConsumedRowInfoStrings(string Id, string Date, string Text, string Kcal);
}