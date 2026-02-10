using System.Text.RegularExpressions;

namespace TelegramBot.Tests;

public class CommandParsingTests
{
    private static readonly Regex CommandRegex = WeightBot.CommandRegex;
    private static readonly Regex AddRegex = WeightBot.AddRegex;

    [Theory]
    [InlineData("add bread, 150", "add", "bread, 150")]
    [InlineData("/add bread, 150", "add", "bread, 150")]
    [InlineData("stat", "stat", "")]
    [InlineData("/stat", "stat", "")]
    [InlineData("  /start  ", "start", "")]
    [InlineData("help", "help", "")]
    [InlineData("remove 6", "remove", "6")]
    [InlineData("timezone +7", "timezone", "+7")]
    [InlineData("limit 2500", "limit", "2500")]
    public void CommandRegex_ValidCommands_MatchesCorrectly(string input, string expectedCmd, string expectedArgs)
    {
        Match m = CommandRegex.Match(input);
        Assert.True(m.Success);
        Assert.Equal(expectedCmd, m.Groups["cmd"].Value);
        Assert.Equal(expectedArgs, m.Groups["args"].Value);
    }

    [Theory]
    [InlineData("добавить каша, 12", "добавить", "каша, 12")]
    [InlineData("доб каша 100", "доб", "каша 100")]
    [InlineData("д 50", "д", "50")]
    [InlineData("стат", "стат", "")]
    [InlineData("дейстат", "дейстат", "")]
    [InlineData("лонгстат", "лонгстат", "")]
    [InlineData("удалить 3", "удалить", "3")]
    [InlineData("дел 5", "дел", "5")]
    public void CommandRegex_RussianCommands_MatchesCorrectly(string input, string expectedCmd, string expectedArgs)
    {
        Match m = CommandRegex.Match(input);
        Assert.True(m.Success);
        Assert.Equal(expectedCmd, m.Groups["cmd"].Value);
        Assert.Equal(expectedArgs, m.Groups["args"].Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!invalid")]
    [InlineData("@#$")]
    public void CommandRegex_InvalidInput_DoesNotMatch(string input)
    {
        Match m = CommandRegex.Match(input);
        Assert.False(m.Success);
    }

    [Theory]
    [InlineData("bread, 150", "bread", "150")]
    [InlineData("молочная каша, 12", "молочная каша", "12")]
    [InlineData("bread 150", "bread", "150")]
    [InlineData("bread, 150.5", "bread", "150.5")]
    [InlineData("bread, 150,5", "bread", "150,5")]
    [InlineData("bread", "bread", "")]
    public void AddRegex_ValidInput_MatchesCorrectly(string input, string expectedName, string expectedKcal)
    {
        Match m = AddRegex.Match(input);
        Assert.True(m.Success);
        Assert.Equal(expectedName, m.Groups["name"].Value);
        Assert.Equal(expectedKcal, m.Groups["kcal"].Value);
    }

    [Theory]
    [InlineData("100", true, 100.0)]
    [InlineData("100.5", true, 100.5)]
    [InlineData("100,5", true, 100.5)]
    [InlineData("", false, 0)]
    [InlineData("   ", false, 0)]
    public void TryParseDouble_VariousInputs_ParsesCorrectly(string input, bool expectedSuccess, double expectedValue)
    {
        bool success = WeightBot.TryParseDouble(input, out double result);
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(expectedValue, result, 2);
        }
    }
}
