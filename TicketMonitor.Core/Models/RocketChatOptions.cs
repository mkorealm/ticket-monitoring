namespace TicketMonitor.Core.Models;

/// <summary>Настройки интеграции с Rocket.Chat.</summary>
public sealed class RocketChatOptions
{
    public const string SectionName = "RocketChat";

    public string ServerUrl { get; set; } = string.Empty;
    public string? Token { get; set; }
    public string? UserId { get; set; }
    public string? DefaultRoomId { get; set; }

    public Dictionary<string, string> RoomIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int TimeoutSeconds { get; set; } = 30;
    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);
}