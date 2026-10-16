namespace TicketMonitor.Core.Models;

/// <summary>Идентификаторы полей GLPI (search options для Ticket).</summary>
public static class GlpiFields
{
    public const int Title = 1;
    public const int Id = 2;
    public const int Priority = 3;
    public const int Requester = 4;
    public const int Assignee = 5;
    public const int Category = 7;
    public const int Status = 12;
    public const int OpenDate = 15;
    public const int LastModified = 19;
}