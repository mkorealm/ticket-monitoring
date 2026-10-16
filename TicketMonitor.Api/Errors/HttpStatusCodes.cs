namespace TicketMonitor.Api.Errors;

/// <summary>HTTP-статусы, отсутствующие в <see cref="Microsoft.AspNetCore.Http.StatusCodes"/>.</summary>
public static class HttpStatusCodes
{
    /// <summary>499 Client Closed Request (nginx).</summary>
    public const int ClientClosedRequest = 499;
}