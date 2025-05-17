namespace TelegramBot;

public interface IBotDatabase
{
    public Task<bool> HasUserIdAsync(long userId);
    public Task<bool> RegisterUserIdAsync(long userId, DateTime date);

    public Task<ConsumedRowInfo?> AddConsumedAsync(long userId, string name, double kcal, DateTime date);
    public Task<ConsumedRowInfo?> RemoveConsumedAsync(long id, long? userId);

    public Task<double> GetConsumedKcalAsync(DateTime? optionalBegin, DateTime? optionalEnd, long userId);
    public Task<List<ConsumedRowInfo>> GetStatAsync(DateTime? optionalBegin, DateTime? optionalEnd, long? userId);

    public Task<bool> SetUserTimezoneOffsetAsync(long userId, int timezoneOffset);
    public Task<int> GetUserTimezoneOffsetAsync(long userId);

    public Task<double?> GetMaxKcalAsync(long userId);
    public Task<bool> SetMaxKcalAsync(long userId, double? maxKcal);
}