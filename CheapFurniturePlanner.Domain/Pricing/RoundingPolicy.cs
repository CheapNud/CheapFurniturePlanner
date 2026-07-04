namespace CheapFurniturePlanner.Domain.Pricing;

[Flags] public enum RoundStage { None = 0, Line = 1, Subtotal = 2, Final = 4 }

public record RoundingPolicy(int LineDecimals, int FinalDecimals, MidpointRounding Midpoint, RoundStage Stages)
{
    public decimal RoundLine(decimal amount) => Stages.HasFlag(RoundStage.Line) ? Math.Round(amount, LineDecimals, Midpoint) : amount;
    public decimal RoundSubtotal(decimal amount) => Stages.HasFlag(RoundStage.Subtotal) ? Math.Round(amount, LineDecimals, Midpoint) : amount;
    public decimal RoundFinal(decimal amount) => Stages.HasFlag(RoundStage.Final) ? Math.Round(amount, FinalDecimals, Midpoint) : amount;
}
