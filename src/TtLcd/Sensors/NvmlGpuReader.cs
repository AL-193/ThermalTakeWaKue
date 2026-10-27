using System.Runtime.InteropServices;
using System.Text;

namespace TtLcd.Sensors;

/// <summary>
/// NVIDIA GPU sensors on Linux through libnvidia-ml (ships with the driver).
/// Loaded dynamically so machines without NVIDIA hardware are unaffected.
/// </summary>
internal sealed class NvmlGpuReader : IDisposable
{
    private const int Success = 0;
    private const int TemperatureGpu = 0;
    private const int ClockGraphics = 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CountFn(out uint count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HandleFn(uint index, out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NameFn(IntPtr device, byte[] name, uint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ValueByTypeFn(IntPtr device, int type, out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ValueFn(IntPtr device, out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int UtilizationFn(IntPtr device, out Utilization utilization);

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public uint Gpu;
        public uint Memory;
    }

    private readonly IntPtr _library;
    private readonly InitFn _shutdown;
    private readonly ValueByTypeFn _temperature;
    private readonly ValueByTypeFn _clock;
    private readonly ValueFn _power;
    private readonly UtilizationFn _utilization;
    private readonly List<(IntPtr Handle, string Name)> _devices = [];

    private NvmlGpuReader(IntPtr library)
    {
        _library = library;
        Get<InitFn>("nvmlInit_v2")().ThrowIfFailed();
        _shutdown = Get<InitFn>("nvmlShutdown");
        _temperature = Get<ValueByTypeFn>("nvmlDeviceGetTemperature");
        _clock = Get<ValueByTypeFn>("nvmlDeviceGetClockInfo");
        _power = Get<ValueFn>("nvmlDeviceGetPowerUsage");
        _utilization = Get<UtilizationFn>("nvmlDeviceGetUtilizationRates");

        var getHandle = Get<HandleFn>("nvmlDeviceGetHandleByIndex_v2");
        var getName = Get<NameFn>("nvmlDeviceGetName");
        Get<CountFn>("nvmlDeviceGetCount_v2")(out var count).ThrowIfFailed();
        for (uint i = 0; i < count; i++)
        {
            if (getHandle(i, out var handle) != Success)
                continue;
            var buffer = new byte[96];
            var name = getName(handle, buffer, (uint)buffer.Length) == Success
                ? Encoding.ASCII.GetString(buffer).TrimEnd('\0')
                : $"NVIDIA GPU {i}";
            _devices.Add((handle, name));
        }
    }

    public static NvmlGpuReader? TryCreate()
    {
        if (!NativeLibrary.TryLoad("libnvidia-ml.so.1", out var library))
            return null;
        try
        {
            return new NvmlGpuReader(library);
        }
        catch (Exception e) when (e is InvalidOperationException or EntryPointNotFoundException)
        {
            NativeLibrary.Free(library);
            return null;
        }
    }

    public void Read(List<SensorReading> list)
    {
        for (int i = 0; i < _devices.Count; i++)
        {
            var (handle, name) = _devices[i];
            var prefix = $"/nvml/{i}";

            double? temperature = _temperature(handle, TemperatureGpu, out var t) == Success ? t : null;
            double? load = _utilization(handle, out var u) == Success ? u.Gpu : null;
            double? power = _power(handle, out var mw) == Success ? mw / 1000.0 : null;
            double? clock = _clock(handle, ClockGraphics, out var mhz) == Success ? mhz : null;

            list.Add(new($"{prefix}/temperature/core", name, HardwareKind.Gpu, "GPU Core", SensorKind.Temperature, temperature));
            list.Add(new($"{prefix}/load/core", name, HardwareKind.Gpu, "GPU Core", SensorKind.Load, load));
            list.Add(new($"{prefix}/power/board", name, HardwareKind.Gpu, "GPU Power", SensorKind.Power, power));
            list.Add(new($"{prefix}/clock/core", name, HardwareKind.Gpu, "GPU Core", SensorKind.Clock, clock));
        }
    }

    private T Get<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, name));

    public void Dispose()
    {
        _shutdown();
        NativeLibrary.Free(_library);
    }
}

internal static class NvmlResultExtensions
{
    public static void ThrowIfFailed(this int result)
    {
        if (result != 0)
            throw new InvalidOperationException($"NVML call failed with code {result}.");
    }
}
