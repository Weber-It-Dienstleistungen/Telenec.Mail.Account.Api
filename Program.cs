using System.Net;
using System.Net.Mail;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Telenec.Mail.Account.Api.Contracts;
using Telenec.Mail.Account.Api.Options;
using Telenec.Mail.Account.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<PoppassdOptions>()
    .Bind(builder.Configuration.GetSection(PoppassdOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Host),
        "Poppassd host must be configured.")
    .Validate(
        options => options.Port is > 0 and <= 65535,
        "Poppassd port must be between 1 and 65535.")
    .Validate(
        options => options.TimeoutSeconds is >= 1 and <= 30,
        "Poppassd timeout must be between 1 and 30 seconds.")
    .ValidateOnStart();

builder.Services
    .AddOptions<PasswordChangeRateLimitOptions>()
    .Bind(builder.Configuration.GetSection(
        PasswordChangeRateLimitOptions.SectionName))
    .Validate(
        options => options.PermitLimit is >= 1 and <= 100,
        "Password change rate limit must be between 1 and 100 requests.")
    .Validate(
        options => options.WindowMinutes is >= 1 and <= 60,
        "Password change rate limit window must be between 1 and 60 minutes.")
    .ValidateOnStart();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    options.ForwardLimit = 1;

    options.KnownProxies.Clear();

    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

builder.Services.AddSingleton<
    IPasswordChangeService,
    PoppassdPasswordChangeService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode =
        StatusCodes.Status429TooManyRequests;

    options.AddPolicy(
        "password-change",
        httpContext =>
        {
            var rateLimitOptions = httpContext
                .RequestServices
                .GetRequiredService<
                    IOptions<PasswordChangeRateLimitOptions>>()
                .Value;

            var partitionKey =
                httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown";

            return RateLimitPartition.GetFixedWindowLimiter(
                partitionKey,
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = rateLimitOptions.PermitLimit,
                    Window = TimeSpan.FromMinutes(
                        rateLimitOptions.WindowMinutes),
                    QueueLimit = 0,
                    QueueProcessingOrder =
                        QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                });
        });
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new
{
    service = "Telenec Mail Account API",
    status = "ok"
}));

app.MapPost(
        "/api/v1/account/password",
        async (
            ChangePasswordRequest request,
            IPasswordChangeService passwordChangeService,
            CancellationToken cancellationToken) =>
        {
            var validationError = ValidateRequest(request);

            if (validationError is not null)
            {
                return Results.Json(
                    new
                    {
                        code = "invalid_request",
                        message = validationError
                    },
                    statusCode:
                        StatusCodes.Status400BadRequest);
            }

            var result =
                await passwordChangeService.ChangePasswordAsync(
                    request.UserName!,
                    request.CurrentPassword!,
                    request.NewPassword!,
                    cancellationToken);

            return result.Status switch
            {
                PasswordChangeStatus.Success =>
                    Results.NoContent(),

                PasswordChangeStatus.AuthenticationFailed =>
                    Results.Json(
                        new
                        {
                            code = "invalid_credentials",
                            message =
                                "Benutzername oder aktuelles Passwort ist ungültig."
                        },
                        statusCode:
                            StatusCodes.Status401Unauthorized),

                PasswordChangeStatus.Rejected =>
                    Results.Json(
                        new
                        {
                            code = "password_rejected",
                            message =
                                "Das neue Passwort wurde vom Mailserver abgelehnt."
                        },
                        statusCode:
                            StatusCodes.Status400BadRequest),

                PasswordChangeStatus.ServiceUnavailable =>
                    Results.Json(
                        new
                        {
                            code = "service_unavailable",
                            message =
                                "Der Passwortdienst ist momentan nicht verfügbar."
                        },
                        statusCode:
                            StatusCodes.Status503ServiceUnavailable),

                _ =>
                    Results.Json(
                        new
                        {
                            code = "server_error",
                            message =
                                "Der Passwortwechsel konnte nicht abgeschlossen werden."
                        },
                        statusCode:
                            StatusCodes.Status502BadGateway)
            };
        })
    .RequireRateLimiting("password-change");

app.Run();

static string? ValidateRequest(
    ChangePasswordRequest request)
{
    if (string.IsNullOrWhiteSpace(request.UserName) ||
        string.IsNullOrEmpty(request.CurrentPassword) ||
        string.IsNullOrEmpty(request.NewPassword))
    {
        return "Benutzername, aktuelles Passwort und neues Passwort sind erforderlich.";
    }

    if (request.UserName.Length > 254 ||
        request.CurrentPassword.Length > 1024 ||
        request.NewPassword.Length > 1024)
    {
        return "Die übermittelten Daten sind ungültig.";
    }

    if (ContainsProtocolControlCharacters(
            request.UserName) ||
        ContainsProtocolControlCharacters(
            request.CurrentPassword) ||
        ContainsProtocolControlCharacters(
            request.NewPassword))
    {
        return "Die übermittelten Daten sind ungültig.";
    }

    if (!MailAddress.TryCreate(
            request.UserName,
            out var mailAddress) ||
        !string.Equals(
            mailAddress.Address,
            request.UserName,
            StringComparison.OrdinalIgnoreCase))
    {
        return "Der Benutzername muss eine gültige E-Mail-Adresse sein.";
    }

    if (string.Equals(
            request.CurrentPassword,
            request.NewPassword,
            StringComparison.Ordinal))
    {
        return "Das neue Passwort muss sich vom aktuellen Passwort unterscheiden.";
    }

    return null;
}

static bool ContainsProtocolControlCharacters(
    string value)
{
    return value.Contains('\r') ||
           value.Contains('\n') ||
           value.Contains('\0');
}