# Cheap Furniture Planner

> **⚠️ Hobby Project Warning**: This is a personal hobby project built for learning and experimentation. It may contain incomplete features, bugs, or undergo breaking changes without notice. Use at your own risk.

A desktop furniture room layout planning application built with Avalonia and Blazor. Design room layouts by placing furniture items on a virtual grid canvas with support for positioning, rotation, collision detection, and real-time visualization.

## Features

- **Furniture Catalog**: Browse, search, and manage furniture items with 20+ types (Sofa, Chair, Table, Bed, etc.)
- **Interactive Room Planning**: Drag-and-drop furniture placement with collision detection and grid snapping
- **Canvas Controls**: Rotate items (0-360°), multi-select (Shift/Ctrl), group furniture, customize grid settings
- **Modern UI**: Material Design with MudBlazor, dark/light themes, responsive layouts
- **Data Management**: SQLite database, usage statistics, auto-save, export/import room layouts

## Technology Stack

- **C# 13** / **.NET 10.0**
- **Avalonia** (desktop) + **Blazor Server** + **MudBlazor 8.13.0** (UI)
- **SQLite** with **Entity Framework Core 9.0.10**
- **Mapster 7.4.0** (object mapping)
- **CheapAvaloniaBlazor** (1.1.3) - Custom hosting framework
- **CheapHelpers** (1.1.2) - Utilities and EF extensions

## Architecture

**Clean architecture with layered separation:**
- **Models** (`/Models`): Domain entities (FurnitureItem, RoomPlan, PlannerFurnitureItem)
- **Data Access** (`/Data`, `/Repositories`): EF Core context and repository pattern with IDbContextFactory
- **Services** (`/Services`): Business logic (FurnitureCatalogService, RoomPlanService, PlannerService)
- **View Models** (`/ViewModels`): DTOs for UI binding
- **Components** (`/Components`): Blazor pages and shared components with MudBlazor

## Installation

**Prerequisites**: .NET 10.0 SDK or later

```bash
git clone https://github.com/CheapNud/CheapFurniturePlanner.git
cd CheapFurniturePlanner
dotnet restore
dotnet run
```

Database is created automatically at `%LocalAppData%\CheapFurniturePlanner\furniture_planner.db` with sample data.

## Usage

1. **Create Room Plan**: Navigate to Room Plans → Create New Room Plan → Configure dimensions and grid settings
2. **Add Furniture**: Go to Furniture Catalog → Add Furniture → Fill in dimensions and properties
3. **Design Layout**: Open room plan → Add furniture from catalog → Drag, rotate, and position items on canvas
4. **Customize**: Use grid snapping, collision detection, grouping, and custom notes for organization

## Code Examples

**Repository Pattern** (`Repositories/FurniturePlannerRepository.cs:87`):
```csharp
public async Task<List<FurnitureItem>> GetActiveFurnitureAsync(CancellationToken ct = default)
{
    using var context = _contextFactory.CreateDbContext();
    return await context.FurnitureItems
        .Where(f => f.IsActive)
        .OrderBy(f => f.Type)
        .ToListAsync(ct);
}
```

**Service Injection** (`Program.cs`):
```csharp
builder.Services.AddScoped<FurnitureCatalogService>();
builder.Services.AddScoped<RoomPlanService>();
builder.Services.AddScoped<PlannerService>();
```

## Database Schema

**Three main tables:**
- **FurnitureItem**: Catalog with dimensions, properties, and metadata (unique Code index)
- **RoomPlan**: Room layouts with configuration (Width, Height, Unit, GridSize, flags)
- **PlannerFurnitureItem**: Furniture instances with position (X, Y), Rotation, GroupId, Notes

**Relationships**: RoomPlan → PlannerFurnitureItem (CASCADE), FurnitureItem → PlannerFurnitureItem (RESTRICT)

## Development

**Migrations**: `dotnet ef migrations add MigrationName` → `dotnet ef database update`

**Code Style**: C# 13 features (primary constructors, collection expressions), async/await, LINQ lambda syntax, MudBlazor components

**Testing**: xUnit, BUnit (Blazor), Moq

## Author

**CheapNud** - [GitHub](https://github.com/CheapNud)
