namespace TelegramBot;

using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;

public class WeightBot
{
    private string _token;

    public WeightBot(string token)
    {
        _token = token;
    }


    public async Task Run()
    {
        var botClient = new TelegramBotClient(_token);

        using var cts = new CancellationTokenSource();

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = [],
        };

        botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            errorHandler: HandleErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token);

        var me = await botClient.GetMe(cancellationToken: cts.Token);
        Console.WriteLine($"Start listening @{me.Username}");

        cts.Token.WaitHandle.WaitOne();
    }


    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update,
        CancellationToken cancellationToken)
    {
        if (update.Message is not { Text: { } messageText }) return;

        var chatId = update.Message.Chat.Id;
        await botClient.SendMessage(chatId, messageText, cancellationToken: cancellationToken);
    }

    private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Error: {exception.Message}");
        return Task.CompletedTask;
    }
}