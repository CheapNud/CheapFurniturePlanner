namespace CheapFurniturePlanner.Domain.Bom;

public record SubstitutionRule(ApplicabilityCondition When, string ReplaceMaterialCode, string WithMaterialCode, decimal? QuantityOverride);
