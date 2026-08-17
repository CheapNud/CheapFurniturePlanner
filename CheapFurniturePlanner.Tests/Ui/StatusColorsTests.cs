using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using CheapFurniturePlanner.Ui;
using MudBlazor;
using Xunit;

namespace CheapFurniturePlanner.Tests.Ui;

// UX-1 Task 1: table-driven so every enum member is pinned to a color - a missing arm falling
// through to the wrong default is exactly the bug StatusColors exists to prevent.
public class StatusColorsTests
{
    [Theory]
    [InlineData(ProductionUnitState.Expected, Color.Secondary)]
    [InlineData(ProductionUnitState.Arrived, Color.Primary)]
    [InlineData(ProductionUnitState.Delivered, Color.Success)]
    [InlineData(ProductionUnitState.Cancelled, Color.Error)]
    public void For_ProductionUnitState_MapsToExpectedColor(ProductionUnitState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(TripState.Planning, Color.Secondary)]
    [InlineData(TripState.Departed, Color.Primary)]
    [InlineData(TripState.Completed, Color.Success)]
    public void For_TripState_MapsToExpectedColor(TripState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(SupplierOrderState.Draft, Color.Secondary)]
    [InlineData(SupplierOrderState.Sent, Color.Primary)]
    [InlineData(SupplierOrderState.Completed, Color.Success)]
    public void For_SupplierOrderState_MapsToExpectedColor(SupplierOrderState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(MaterialOrderState.Draft, Color.Secondary)]
    [InlineData(MaterialOrderState.Sent, Color.Primary)]
    [InlineData(MaterialOrderState.Completed, Color.Success)]
    public void For_MaterialOrderState_MapsToExpectedColor(MaterialOrderState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(OrderState.Draft, Color.Secondary)]
    [InlineData(OrderState.Placed, Color.Primary)]
    [InlineData(OrderState.Cancelled, Color.Error)]
    public void For_OrderState_MapsToExpectedColor(OrderState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(ServiceTicketState.New, Color.Secondary)]
    [InlineData(ServiceTicketState.InProgress, Color.Primary)]
    [InlineData(ServiceTicketState.Resolved, Color.Success)]
    [InlineData(ServiceTicketState.Cancelled, Color.Error)]
    public void For_ServiceTicketState_MapsToExpectedColor(ServiceTicketState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(TradeItemState.Draft, Color.Secondary)]
    [InlineData(TradeItemState.Active, Color.Primary)]
    [InlineData(TradeItemState.Discontinued, Color.Error)]
    [InlineData(TradeItemState.PhasingOut, Color.Warning)]
    public void For_TradeItemState_MapsToExpectedColor(TradeItemState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(ProductionPhase.InProduction, Color.Primary)]
    [InlineData(ProductionPhase.Ready, Color.Secondary)]
    [InlineData(ProductionPhase.Delivered, Color.Success)]
    public void For_ProductionPhase_MapsToExpectedColor(ProductionPhase phase, Color expected) =>
        Assert.Equal(expected, StatusColors.For(phase));

    [Theory]
    [InlineData(PriceVersionStatus.Effective, Color.Primary)]
    [InlineData(PriceVersionStatus.Scheduled, Color.Secondary)]
    [InlineData(PriceVersionStatus.Superseded, Color.Default)]
    public void For_PriceVersionStatus_MapsToExpectedColor(PriceVersionStatus status, Color expected) =>
        Assert.Equal(expected, StatusColors.For(status));

    [Theory]
    [InlineData(ProductionCodeStatus.Composed, Color.Secondary)]
    [InlineData(ProductionCodeStatus.Provisional, Color.Primary)]
    [InlineData(ProductionCodeStatus.Released, Color.Success)]
    public void For_ProductionCodeStatus_MapsToExpectedColor(ProductionCodeStatus status, Color expected) =>
        Assert.Equal(expected, StatusColors.For(status));

    [Theory]
    [InlineData(true, Color.Success)]
    [InlineData(false, Color.Primary)]
    public void ForPaid_MapsToExpectedColor(bool isPaid, Color expected) =>
        Assert.Equal(expected, StatusColors.ForPaid(isPaid));

    [Theory]
    [InlineData(true, Color.Success)]
    [InlineData(false, Color.Primary)]
    public void ForSettled_MapsToExpectedColor(bool isSettled, Color expected) =>
        Assert.Equal(expected, StatusColors.ForSettled(isSettled));

    [Theory]
    [InlineData(true, Color.Success)]
    [InlineData(false, Color.Default)]
    public void ForActive_MapsToExpectedColor(bool isActive, Color expected) =>
        Assert.Equal(expected, StatusColors.ForActive(isActive));

    [Theory]
    [InlineData(true, Color.Secondary)]
    [InlineData(false, Color.Default)]
    public void ForCredited_MapsToExpectedColor(bool isCredited, Color expected) =>
        Assert.Equal(expected, StatusColors.ForCredited(isCredited));

    [Theory]
    [InlineData(true, Color.Info)]
    [InlineData(false, Color.Default)]
    public void ForExported_MapsToExpectedColor(bool isExported, Color expected) =>
        Assert.Equal(expected, StatusColors.ForExported(isExported));
}
