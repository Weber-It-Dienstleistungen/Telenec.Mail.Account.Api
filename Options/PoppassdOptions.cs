namespace Telenec.Mail.Account.Api.Options;

public sealed class PoppassdOptions
{
    public const string SectionName = "Poppassd";

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 106;

    public int TimeoutSeconds { get; set; } = 5;
}