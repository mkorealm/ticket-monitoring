namespace TicketMonitor.Core.Models.Dto;

/// <summary>Универсальный ответ API.</summary>
public sealed record ApiResponseDto
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    public static ApiResponseDto Ok(string message = "") => new() { Success = true, Message = message };
    public static ApiResponseDto Fail(string message) => new() { Success = false, Message = message };
}