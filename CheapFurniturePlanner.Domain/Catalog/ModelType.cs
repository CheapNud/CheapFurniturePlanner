namespace CheapFurniturePlanner.Domain.Catalog;

// The closed model taxonomy (user-dictated starter set). Extending it is a deliberate code change —
// chosen over free text so the ModelType discount tier can't be broken by a typo.
public enum ModelType { Classic, Relax, Corner, Custom }
