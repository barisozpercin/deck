using Deck.Shell.Calendars;

namespace Deck.Shell.Tests;

public class MeetingLinksTests
{
    [Theory]
    [InlineData("Join: https://meet.google.com/abc-defg-hij now", "https://meet.google.com/abc-defg-hij")]
    [InlineData("https://us02web.zoom.us/j/123456789?pwd=abcDEF", "https://us02web.zoom.us/j/123456789?pwd=abcDEF")]
    [InlineData("https://zoom.us/j/987.", "https://zoom.us/j/987")]
    [InlineData("<https://teams.microsoft.com/l/meetup-join/19%3ameeting_x%40thread.v2/0?context=%7b%7d>", "https://teams.microsoft.com/l/meetup-join/19%3ameeting_x%40thread.v2/0?context=%7b%7d")]
    [InlineData("Webex: https://acme.webex.com/meet/jdoe", "https://acme.webex.com/meet/jdoe")]
    public void Finds_the_join_link(string text, string expected) =>
        Assert.Equal(expected, MeetingLinks.Find(text));

    [Fact]
    public void Returns_null_when_there_is_no_meeting()
    {
        Assert.Null(MeetingLinks.Find("Lunch at the usual place", null, "", "https://example.com/menu"));
    }

    [Fact]
    public void Earlier_fields_win()
    {
        Assert.Equal(
            "https://meet.google.com/aaa-bbbb-ccc",
            MeetingLinks.Find("https://meet.google.com/aaa-bbbb-ccc", "https://zoom.us/j/1"));
        Assert.Equal("https://zoom.us/j/1", MeetingLinks.Find(null, "https://zoom.us/j/1"));
    }
}
