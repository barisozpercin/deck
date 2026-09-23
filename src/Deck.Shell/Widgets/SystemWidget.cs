using Deck.Shell.Stats;

namespace Deck.Shell.Widgets;

internal sealed class SystemWidget(WidgetContext context) : WidgetBase(context, "system")
{
    private readonly SystemStats _system = new();
    private GpuStats? _gpu;
    private bool _acquired;

    public override void Start()
    {
        Context.Tick.Ticked += Sample;
        Context.Tick.Acquire();
        _acquired = true;
        _gpu = new GpuStats();
    }

    public override void Stop()
    {
        if (!_acquired) return;

        Context.Tick.Ticked -= Sample;
        Context.Tick.Release();
        _acquired = false;
        _gpu?.Dispose();
        _gpu = null;
    }

    public override void Push()
    {
        bool hasGpu = _gpu is { IsAvailable: true };

        Post(new
        {
            cpu = Math.Round(_system.CpuPercent),
            ram = Math.Round(_system.RamPercent),
            gpuAvailable = hasGpu,
            gpu = hasGpu ? Math.Round(_gpu!.GpuPercent) : 0
        });
    }

    private void Sample()
    {
        _system.Sample();
        _gpu?.Sample();
        Push();
    }
}
