using Adsb;
using Adsb.Decoding;
using Adsb.Formatting;
using Adsb.Radio;

return await ProgramEntry.RunAsync(args);

internal static class ProgramEntry
{
    public static async Task<int> RunAsync(string[] args)
    {
        AppOptions options;
        try
        {
            options = AppOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            AppOptions.WriteUsage(Console.Error);
            return 2;
        }

        if (options.ShowHelp)
        {
            AppOptions.WriteUsage(Console.Out);
            return 0;
        }

        try
        {
            RtlSdrNative.Configure(options.DriverPath);

            if (options.ListDevices)
            {
                ListDevices();
                return 0;
            }

            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };

            using var device = RtlSdrDevice.Open(options);
            device.Configure();

            var demodulator = new AdsbDemodulator(options.SampleRate);
            var decoder = new ModeSDecoder();
            var tracker = new AircraftStateTracker(options.ReceiverLatitude, options.ReceiverLongitude);

            Console.Error.WriteLine(
                $"Listening on {options.FrequencyHz:N0} Hz at {options.SampleRate:N0} samples/s. Press Ctrl+C to stop.");

            await device.RunAsync(
                (samples, token) =>
                {
                    var receivedAt = DateTimeOffset.UtcNow;
                    foreach (var frame in demodulator.Process(samples.Span, receivedAt))
                    {
                        var message = decoder.Decode(frame);
                        if (!ShouldPrint(message, options))
                        {
                            continue;
                        }

                        var snapshot = tracker.Apply(message);
                        Console.WriteLine(ConsoleMessageFormatter.Format(message, snapshot, options.ShowRawFrames));
                    }

                    return ValueTask.CompletedTask;
                },
                cancellation.Token);

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (DllNotFoundException ex)
        {
            Console.Error.WriteLine("Unable to load librtlsdr.");
            if (!string.IsNullOrWhiteSpace(options.DriverPath))
            {
                Console.Error.WriteLine($"Tried explicit driver path: {options.DriverPath}");
            }

            Console.Error.WriteLine($"Loader detail: {ex.GetType().Name}");
            Console.Error.WriteLine("Install the RTL-SDR native library or pass --driver <path-to-librtlsdr>.");
            return 1;
        }
        catch (RtlSdrException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static bool ShouldPrint(ModeSMessage message, AppOptions options)
    {
        if (!options.IncludeInvalidFrames && !message.CrcOk)
        {
            return false;
        }

        if (!options.IncludeNonAdsbFrames && !message.IsExtendedSquitter)
        {
            return false;
        }

        return true;
    }

    private static void ListDevices()
    {
        var devices = RtlSdrDevice.ListDevices();
        if (devices.Count == 0)
        {
            Console.WriteLine("No RTL-SDR devices were found.");
            return;
        }

        foreach (var device in devices)
        {
            Console.WriteLine($"{device.Index}: {device.Manufacturer} {device.Product} serial={device.Serial}");
        }
    }
}
