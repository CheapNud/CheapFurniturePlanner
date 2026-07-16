using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CheapFurniturePlanner.Domain.Tests")]
// OrderEntryService's bridge-stamp helper resolves fabric price group -> material type the same way
// ProductionIdentityResolver does, calling MaterialResolution directly rather than duplicating it publicly.
[assembly: InternalsVisibleTo("CheapFurniturePlanner")]
