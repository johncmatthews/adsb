using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace Adsb.Radio;

public sealed class RtlSdrDevice : IDisposable
{
    private readonly AppOptions options;
    private readonly IntPtr handle;
    private bool disposed;
    private RtlSdrNative.ReadAsyncCallback? callback;

    private RtlSdrDevice(AppOptions options, IntPtr handle)
    {
        this.options = options;
        this.handle = handle;
    }

    public static IReadOnlyList<RtlSdrDeviceInfo> ListDevices()
    {
        var count = RtlSdrNative.GetDeviceCount();
        var devices = new List<RtlSdrDeviceInfo>((int)count);

        for (uint i = 0; i < count; i++)
        {
            var manufacturer = new byte[256];
            var product = new byte[256];
            var serial = new byte[256];
            var result = RtlSdrNative.GetDeviceUsbStrings(i, manufacturer, product, serial);
            if (result < 0)
            {
                devices.Add(new RtlSdrDeviceInfo(i, "RTL-SDR", $"Device {i}", "unknown"));
                continue;
            }

            devices.Add(
                new RtlSdrDeviceInfo(
                    i,
                    ReadCString(manufacturer),
                    ReadCString(product),
                    ReadCString(serial)));
        }

        return devices;
    }

    public static RtlSdrDevice Open(AppOptions options)
    {
        var result = RtlSdrNative.Open(out var handle, options.DeviceIndex);
        RtlSdrNative.ThrowIfError(result, $"Failed to open RTL-SDR device {options.DeviceIndex}");
        return new RtlSdrDevice(options, handle);
    }

    public void Configure()
    {
        EnsureNotDisposed();

        RtlSdrNative.ThrowIfError(
            RtlSdrNative.SetSampleRate(handle, options.SampleRate),
            $"Failed to set sample rate to {options.SampleRate}");

        RtlSdrNative.ThrowIfError(
            RtlSdrNative.SetCenterFrequency(handle, options.FrequencyHz),
            $"Failed to tune to {options.FrequencyHz}");

        if (options.FrequencyCorrectionPpm != 0)
        {
            RtlSdrNative.ThrowIfError(
                RtlSdrNative.SetFrequencyCorrection(handle, options.FrequencyCorrectionPpm),
                $"Failed to set frequency correction to {options.FrequencyCorrectionPpm} PPM");
        }

        ConfigureGain();

        RtlSdrNative.ThrowIfError(RtlSdrNative.ResetBuffer(handle), "Failed to reset RTL-SDR sample buffer");
    }

    public async Task RunAsync(
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> onSamples,
        CancellationToken cancellationToken)
    {
        EnsureNotDisposed();

        var channel = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions((int)options.AsyncBufferCount)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

        callback = (buffer, length, _) =>
        {
            var managed = GC.AllocateUninitializedArray<byte>((int)length);
            Marshal.Copy(buffer, managed, 0, (int)length);
            channel.Writer.TryWrite(managed);
        };

        using var registration = cancellationToken.Register(() => RtlSdrNative.CancelAsync(handle));

        var readTask = Task.Run(
            () =>
            {
                var result = RtlSdrNative.ReadAsync(
                    handle,
                    callback,
                    IntPtr.Zero,
                    options.AsyncBufferCount,
                    options.AsyncBufferSize);

                if (cancellationToken.IsCancellationRequested || result == 0)
                {
                    channel.Writer.TryComplete();
                }
                else
                {
                    channel.Writer.TryComplete(new RtlSdrException("RTL-SDR async read failed", result));
                }
            },
            CancellationToken.None);

        try
        {
            await foreach (var buffer in channel.Reader.ReadAllAsync(cancellationToken))
            {
                await onSamples(buffer, cancellationToken);
            }
        }
        finally
        {
            RtlSdrNative.CancelAsync(handle);
            await readTask.WaitAsync(CancellationToken.None);
            callback = null;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        RtlSdrNative.CancelAsync(handle);
        RtlSdrNative.Close(handle);
        disposed = true;
    }

    private void ConfigureGain()
    {
        switch (options.Gain.Mode)
        {
            case GainMode.Auto:
                RtlSdrNative.ThrowIfError(RtlSdrNative.SetTunerGainMode(handle, 0), "Failed to enable automatic tuner gain");
                RtlSdrNative.SetAgcMode(handle, 1);
                break;
            case GainMode.Max:
                RtlSdrNative.ThrowIfError(RtlSdrNative.SetTunerGainMode(handle, 1), "Failed to enable manual tuner gain");
                var maxGain = GetAvailableGains().DefaultIfEmpty(496).Max();
                RtlSdrNative.ThrowIfError(RtlSdrNative.SetTunerGain(handle, maxGain), $"Failed to set tuner gain to {maxGain / 10.0:F1} dB");
                break;
            case GainMode.Manual:
                RtlSdrNative.ThrowIfError(RtlSdrNative.SetTunerGainMode(handle, 1), "Failed to enable manual tuner gain");
                RtlSdrNative.ThrowIfError(RtlSdrNative.SetTunerGain(handle, options.Gain.TenthDb), $"Failed to set tuner gain to {options.Gain}");
                break;
            default:
                throw new InvalidOperationException($"Unsupported gain mode: {options.Gain.Mode}");
        }
    }

    private IReadOnlyList<int> GetAvailableGains()
    {
        var count = RtlSdrNative.GetTunerGains(handle, IntPtr.Zero);
        if (count <= 0)
        {
            return Array.Empty<int>();
        }

        var pointer = Marshal.AllocHGlobal(count * sizeof(int));
        try
        {
            var actual = RtlSdrNative.GetTunerGains(handle, pointer);
            if (actual <= 0)
            {
                return Array.Empty<int>();
            }

            var gains = new int[actual];
            Marshal.Copy(pointer, gains, 0, actual);
            return gains;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    private void EnsureNotDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private static string ReadCString(byte[] buffer)
    {
        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0)
        {
            length = buffer.Length;
        }

        return System.Text.Encoding.ASCII.GetString(buffer, 0, length);
    }
}
