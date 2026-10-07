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

builder.Services.AddSingleton<
    IPasswordChangeService,
    PoppassdPasswordChangeService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "Telenec Mail Account API",
    status = "ok"
}));

app.Run();