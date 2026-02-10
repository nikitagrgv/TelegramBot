namespace TelegramBot;

public interface IBotDatabase
{
    Task<bool> InitializeAsync();
    Task<double?> GetMaxKcalAsync(long userId);
    Task<bool> SetMaxKcalAsync(long userId, double? maxKcal);
    Task<double> GetConsumedCalAsync(DateTime? optionalBegin, DateTime? optionalEnd, long userId);
    Task<ConsumedRowInfo?> AddConsumedAsync(long userId, string name, double kcal, DateTime date);
    Task<ConsumedRowInfo?> RemoveConsumedAsync(long id, long? userId);
    Task<List<ConsumedRowInfo>> GetStatAsync(DateTime? optionalBegin, DateTime? optionalEnd, long? userId);
    Task<bool> SetUserNotificationsEnabled(long userId, bool enabled);
    Task<List<long>> GetNotifiedUsersIds();
    Task<bool> HasUserIdAsync(long userId);
    Task<bool> SetUserTimezoneOffsetAsync(long userId, int timezoneOffset);
    Task<int> GetUserTimezoneOffsetAsync(long userId);
    Task<bool> RegisterUserIdAsync(long userId, DateTime date);
}
