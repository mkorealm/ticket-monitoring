using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMonitor.Watchdog.Services;
using Xunit;

namespace TicketMonitor.Watchdog.Tests;

/// <summary>Юнит-тесты <see cref="OperationTypeDisplayNameProvider"/>.</summary>
public sealed class OperationTypeDisplayNameProviderTests
{
    [Fact]
    public void GetDisplayName_KnownType_ReturnsHumanName()
    {
        var sut = new OperationTypeDisplayNameProvider(NullLogger<OperationTypeDisplayNameProvider>.Instance);

        var result = sut.GetDisplayName("GLPI:FetchTicket");

        result.Should().Be("Получение заявки из GLPI");
    }

    [Fact]
    public void GetDisplayName_UnknownType_ReturnsTypeAsIs()
    {
        var sut = new OperationTypeDisplayNameProvider(NullLogger<OperationTypeDisplayNameProvider>.Instance);

        var result = sut.GetDisplayName("Unknown:NewOperation");

        result.Should().Be("Unknown:NewOperation");
    }

    [Fact]
    public void GetDisplayName_EmptyInput_ReturnsEmpty()
    {
        var sut = new OperationTypeDisplayNameProvider(NullLogger<OperationTypeDisplayNameProvider>.Instance);

        sut.GetDisplayName("").Should().BeEmpty();
        sut.GetDisplayName("   ").Should().BeEmpty();
    }

    [Fact]
    public void GetDisplayName_NullInput_ReturnsEmpty()
    {
        var sut = new OperationTypeDisplayNameProvider(NullLogger<OperationTypeDisplayNameProvider>.Instance);

        sut.GetDisplayName(null!).Should().BeEmpty();
    }

    [Fact]
    public void GetDisplayName_CaseSensitive_ReturnsTypeForDifferentCase()
    {
        var sut = new OperationTypeDisplayNameProvider(NullLogger<OperationTypeDisplayNameProvider>.Instance);

        // Маппинг — Ordinal, поэтому регистр имеет значение.
        var result = sut.GetDisplayName("glpi:fetchticket");

        result.Should().Be("glpi:fetchticket");
    }
}