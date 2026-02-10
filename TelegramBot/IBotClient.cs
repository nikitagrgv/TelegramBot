using Telegram.Bot.Types.Enums;

namespace TelegramBot;

public interface IBotClient
{
    Task SendMessageAsync(long chatId, string text, ParseMode? parseMode = null,
        CancellationToken cancellationToken = default);
}
