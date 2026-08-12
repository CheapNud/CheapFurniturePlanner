using CheapFurniturePlanner.Domain.Catalog;
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
    [InlineData(ProductionUnitState.Expected, Color.Default)]
    [InlineData(ProductionUnitState.Arrived, Color.Info)]
    [InlineData(ProductionUnitState.Delivered, Color.Success)]
    [InlineData(ProductionUnitState.Cancelled, Color.Error)]
    public void For_ProductionUnitState_MapsToExpectedColor(ProductionUnitState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(TripState.Planning, Color.Default)]
    [InlineData(TripState.Departed, Color.Info)]
    [InlineData(TripState.Completed, Color.Success)]
    public void For_TripState_MapsToExpectedColor(TripState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(SupplierOrderState.Draft, Color.Default)]
    [InlineData(SupplierOrderState.Sent, Color.Info)]
    [InlineData(SupplierOrderState.Completed, Color.Success)]
    public void For_SupplierOrderState_MapsToExpectedColor(SupplierOrderState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(MaterialOrderState.Draft, Color.Default)]
    [InlineData(MaterialOrderState.Sent, Color.Info)]
    [InlineData(MaterialOrderState.Completed, Color.Success)]
    public void For_MaterialOrderState_MapsToExpectedColor(MaterialOrderState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(OrderState.Draft, Color.Default)]
    [InlineData(OrderState.Placed, Color.Info)]
    [InlineData(OrderState.Cancelled, Color.Error)]
    public void For_OrderState_MapsToExpectedColor(OrderState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(ServiceTicketState.New, Color.Default)]
    [InlineData(ServiceTicketState.InProgress, Color.Info)]
    [InlineData(ServiceTicketState.Resolved, Color.Success)]
    [InlineData(ServiceTicketState.Cancelled, Color.Error)]
    public void For_ServiceTicketState_MapsToExpectedColor(ServiceTicketState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(TradeItemState.Draft, Color.Default)]
    [InlineData(TradeItemState.Active, Color.Success)]
    [InlineData(TradeItemState.Discontinued, Color.Error)]
    [InlineData(TradeItemState.PhasingOut, Color.Warning)]
    public void For_TradeItemState_MapsToExpectedColor(TradeItemState state, Color expected) =>
        Assert.Equal(expected, StatusColors.For(state));

    [Theory]
    [InlineData(ProductionPhase.InProduction, Color.Warning)]
    [InlineData(ProductionPhase.Ready, Color.Info)]
    [InlineData(ProductionPhase.Delivered, Color.Success)]
    public void For_ProductionPhase_MapsToExpectedColor(ProductionPhase phase, Color expected) =>
        Assert.Equal(expected, StatusColors.For(phase));

    [Theory]
    [InlineData(PriceVersionStatus.Effective, Color.Success)]
    [InlineData(PriceVersionStatus.Scheduled, Color.Warning)]
    [InlineData(PriceVersionStatus.Superseded, Color.Default)]
    public void For_PriceVersionStatus_MapsToExpectedColor(PriceVersionStatus status, Color expected) =>
        Assert.Equal(expected, StatusColors.For(status));

    [Theory]
    [InlineData(true, Color.Success)]
    [InlineData(false, Color.Info)]
    public void ForPaid_MapsToExpectedColor(bool isPaid, Color expected) =>
        Assert.Equal(expected, StatusColors.ForPaid(isPaid));

    [Theory]
    [InlineData(true, Color.Success)]
    [InlineData(false, Color.Info)]
    public void ForSettled_MapsToExpectedColor(bool isSettled, Color expected) =>
        Assert.Equal(expected, StatusColors.ForSettled(isSettled));

    [Theory]
    [InlineData(true, Color.Success)]
    [InlineData(false, Color.Default)]
    public void ForActive_MapsToExpectedColor(bool isActive, Color expected) =>
        Assert.Equal(expected, StatusColors.ForActive(isActive));
}
