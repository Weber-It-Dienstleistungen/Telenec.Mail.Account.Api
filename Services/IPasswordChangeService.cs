namespace Telenec.Mail.Account.Api.Services;

public interface IPasswordChangeService
{
    Task<PasswordChangeResult> ChangePasswordAsync(
        string userName,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);
}