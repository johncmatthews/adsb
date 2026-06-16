using System.Reflection;
using System.Runtime.InteropServices;

namespace Adsb.Radio;

public static class RtlSdrNative
{
    private const string LibraryName = "rtlsdr";
    private static int resolverRegistered;
    private static string? explicitLibraryPath;

    public static void Configure(string? libraryPath)
    {
        explicitLibraryPath = string.IsNullOrWhiteSpace(libraryPath)
            ? Environment.GetEnvironmentVariable("RTLSDR_LIBRARY")
            : libraryPath;

        if (Interlocked.Exchange(ref resolverRegistered, 1) == 0)
        {
            NativeLibrary.SetDllImportResolver(typeof(RtlSdrNative).Assembly, ResolveLibrary);
        }
    }

    [DllImport(LibraryName, EntryPoint = "rtlsdr_get_device_count", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint GetDeviceCount();

    [DllImport(LibraryName, EntryPoint = "rtlsdr_get_device_usb_strings", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetDeviceUsbStrings(uint index, byte[] manufacturer, byte[] product, byte[] serial);

    [DllImport(LibraryName, EntryPoint = "rtlsdr_open", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Open(out IntPtr device, uint index);

    [DllImport(LibraryName, EntryPoint = "rtlsdr_close", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Close(IntPtr device);

    [DllImport(LibraryName, EntryPoint = "rtlsdr_set_center_freq", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetCenterFrequency(IntPtr device, uint frequency);

    [DllImport(LibraryName, EntryPoint = "rtlsdr_set_sample_rate", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetSampleRate(IntPtr device, uint rate);

    [DllImport(LibraryName, EntryPoint = "rtlsdr_set_freq_correction", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetFrequencyCorrection(IntPtr device, int ppm);

    [DllImport(LibraryName, EntryPoint = "rtlsdr_set_tuner_gain_mode", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetTunerGainMode(IntPtr device, int manual);

    [DllImport(LibraryName, EntryPoint = "rtlsdr_set_tuner_gain", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetTunerGain(IntPtr device, int gain);

    [DllImport(LibraryName, EntryPoint = "rtlsdr_get_tuner_gains", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetTunerGains(IntPtr device, IntPtr gains);

    [DllImport(LibraryName, EntryPoint = "rtlsdr_set_agc_mode", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetAgcMode(IntPtr device, int on);

    [DllImport(LibraryName, EntryPoint = "rtlsdr_reset_buffer", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ResetBuffer(IntPtr device);

    [DllImport(LibraryName, EntryPoint = "rtlsdr_read_async", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ReadAsync(
        IntPtr device,
        ReadAsyncCallback callback,
        IntPtr context,
        uint bufferCount,
        uint bufferLength);

    [DllImport(LibraryName, EntryPoint = "rtlsdr_cancel_async", CallingConvention = CallingConvention.Cdecl)]
    public static extern int CancelAsync(IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ReadAsyncCallback(IntPtr buffer, uint length, IntPtr context);

    public static void ThrowIfError(int result, string operation)
    {
        if (result < 0)
        {
            throw new RtlSdrException(operation, result);
        }
    }

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!libraryName.Equals(LibraryName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        foreach (var candidate in GetLibraryCandidates())
        {
            if (Path.IsPathRooted(candidate))
            {
                if (NativeLibrary.TryLoad(candidate, out var absoluteHandle))
                {
                    return absoluteHandle;
                }
            }
            else if (NativeLibrary.TryLoad(candidate, assembly, searchPath, out var relativeHandle))
            {
                return relativeHandle;
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> GetLibraryCandidates()
    {
        if (!string.IsNullOrWhiteSpace(explicitLibraryPath))
        {
            yield return explicitLibraryPath;
        }

        if (OperatingSystem.IsWindows())
        {
            yield return "rtlsdr.dll";
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return "librtlsdr.dylib";
            yield return "/usr/local/lib/librtlsdr.dylib";
            yield return "/usr/local/opt/librtlsdr/lib/librtlsdr.dylib";
            yield return "/opt/homebrew/lib/librtlsdr.dylib";
            yield return "/opt/homebrew/opt/librtlsdr/lib/librtlsdr.dylib";
            yield break;
        }

        yield return "librtlsdr.so.2";
        yield return "librtlsdr.so";
        yield return "/usr/lib/librtlsdr.so";
        yield return "/usr/local/lib/librtlsdr.so";
    }
}
