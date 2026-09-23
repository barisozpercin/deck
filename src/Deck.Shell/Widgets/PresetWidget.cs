using Deck.Shell.Layout;
using Deck.Shell.Presets;

namespace Deck.Shell.Widgets;

/// <summary>One saved window layout. Deleting it is the main window's job, since that changes the config's list.</summary>
internal sealed class PresetWidget(WidgetContext context, string id) : WidgetBase(context, "preset", id)
{
    private string _state = "idle";
    private string? _summary;

    private Preset? Preset => Context.Config.Presets.FirstOrDefault(p => p.Id == Ref);

    public override bool Handle(string message)
    {
        if (message != "press") return false;

        _ = RunAsync();
        return true;
    }

    public override bool HandleHotkey(string action)
    {
        if (action != WidgetCatalog.PresetActionPrefix + Ref) return false;

        _ = RunAsync();
        return true;
    }

    public override void Push()
    {
        if (Preset is not { } preset) return;

        Post(new
        {
            name = preset.Name,
            count = preset.Entries.Count,
            state = _state,
            summary = _summary
        });
    }

    private async Task RunAsync()
    {
        if (Preset is not { } preset) return;

        _state = "running";
        Push();

        try
        {
            var report = await new PresetRunner().RunAsync(preset);
            _state = "done";
            _summary = report.Summary();
            Push();

            if (report.Failed.Count > 0)
            {
                Context.Notifier.Show($"{preset.Name}: {report.Failed.Count} didn't work",
                    string.Join("\n", report.Failed.Take(4)));
            }
        }
        catch (Exception ex)
        {
            _state = "error";
            _summary = ex.Message;
            Push();
        }
    }
}
