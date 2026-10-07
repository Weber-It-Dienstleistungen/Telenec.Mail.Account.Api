using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using Telenec.Mail.Account.Api.Options;

namespace Telenec.Mail.Account.Api.Services;

public sealed class PoppassdPasswordChangeService : IPasswordChangeService
{
    private readonly PoppassdOptions _options;
    private readonly ILogger<PoppassdPasswordChangeService> _logger;

    public PoppassdPasswordChangeService(
        IOptions<PoppassdOptions> options,
        ILogger<PoppassdPasswordChangeService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PasswordChangeResult> ChangePasswordAsync(
        string userName,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        if (!IsSafeProtocolValue(userName) ||
            !IsSafeProtocolValue(currentPassword) ||
            !IsSafeProtocolValue(newPassword))
        {
            return PasswordChangeResult.Rejected();
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);

        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        try
        {
            using var client = new TcpClient();

            await client.ConnectAsync(
                _options.Host,
                _options.Port,
                timeoutCts.Token);

            await using var stream = client.GetStream();

            using var reader = new StreamReader(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: false),
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);

            await using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: false),
                leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\r\n"
            };

            var greeting = await ReadResponseAsync(
                reader,
                timeoutCts.Token);

            if (!IsSuccessfulResponse(greeting))
            {
                return PasswordChangeResult.ProtocolError();
            }

            await writer.WriteLineAsync(
                $"user {userName}".AsMemory(),
                timeoutCts.Token);

            var userResponse = await ReadResponseAsync(
                reader,
                timeoutCts.Token);

            if (!IsSuccessfulResponse(userResponse))
            {
                return PasswordChangeResult.AuthenticationFailed();
            }

            await writer.WriteLineAsync(
                $"pass {currentPassword}".AsMemory(),
                timeoutCts.Token);

            var passwordResponse = await ReadResponseAsync(
                reader,
                timeoutCts.Token);

            if (!IsSuccessfulResponse(passwordResponse))
            {
                return PasswordChangeResult.AuthenticationFailed();
            }

            await writer.WriteLineAsync(
                $"newpass {newPassword}".AsMemory(),
                timeoutCts.Token);

            var newPasswordResponse = await ReadResponseAsync(
                reader,
                timeoutCts.Token);

            if (!IsSuccessfulResponse(newPasswordResponse))
            {
                return PasswordChangeResult.Rejected();
            }

            return PasswordChangeResult.Success();
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Connection to the local poppassd service timed out.");

            return PasswordChangeResult.ServiceUnavailable();
        }
        catch (SocketException exception)
        {
            _logger.LogWarning(
                exception,
                "Could not connect to the local poppassd service.");

            return PasswordChangeResult.ServiceUnavailable();
        }
        catch (IOException exception)
        {
            _logger.LogWarning(
                exception,
                "Communication with the local poppassd service failed.");

            return PasswordChangeResult.ServiceUnavailable();
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unexpected error while communicating with poppassd.");

            return PasswordChangeResult.ProtocolError();
        }
    }

    private static async Task<string?> ReadResponseAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        return await reader.ReadLineAsync(cancellationToken);
    }

    private static bool IsSuccessfulResponse(string? response)
    {
        return response is not null &&
               response.Length >= 3 &&
               response[0] == '2' &&
               char.IsDigit(response[1]) &&
               char.IsDigit(response[2]);
    }

    private static bool IsSafeProtocolValue(string value)
    {
        return !string.IsNullOrEmpty(value) &&
               !value.Contains('\r') &&
               !value.Contains('\n') &&
               !value.Contains('\0');
    }
}