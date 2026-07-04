namespace CheapFurniturePlanner.Domain.Pricing;

public record BreakdownLine(BreakdownStage Stage, string Category, string Description, string? SourceLineKey, decimal Quantity, string Unit, decimal UnitCost, decimal LineTotal);
