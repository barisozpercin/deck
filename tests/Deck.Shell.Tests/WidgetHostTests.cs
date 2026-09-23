using Deck.Shell.Layout;
using Deck.Shell.Widgets;

namespace Deck.Shell.Tests;

internal sealed class FakeWidget(string kind, string? reference, bool failOnStart) : IWidget
{
    public string Kind { get; } = kind;
    public string? Ref { get; } = reference;
    public string Variant { get; set; } = "";
    public int Starts;
    public int Stops;
    public int Pushes;
    public List<string> Messages { get; } = [];

    public void Start()
    {
        Starts++;
        if (failOnStart) throw new InvalidOperationException("boom");
    }

    public void Stop() => Stops++;
    public void Push() => Pushes++;

    public bool Handle(string message)
    {
        Messages.Add(message);
        return true;
    }

    public bool HandleHotkey(string action) => action == "hk:" + Kind;
}

public class WidgetHostTests
{
    private readonly List<FakeWidget> _created = [];
    private readonly List<(string Kind, string? Ref)> _failures = [];

    private WidgetHost NewHost(Func<WidgetPlacement, bool>? fails = null) => new(
        p =>
        {
            var widget = new FakeWidget(p.Kind, p.Ref, fails?.Invoke(p) ?? false);
            _created.Add(widget);
            return widget;
        },
        (kind, reference) => _failures.Add((kind, reference)));

    private static WidgetPlacement P(string kind, string? reference = null, string variant = "standard") =>
        new(kind, variant, reference, 0, 0);

    [Fact]
    public void Starts_and_pushes_newly_placed_widgets_with_their_variant()
    {
        var host = NewHost();

        host.Sync([P("mixer", variant: "short")]);

        var widget = Assert.Single(_created);
        Assert.Equal(1, widget.Starts);
        Assert.Equal(1, widget.Pushes);
        Assert.Equal("short", widget.Variant);
    }

    [Fact]
    public void Stops_widgets_that_left_the_deck_and_keeps_the_rest_running()
    {
        var host = NewHost();
        host.Sync([P("clock"), P("mic")]);

        host.Sync([P("mic")]);

        Assert.Equal(1, _created[0].Stops);
        Assert.Equal(0, _created[1].Stops);
        Assert.Equal(1, _created[1].Starts);
        Assert.Equal(2, _created.Count);
    }

    [Fact]
    public void Routes_messages_only_to_widgets_on_the_deck()
    {
        var host = NewHost();
        host.Sync([P("mic")]);

        Assert.True(host.Route("mic", null, "press"));
        Assert.False(host.Route("noise", null, "press"));
        Assert.Equal(new[] { "press" }, _created[0].Messages);
    }

    [Fact]
    public void Presets_are_told_apart_by_reference()
    {
        var host = NewHost();
        host.Sync([P("preset", "a"), P("preset", "b")]);

        host.Route("preset", "b", "press");

        Assert.Empty(_created[0].Messages);
        Assert.Single(_created[1].Messages);
    }

    [Fact]
    public void A_widget_that_fails_to_start_is_reported_and_not_retried_until_replaced()
    {
        var host = NewHost(fails: p => p.Kind == "claude");

        host.Sync([P("claude"), P("clock")]);

        Assert.Equal(new[] { ("claude", (string?)null) }, _failures);
        Assert.False(host.Route("claude", null, "press"));
        Assert.True(host.Route("clock", null, "tick"));

        host.PushAll();
        Assert.Equal(2, _failures.Count);

        host.Sync([P("claude"), P("clock")]);
        Assert.Equal(2, _created.Count);

        host.Sync([P("clock")]);
        host.Sync([P("claude"), P("clock")]);
        Assert.Equal(3, _created.Count);
    }

    [Fact]
    public void Hotkeys_reach_only_the_widget_that_claims_them()
    {
        var host = NewHost();
        host.Sync([P("mic")]);

        Assert.True(host.Hotkey("hk:mic"));
        Assert.False(host.Hotkey("hk:noise"));
    }

    [Fact]
    public void StopAll_stops_everything()
    {
        var host = NewHost();
        host.Sync([P("mic"), P("clock")]);

        host.StopAll();

        Assert.All(_created, w => Assert.Equal(1, w.Stops));
        Assert.False(host.Route("mic", null, "press"));
    }
}
