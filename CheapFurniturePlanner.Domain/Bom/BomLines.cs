using System.Text.Json.Serialization;

namespace CheapFurniturePlanner.Domain.Bom;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(FrameBomLine), "frame")]
[JsonDerivedType(typeof(FoamBomLine), "foam")]
[JsonDerivedType(typeof(CottonBomLine), "cotton")]
[JsonDerivedType(typeof(CutSortBomLine), "cutsort")]
[JsonDerivedType(typeof(MiscBomLine), "misc")]
[JsonDerivedType(typeof(LaborBomLine), "labor")]
public abstract record BomLine
{
    public required string LineKey { get; init; }
    public ApplicabilityCondition? Condition { get; init; }
    public decimal Quantity { get; init; } = 1m;
}

public sealed record FrameBomLine : BomLine
{
    public required string FrameBodyCode { get; init; }
    public bool Colored { get; init; }
}

public sealed record FoamBomLine : BomLine
{
    public required string FoamCode { get; init; }
    public string? HardnessCode { get; init; }
}

public sealed record CottonBomLine : BomLine
{
    public required string CottonQualityCode { get; init; }
    public decimal Measurement { get; init; }
    public decimal CutUnits { get; init; }
    public decimal UnitConversionFactor { get; init; } = 1m;
}

public sealed record CutSortBomLine : BomLine
{
    public decimal Metrage { get; init; }
    public decimal CutUnits { get; init; }
    public Dictionary<string, decimal> SecondaryGroupMetrages { get; init; } = [];
}

public sealed record MiscBomLine : BomLine
{
    public required string MaterialCode { get; init; }
    public decimal UnitConversionFactor { get; init; } = 1m;
}

public sealed record LaborBomLine : BomLine
{
    public required string OperationCode { get; init; }
    public decimal Units { get; init; }
}
