using Deck.Shell.Layout;

namespace Deck.Shell.Widgets;

internal abstract class WidgetBase : IWidget
{
    protected WidgetBase(WidgetContext context, string kind, string? reference = null)
    {
        Context = context;
        Kind = kind;
        Ref = reference;
    }

    protected WidgetContext Context { get; }

    public string Kind { get; }

    public string? Ref { get; }

    public string Variant { get; set; } = WidgetCatalog.Standard;

    public virtual void Start()
    {
    }

    public virtual void Stop()
    {
    }

    public abstract void Push();

    public virtual bool Handle(string message) => false;

    public virtual bool HandleHotkey(string action) => false;

    protected void Post(object data) => Context.Post(Kind, Ref, data);
}
