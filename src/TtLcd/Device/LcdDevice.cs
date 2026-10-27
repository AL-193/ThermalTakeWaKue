using HidSharp;

namespace TtLcd.Device;

/// <summary>The segment LCD on the water block, a vendor-defined HID device.</summary>
public sealed class LcdDevice : IDisposable
{
    public const int VendorId = 0x5131;
    public const int ProductId = 0x2007;

    private HidStream? _stream;
    private int _reportLength;

    public bool IsConnected => _stream is not null;

    public string? DevicePath { get; private set; }

    public bool TryConnect()
    {
        Close();
        var device = DeviceList.Local
            .GetHidDevices(VendorId, ProductId)
            .FirstOrDefault(d => d.GetMaxOutputReportLength() > 0);
        if (device is null || !device.TryOpen(out HidStream stream))
            return false;

        stream.WriteTimeout = 1000;
        _stream = stream;
        // Includes the leading report ID byte, so 65 for this device.
        _reportLength = device.GetMaxOutputReportLength();
        DevicePath = device.DevicePath;
        return true;
    }

    /// <summary>Writes one 64-byte packet. Returns false and disconnects on failure.</summary>
    public bool Send(ReadOnlySpan<byte> packet)
    {
        if (_stream is null)
            return false;

        // The device has no report IDs; HID APIs still expect a leading 0.
        var report = new byte[Math.Max(_reportLength, packet.Length + 1)];
        packet.CopyTo(report.AsSpan(1));
        try
        {
            _stream.Write(report);
            return true;
        }
        catch (Exception e) when (e is IOException or TimeoutException or ObjectDisposedException)
        {
            Close();
            return false;
        }
    }

    private void Close()
    {
        _stream?.Dispose();
        _stream = null;
        DevicePath = null;
    }

    public void Dispose() => Close();
}
