using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Production;
using CheapFurniturePlanner.Models;
using CheapFurniturePlanner.Services;
using MudBlazor;

namespace CheapFurniturePlanner.Ui;

// The one place chip colors live. UX-2 Task 3 semantic scheme, context-free across every enum:
// Secondary at-rest (not yet started/moving) - Primary in-progress (currently active/moving) -
// Success completed (the good terminal state) - Error terminal-bad (cancelled/failed-terminal) -
// Warning remediation (needs attention before it can proceed) - Info a secondary, non-blocking
// fact about an otherwise-normal record (e.g. "already exported") - Default unknown (an enum
// value with no defined mapping, or a deliberately muted historical record).
// Two screens once showed the SAME unit state in opposite colors because every page kept its own
// switch - never again.
public static class StatusColors
{
    // Arrived is mid-flow toward delivery, not terminal, so it gets the active color (documented
    // decision). A failed-delivery confirmation (ConfirmFailedAsync) has no separate enum member -
    // it strips the unit's trip assignment and leaves State at Arrived so the unit re-enters the
    // pool for re-planning, so that remediation path is already covered by Arrived's Primary.
    public static Color For(ProductionUnitState unitState) => unitState switch
    {
        ProductionUnitState.Expected => Color.Secondary,
        ProductionUnitState.Arrived => Color.Primary,
        ProductionUnitState.Delivered => Color.Success,
        ProductionUnitState.Cancelled => Color.Error,
        _ => Color.Default,
    };

    public static Color For(TripState tripState) => tripState switch
    {
        TripState.Planning => Color.Secondary,
        TripState.Departed => Color.Primary,
        _ => Color.Success,
    };

    public static Color For(SupplierOrderState orderState) => orderState switch
    {
        SupplierOrderState.Draft => Color.Secondary,
        SupplierOrderState.Sent => Color.Primary,
        _ => Color.Success,
    };

    public static Color For(MaterialOrderState materialOrderState) => materialOrderState switch
    {
        MaterialOrderState.Draft => Color.Secondary,
        MaterialOrderState.Sent => Color.Primary,
        _ => Color.Success,
    };

    public static Color For(OrderState orderState) => orderState switch
    {
        OrderState.Draft => Color.Secondary,
        OrderState.Placed => Color.Primary,
        _ => Color.Error,
    };

    // New = at-rest, InProgress = active, Resolved = done, Cancelled = terminal-failure.
    public static Color For(ServiceTicketState ticketState) => ticketState switch
    {
        ServiceTicketState.New => Color.Secondary,
        ServiceTicketState.InProgress => Color.Primary,
        ServiceTicketState.Resolved => Color.Success,
        _ => Color.Error,
    };

    // TradeItemState has four members, not the illustrative three: Draft is at-rest (unpublished),
    // Active is the live/in-play state (active, not "done" - a catalogue item is never finished),
    // PhasingOut is remediation (needs attention before it drops out of the catalogue), and
    // Discontinued is the distinct terminal-bad from PhasingOut's warning.
    public static Color For(TradeItemState tradeItemState) => tradeItemState switch
    {
        TradeItemState.Draft => Color.Secondary,
        TradeItemState.Active => Color.Primary,
        TradeItemState.Discontinued => Color.Error,
        TradeItemState.PhasingOut => Color.Warning,
        _ => Color.Default,
    };

    // Derived from ProductionUnitService.DerivePhase: InProduction means units are still Expected
    // (actively being sourced - active), Ready means all units Arrived but not yet all Delivered
    // (staged at the dock, waiting on a trip - at-rest), Delivered means done.
    public static Color For(ProductionPhase phase) => phase switch
    {
        ProductionPhase.InProduction => Color.Primary,
        ProductionPhase.Ready => Color.Secondary,
        _ => Color.Success,
    };

    // Scheduled = at-rest (not yet in force), Effective = the actively-current version (active),
    // Superseded = muted historical record, not an error - stays Default (documented decision).
    public static Color For(PriceVersionStatus versionStatus) => versionStatus switch
    {
        PriceVersionStatus.Scheduled => Color.Secondary,
        PriceVersionStatus.Effective => Color.Primary,
        _ => Color.Default,
    };

    // Composed = no naming decision made yet, using the calculated fallback code (at-rest);
    // Provisional = a suggestion exists but the model is still Draft (active, not yet final);
    // Released = the suggestion is finalized against an Active/Discontinued model (done).
    public static Color For(ProductionCodeStatus codeStatus) => codeStatus switch
    {
        ProductionCodeStatus.Composed => Color.Secondary,
        ProductionCodeStatus.Provisional => Color.Primary,
        _ => Color.Success,
    };

    public static Color ForPaid(bool isPaid) => isPaid ? Color.Success : Color.Primary;
    public static Color ForSettled(bool isSettled) => isSettled ? Color.Success : Color.Primary;
    // Affirmative designation markers (e.g. Default/Preferred chips) stay Success when true -
    // they are not a completion state but flagging one still reads as the "good" color; false is
    // simply unmarked (Default), not an error (documented decision).
    public static Color ForActive(bool isActive) => isActive ? Color.Success : Color.Default;
    // A credit note doesn't undo the invoice or flag a problem with it - Secondary (a neutral,
    // at-rest marker) rather than Warning/Error.
    public static Color ForCredited(bool isCredited) => isCredited ? Color.Secondary : Color.Default;
    // Already-exported is informational, not a completion state of the invoice itself - Info.
    public static Color ForExported(bool isExported) => isExported ? Color.Info : Color.Default;
}
