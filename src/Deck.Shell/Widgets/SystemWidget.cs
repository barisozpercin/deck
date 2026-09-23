using Deck.Shell.Stats;

namespace Deck.Shell.Widgets;

internal sealed class SystemWidget(WidgetContext context) : WidgetBase(context, "system")
{
    private readonly SystemStats _system = new();
    private GpuStats? _gpu;

    public override void Start()
    {
        _gpu = new GpuStats();
        Context.Tick.Ticked += Sample;
        Context.Tick.Acquire();
    }

    public override void Stop()
    {
        Context.Tick.Ticked -= Sample;
        Context.Tick.Release();
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
