namespace Adsb.Radio;

/// <summary>
/// Exception raised when librtlsdr returns a negative error code for a requested operation.
/// </summary>
public sealed class RtlSdrException : Exception
{
    /// <summary>
    /// Creates an exception that preserves the native librtlsdr error code for diagnostics.
    /// </summary>
    public RtlSdrException(string message, int errorCode)
        : base($"{message} (rtlsdr error {errorCode})")
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Native librtlsdr return code associated with the failed operation.
    /// </summary>
    public int ErrorCode { get; }
}
