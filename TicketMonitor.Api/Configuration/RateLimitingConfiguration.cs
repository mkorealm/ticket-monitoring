using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using TicketMonitor.Core.Models.Dto;

namespace TicketMonitor.Api.Configuration;

/// <summary>Конфигурация rate limiting для API.</summary>
public static class RateLimitingConfiguration
{
    public const string HealthPolicyName = "health";
    public const string DefaultPolicyName = "default";

    private const int PermitLimit = 100;
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    /// <summary>Регистрирует rate limiter с разделением по IP-адресу клиента.</summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(DefaultPolicyName, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ResolvePartitionKey(context),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = PermitLimit,
                        Window = Window,
                        QueueLimit = 0,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        AutoReplenishment = true
                    }));

            options.AddPolicy(HealthPolicyName, _ => RateLimitPartition.GetNoLimiter("health"));
            options.OnRejected = OnRejectedAsync;
        });

        return services;
    }

    private static string ResolvePartitionKey(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        return ip is null ? "unknown" : ip.ToString();
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var logger = context.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("TicketMonitor.Api.RateLimiting");

        var ip = ResolvePartitionKey(context.HttpContext);
        var path = context.HttpContext.Request.Path.Value ?? string.Empty;

        var retryAfterSeconds = 30;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            retryAfterSeconds = (int)Math.Ceiling(retryAfter.TotalSeconds);

        logger.LogWarning("Rate limit превышен: IP={Ip}, Path={Path}, RetryAfter={RetryAfter}s", ip, path, retryAfterSeconds);

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json";
        context.HttpContext.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        var response = ApiResponseDto.Fail($"Слишком много запросов. Повторите через {retryAfterSeconds} сек.");
        await context.HttpContext.Response.WriteAsJsonAsync(response, cancellationToken).ConfigureAwait(false);
    }
}