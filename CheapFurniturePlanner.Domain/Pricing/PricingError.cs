namespace CheapFurniturePlanner.Domain.Pricing;

public enum PricingErrorKind
{
    IncompleteConfiguration,
    UnknownModel,
    UnknownElement,
    UnknownOptionSelection,
    SelectionViolatesVisibility,
    UnknownFabricColor,
    NoPriceGroupForMaterialKind,
    UnknownMaterialKind,
    MissingBomSection,
    UnknownOperation,
    UnknownMaterial,
    UnknownFrameBody,
    UnknownMarket
}

public record PricingError(PricingErrorKind Kind, string Subject, string? Detail = null);
