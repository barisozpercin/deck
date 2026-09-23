using Deck.Shell.Widgets;

namespace Deck.Shell.Tests;

public class SharedServiceTests
{
    private sealed class Counting : SharedService
    {
        public int Starts;
        public int Stops;
        protected override void OnStart() => Starts++;
        protected override void OnStop() => Stops++;
    }

    [Fact]
    public void Starts_with_the_first_user_and_stops_after_the_last()
    {
        var service = new Counting();

        service.Acquire();
        service.Acquire();
        Assert.Equal(1, service.Starts);

        service.Release();
        Assert.Equal(0, service.Stops);
        Assert.True(service.IsRunning);

        service.Release();
        Assert.Equal(1, service.Stops);
        Assert.False(service.IsRunning);
    }

    [Fact]
    public void Extra_releases_are_ignored()
    {
        var service = new Counting();

        service.Release();
        Assert.Equal(0, service.Stops);

        service.Acquire();
        Assert.Equal(1, service.Starts);
    }
}
