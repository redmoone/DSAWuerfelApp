namespace DsaWuerfelApp.Services;

public enum RequestRejectionReason
{
    Validation,
    Forbidden,
    NotFound
}

public sealed class RequestRejectedException : Exception
{
    public RequestRejectedException(RequestRejectionReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    public RequestRejectionReason Reason { get; }
}
