namespace Deck.Shell.Widgets;

/// <summary>An indicator only: there is no camera switch that could be trusted to have worked.</summary>
internal sealed class CameraWidget(WidgetContext context) : WidgetBase(context, "camera")
{
    public override void Start()
    {
        Context.Privacy.Changed += Push;
        Context.Privacy.Acquire();
    }

    public override void Stop()
    {
        Context.Privacy.Changed -= Push;
        Context.Privacy.Release();
    }

    public override void Push()
    {
        var camera = Context.Privacy.Camera;
        Post(new { inUse = camera.InUse, apps = camera.Describe() });
    }
}
