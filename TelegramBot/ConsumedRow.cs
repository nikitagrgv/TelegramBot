namespace TelegramBot;

public class ConsumedRow
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public DateTime Date { get; set; }
    public string Text { get; set; }
    public double? Kcal { get; set; }

    public UserRow? User { get; set; }
}