namespace CheapFurniturePlanner.Domain.Pricing;

public enum MarkupMode { Compound, Additive }

public record MarkupStep(string Name, decimal Percent, MarkupMode Mode);
