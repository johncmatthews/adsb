using System.Reflection;
using System.Runtime.InteropServices;

namespace Adsb.Radio;

/// <summary>
/// P/Invoke declarations and library resolution logic for the native librtlsdr API.
/// </summary>
public static class RtlSdrNative
{
    private const string LibraryName = "rtlsdr";
    private static int resolverRegistered;
    private static string? explicitLibraryPath;

    /// <summary>
    /// Registers the native library resolver and optionally pins loading to a user-supplied librtlsdr path.
    /// </summary>
    /// <param name="libraryPath">Explicit native library path, or null to use environment and platform defaults.</param>
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

    /// <summary>Returns the number of RTL-SDR devices visible to librtlsdr.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_get_device_count", CallingConvention = CallingConvention.Cdecl)]
    public static extern uint GetDeviceCount();

    /// <summary>Reads manufacturer, product, and serial strings for an RTL-SDR device index.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_get_device_usb_strings", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetDeviceUsbStrings(uint index, byte[] manufacturer, byte[] product, byte[] serial);

    /// <summary>Opens an RTL-SDR device and returns a native handle.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_open", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Open(out IntPtr device, uint index);

    /// <summary>Closes a native RTL-SDR device handle.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_close", CallingConvention = CallingConvention.Cdecl)]
    public static extern int Close(IntPtr device);

    /// <summary>Tunes the device to the requested center frequency in hertz.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_set_center_freq", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetCenterFrequency(IntPtr device, uint frequency);

    /// <summary>Sets the device sample rate in samples per second.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_set_sample_rate", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetSampleRate(IntPtr device, uint rate);

    /// <summary>Applies tuner frequency correction in parts per million.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_set_freq_correction", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetFrequencyCorrection(IntPtr device, int ppm);

    /// <summary>Switches between automatic and manual tuner gain modes.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_set_tuner_gain_mode", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetTunerGainMode(IntPtr device, int manual);

    /// <summary>Sets manual tuner gain in tenths of a decibel.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_set_tuner_gain", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetTunerGain(IntPtr device, int gain);

    /// <summary>Gets the supported manual tuner gains, or the count when the destination pointer is null.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_get_tuner_gains", CallingConvention = CallingConvention.Cdecl)]
    public static extern int GetTunerGains(IntPtr device, IntPtr gains);

    /// <summary>Enables or disables tuner automatic gain control.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_set_agc_mode", CallingConvention = CallingConvention.Cdecl)]
    public static extern int SetAgcMode(IntPtr device, int on);

    /// <summary>Clears queued samples before starting a new receive loop.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_reset_buffer", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ResetBuffer(IntPtr device);

    /// <summary>Starts librtlsdr asynchronous reads and invokes the callback for each native sample buffer.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_read_async", CallingConvention = CallingConvention.Cdecl)]
    public static extern int ReadAsync(
        IntPtr device,
        ReadAsyncCallback callback,
        IntPtr context,
        uint bufferCount,
        uint bufferLength);

    /// <summary>Requests cancellation of a blocking librtlsdr asynchronous read loop.</summary>
    [DllImport(LibraryName, EntryPoint = "rtlsdr_cancel_async", CallingConvention = CallingConvention.Cdecl)]
    public static extern int CancelAsync(IntPtr device);

    /// <summary>
    /// Callback signature used by librtlsdr to expose a native sample buffer.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ReadAsyncCallback(IntPtr buffer, uint length, IntPtr context);

    /// <summary>
    /// Converts negative librtlsdr return codes into exceptions with operation context.
    /// </summary>
    public static void ThrowIfError(int result, string operation)
    {
        if (result < 0)
        {
            throw new RtlSdrException(operation, result);
        }
    }

    /// <summary>
    /// Resolves the abstract DllImport name to an explicit path or a platform-specific librtlsdr filename.
    /// </summary>
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

    /// <summary>
    /// Enumerates candidate native library names and Homebrew/package-manager locations for each supported platform.
    /// </summary>
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
