using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using MudBlazor;

namespace CheapFurniturePlanner.Ui;

// The one place chip colors live. Progression semantics, context-free: pending states are
// grey, in-motion states blue, terminal-good green, terminal-bad red. Two screens once showed
// the SAME unit state in opposite colors because every page kept its own switch - never again.
public static class StatusColors
{
    public static Color For(ProductionUnitState unitState) => unitState switch
    {
        ProductionUnitState.Expected => Color.Default,
        ProductionUnitState.Arrived => Color.Info,
        ProductionUnitState.Delivered => Color.Success,
        _ => Color.Error,
    };

    public static Color For(TripState tripState) => tripState switch
    {
        TripState.Planning => Color.Default,
        TripState.Departed => Color.Info,
        _ => Color.Success,
    };

    public static Color For(SupplierOrderState orderState) => orderState switch
    {
        SupplierOrderState.Draft => Color.Default,
        SupplierOrderState.Sent => Color.Info,
        _ => Color.Success,
    };

    public static Color For(MaterialOrderState materialOrderState) => materialOrderState switch
    {
        MaterialOrderState.Draft => Color.Default,
        MaterialOrderState.Sent => Color.Info,
        _ => Color.Success,
    };

    public static Color For(OrderState orderState) => orderState switch
    {
        OrderState.Draft => Color.Default,
        OrderState.Placed => Color.Info,
        _ => Color.Error,
    };

    public static Color For(ServiceTicketState ticketState) => ticketState switch
    {
        ServiceTicketState.New => Color.Default,
        ServiceTicketState.InProgress => Color.Info,
        ServiceTicketState.Resolved => Color.Success,
        _ => Color.Error,
    };

    // TradeItemState has four members, not the illustrative three - kept as the StudioPage
    // mapping it replaces: Discontinued is a distinct terminal-bad from PhasingOut's warning.
    public static Color For(TradeItemState tradeItemState) => tradeItemState switch
    {
        TradeItemState.Draft => Color.Default,
        TradeItemState.Active => Color.Success,
        TradeItemState.Discontinued => Color.Error,
        TradeItemState.PhasingOut => Color.Warning,
        _ => Color.Default,
    };

    // Shipped attention-ladder mapping kept by decision; centralized here regardless.
    public static Color For(ProductionPhase phase) => phase switch
    {
        ProductionPhase.InProduction => Color.Warning,
        ProductionPhase.Ready => Color.Info,
        _ => Color.Success,
    };

    public static Color For(PriceVersionStatus versionStatus) => versionStatus switch
    {
        PriceVersionStatus.Effective => Color.Success,
        PriceVersionStatus.Scheduled => Color.Warning,
        _ => Color.Default,
    };

    // Preserves StudioNamingPage's existing mapping exactly: Composed falls through the same
    // catch-all as any future member, matching the local switch it replaces.
    public static Color For(ProductionCodeStatus codeStatus) => codeStatus switch
    {
        ProductionCodeStatus.Provisional => Color.Warning,
        ProductionCodeStatus.Released => Color.Success,
        _ => Color.Default,
    };

    public static Color ForPaid(bool isPaid) => isPaid ? Color.Success : Color.Info;
    public static Color ForSettled(bool isSettled) => isSettled ? Color.Success : Color.Info;
    public static Color ForActive(bool isActive) => isActive ? Color.Success : Color.Default;
}
