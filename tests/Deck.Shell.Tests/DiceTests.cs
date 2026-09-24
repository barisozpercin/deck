using Deck.Shell.Widgets;

namespace Deck.Shell.Tests;

public class DiceTests
{
    [Theory]
    [InlineData("d6", "1", true)]
    [InlineData("d6", "6", true)]
    [InlineData("d6", "7", false)]
    [InlineData("d6", "0", false)]
    [InlineData("d20", "20", true)]
    [InlineData("d20", "21", false)]
    [InlineData("coin", "heads", true)]
    [InlineData("coin", "tails", true)]
    [InlineData("coin", "3", false)]
    [InlineData("d6", "heads", false)]
    public void Only_results_the_mode_could_produce_are_kept(string mode, string value, bool valid) =>
        Assert.Equal(valid, DiceWidget.IsResult(mode, value));
}
