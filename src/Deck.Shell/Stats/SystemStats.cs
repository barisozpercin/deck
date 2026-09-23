using System.Runtime.InteropServices;
using Deck.Shell.Interop;

namespace Deck.Shell.Stats;

/// <summary>CPU and memory load. Sampled by delta, so the first reading is meaningless.</summary>
internal sealed class SystemStats
{
    private long _lastIdle;
    private long _lastKernel;
    private long _lastUser;
    private bool _primed;

    public double CpuPercent { get; private set; }
    public double RamPercent { get; private set; }
    public double RamUsedGb { get; private set; }
    public double RamTotalGb { get; private set; }

    public void Sample()
    {
        SampleCpu();
        SampleMemory();
    }

    private void SampleCpu()
    {
        if (!SystemNative.GetSystemTimes(out long idle, out long kernel, out long user)) return;

        if (_primed)
        {
            // Kernel time already includes idle, so total is kernel + user.
            long idleDelta = idle - _lastIdle;
            long totalDelta = (kernel - _lastKernel) + (user - _lastUser);

            if (totalDelta > 0)
            {
                double busy = totalDelta - idleDelta;
                CpuPercent = Math.Clamp(busy / totalDelta * 100.0, 0, 100);
            }
        }

        _lastIdle = idle;
        _lastKernel = kernel;
        _lastUser = user;
        _primed = true;
    }

    private void SampleMemory()
    {
        var status = new SystemNative.MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<SystemNative.MEMORYSTATUSEX>()
        };

        if (!SystemNative.GlobalMemoryStatusEx(ref status)) return;

        RamPercent = status.dwMemoryLoad;
        RamTotalGb = status.ullTotalPhys / 1024d / 1024d / 1024d;
        RamUsedGb = (status.ullTotalPhys - status.ullAvailPhys) / 1024d / 1024d / 1024d;
    }
}
