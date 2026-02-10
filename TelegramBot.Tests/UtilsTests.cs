namespace TelegramBot.Tests;

public class UtilsTests
{
    [Fact]
    public void SplitStringByChunks_ExactMultiple_ReturnsCorrectChunks()
    {
        var result = Utils.SplitStringByChunks("abcdef", 3).ToList();
        Assert.Equal(["abc", "def"], result);
    }

    [Fact]
    public void SplitStringByChunks_NotExactMultiple_ReturnsRemainderAsLastChunk()
    {
        var result = Utils.SplitStringByChunks("abcdefg", 3).ToList();
        Assert.Equal(["abc", "def", "g"], result);
    }

    [Fact]
    public void SplitStringByChunks_ShorterThanMaxLen_ReturnsSingleChunk()
    {
        var result = Utils.SplitStringByChunks("ab", 5).ToList();
        Assert.Equal(["ab"], result);
    }

    [Fact]
    public void SplitStringByChunks_EmptyString_ReturnsEmpty()
    {
        var result = Utils.SplitStringByChunks("", 3).ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void SplitStringByChunks_MaxLenOne_ReturnsSingleCharChunks()
    {
        var result = Utils.SplitStringByChunks("abc", 1).ToList();
        Assert.Equal(["a", "b", "c"], result);
    }

    [Fact]
    public void SplitStringByChunks_MaxLenZero_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Utils.SplitStringByChunks("abc", 0).ToList());
    }
}
