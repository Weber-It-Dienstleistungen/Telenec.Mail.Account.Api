namespace Telenec.Mail.Account.Api.Contracts;

public sealed record ChangePasswordRequest(
    string? UserName,
    string? CurrentPassword,
    string? NewPassword);