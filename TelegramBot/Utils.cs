namespace TelegramBot;

public static class Utils
{
    public static IEnumerable<string> SplitStringByChanks(string str, int maxLen)
    {
        if (maxLen < 1)
        {
            throw new ArgumentException("maxLen must be at least 1", nameof(maxLen));
        }

        for (int i = 0; i < str.Length; i += maxLen)
        {
            int chunkSize = Math.Min(maxLen, str.Length - i);
            yield return str.Substring(i, chunkSize);
        }
    }
}