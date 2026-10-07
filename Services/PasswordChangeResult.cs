namespace Telenec.Mail.Account.Api.Services;

public enum PasswordChangeStatus
{
    Success,
    AuthenticationFailed,
    Rejected,
    ServiceUnavailable,
    ProtocolError
}

public sealed record PasswordChangeResult(PasswordChangeStatus Status)
{
    public bool IsSuccess => Status == PasswordChangeStatus.Success;

    public static PasswordChangeResult Success()
        => new(PasswordChangeStatus.Success);

    public static PasswordChangeResult AuthenticationFailed()
        => new(PasswordChangeStatus.AuthenticationFailed);

    public static PasswordChangeResult Rejected()
        => new(PasswordChangeStatus.Rejected);

    public static PasswordChangeResult ServiceUnavailable()
        => new(PasswordChangeStatus.ServiceUnavailable);

    public static PasswordChangeResult ProtocolError()
        => new(PasswordChangeStatus.ProtocolError);
}