namespace Adsb.Radio;

public sealed class RtlSdrException : Exception
{
    public RtlSdrException(string message, int errorCode)
        : base($"{message} (rtlsdr error {errorCode})")
    {
        ErrorCode = errorCode;
    }

    public int ErrorCode { get; }
}
