using PhotoQuickSelector.Core;
using Xunit;

namespace PhotoQuickSelector.Core.Tests;

public class BatchSafetyTests
{
    [Theory]
    [InlineData("100%OK.jpg")]
    [InlineData("A&B.jpg")]
    [InlineData("A^B.jpg")]
    public void ContainsUnsafe_DetectsEachUnsafeChar(string value)
    {
        Assert.True(BatchSafety.ContainsUnsafe(value));
    }

    [Theory]
    [InlineData("DSC001 - コピー.JPG")]
    [InlineData("写真フォルダ")]
    [InlineData("photo (1).jpg")]
    [InlineData("重要!.jpg")]
    public void ContainsUnsafe_AllowsSpacesJapaneseParensAndBang(string value)
    {
        Assert.False(BatchSafety.ContainsUnsafe(value));
    }

    [Fact]
    public void ContainsUnsafe_NullOrEmpty_IsSafe()
    {
        Assert.False(BatchSafety.ContainsUnsafe(null));
        Assert.False(BatchSafety.ContainsUnsafe(""));
    }

    [Fact]
    public void FindUnsafe_ReturnsOnlyUnsafeValues_InOrder()
    {
        var values = new[] { "safe.jpg", "100%.jpg", null, "ok (1).jpg", "A&B.jpg", "C^D.jpg" };
        var result = BatchSafety.FindUnsafe(values);

        Assert.Equal(new[] { "100%.jpg", "A&B.jpg", "C^D.jpg" }, result);
    }

    [Fact]
    public void FindUnsafe_AllSafe_ReturnsEmpty()
    {
        var values = new[] { "safe.jpg", "ok (1).jpg", "写真.jpg" };
        Assert.Empty(BatchSafety.FindUnsafe(values));
    }
}
