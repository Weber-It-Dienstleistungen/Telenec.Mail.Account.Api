namespace Telenec.Mail.Account.Api.Options;

public sealed class PasswordChangeRateLimitOptions
{
    public const string SectionName = "PasswordChangeRateLimit";

    public int PermitLimit { get; set; } = 5;

    public int WindowMinutes { get; set; } = 10;
}