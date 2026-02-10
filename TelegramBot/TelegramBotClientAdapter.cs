using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace TelegramBot;

public class TelegramBotClientAdapter : IBotClient
{
    private readonly ITelegramBotClient _client;

    public TelegramBotClientAdapter(ITelegramBotClient client)
    {
        _client = client;
    }

    public async Task SendMessageAsync(long chatId, string text, ParseMode? parseMode = null,
        CancellationToken cancellationToken = default)
    {
        if (parseMode.HasValue)
            await _client.SendMessage(chatId, text, parseMode: parseMode.Value, cancellationToken: cancellationToken);
        else
            await _client.SendMessage(chatId, text, cancellationToken: cancellationToken);
    }
}
