namespace TelegramBot;

public class UserRow
{
    public long Id { get; set; }
    public DateTime RegisterDate { get; set; }
    public DateTime? BanDate { get; set; }
    public int? DateTimeOffset { get; set; }
    public double? MinKcal { get; set; }
    public double? MaxKcal { get; set; }

    public ICollection<ConsumedRow>? ConsumedItems { get; set; }
}