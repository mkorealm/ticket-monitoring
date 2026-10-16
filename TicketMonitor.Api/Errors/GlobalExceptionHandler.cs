using Microsoft.AspNetCore.Diagnostics;
using TicketMonitor.Core.Models.Dto;

namespace TicketMonitor.Api.Errors;

/// <summary>Глобальный обработчик исключений через <see cref="IExceptionHandler"/>.</summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        if (httpContext.Response.HasStarted)
        {
            logger.LogWarning(exception, "Не удалось обработать исключение ({ExceptionType}): ответ уже начал отправляться",
                exception.GetType().Name);
            return false;
        }

        var (statusCode, message) = Classify(exception);
        LogException(exception, httpContext, statusCode);

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json";

        await httpContext.Response.WriteAsJsonAsync(ApiResponseDto.Fail(message), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static (int StatusCode, string Message) Classify(Exception exception) => exception switch
    {
        OperationCanceledException when exception is not TaskCanceledException
            => (HttpStatusCodes.ClientClosedRequest, "Запрос отменён клиентом"),

        TaskCanceledException
            => (StatusCodes.Status504GatewayTimeout, "Превышено время ожидания"),

        ArgumentException or ArgumentNullException
            => (StatusCodes.Status400BadRequest, exception.Message),

        InvalidOperationException
            => (StatusCodes.Status500InternalServerError, "Внутренняя ошибка сервера"),

        _ => (StatusCodes.Status500InternalServerError, "Внутренняя ошибка сервера")
    };

    private void LogException(Exception exception, HttpContext httpContext, int statusCode)
    {
        var method = httpContext.Request.Method;
        var path = httpContext.Request.Path.Value ?? "(unknown)";

        switch (statusCode)
        {
            case HttpStatusCodes.ClientClosedRequest:
                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("Запрос {Method} {Path} отменён клиентом", method, path);
                break;

            case StatusCodes.Status400BadRequest:
                logger.LogWarning(exception, "Ошибка валидации при {Method} {Path}: {Message}", method, path, exception.Message);
                break;

            case StatusCodes.Status504GatewayTimeout:
                logger.LogWarning(exception, "Превышено время ожидания при {Method} {Path}", method, path);
                break;

            case StatusCodes.Status500InternalServerError:
                logger.LogError(exception, "Необработанное исключение при {Method} {Path}: {Message}", method, path, exception.Message);
                break;

            default:
                logger.LogError(exception, "Ошибка {StatusCode} при {Method} {Path}: {Message}",
                    statusCode, method, path, exception.Message);
                break;
        }
    }
}