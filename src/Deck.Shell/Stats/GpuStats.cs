using System.Runtime.InteropServices;

namespace Deck.Shell.Stats;

/// <summary>
/// GPU load, temperature and VRAM via NVIDIA's management library. nvml.dll ships with the
/// driver and lives in System32, so there is nothing to install and no elevation needed —
/// unlike CPU temperature, which would require a kernel driver.
///
/// Everything degrades to <see cref="IsAvailable"/> = false on a machine without an NVIDIA GPU.
/// </summary>
internal sealed class GpuStats : IDisposable
{
    private const int NVML_SUCCESS = 0;
    private const int NVML_TEMPERATURE_GPU = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong Total;
        public ulong Free;
        public ulong Used;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
    private static extern int NvmlInit();

    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
    private static extern int NvmlShutdown();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    private static extern int NvmlGetHandle(uint index, out IntPtr device);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
    private static extern int NvmlGetUtilization(IntPtr device, out NvmlUtilization utilization);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
    private static extern int NvmlGetTemperature(IntPtr device, int sensorType, out uint temperature);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
    private static extern int NvmlGetMemoryInfo(IntPtr device, out NvmlMemory memory);

    private IntPtr _device;
    private bool _initialised;

    public bool IsAvailable { get; private set; }
    public double GpuPercent { get; private set; }
    public int TemperatureC { get; private set; }
    public double VramUsedGb { get; private set; }
    public double VramTotalGb { get; private set; }

    public GpuStats()
    {
        try
        {
            if (NvmlInit() != NVML_SUCCESS) return;
            _initialised = true;

            if (NvmlGetHandle(0, out _device) != NVML_SUCCESS) return;
            IsAvailable = true;
        }
        catch (DllNotFoundException)
        {
            // No NVIDIA driver on this machine. The tile simply shows no GPU row.
        }
        catch (EntryPointNotFoundException)
        {
            // Driver too old for the v2 entry points.
        }
    }

    public void Sample()
    {
        if (!IsAvailable) return;

        if (NvmlGetUtilization(_device, out var utilization) == NVML_SUCCESS)
            GpuPercent = utilization.Gpu;

        if (NvmlGetTemperature(_device, NVML_TEMPERATURE_GPU, out uint temperature) == NVML_SUCCESS)
            TemperatureC = (int)temperature;

        if (NvmlGetMemoryInfo(_device, out var memory) == NVML_SUCCESS)
        {
            VramUsedGb = memory.Used / 1024d / 1024d / 1024d;
            VramTotalGb = memory.Total / 1024d / 1024d / 1024d;
        }
    }

    public void Dispose()
    {
        if (!_initialised) return;

        try { NvmlShutdown(); }
        catch { }

        _initialised = false;
        IsAvailable = false;
    }
}
