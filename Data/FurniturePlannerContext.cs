using CheapFurniturePlanner.Models;
using CheapHelpers.EF;
using CheapHelpers.EF.Infrastructure;
using CheapHelpers.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CheapFurniturePlanner.Data;

/// <summary>
/// Database context for the Furniture Planner application, extending CheapContext
/// </summary>
public class FurniturePlannerContext : CheapContext<FurnitureUser>
{
    public FurniturePlannerContext(
        DbContextOptions<FurniturePlannerContext> options,
        CheapContextOptions? contextOptions = null)
        : base(CreateCheapContextOptions(options), contextOptions)
    {
    }

    private static DbContextOptions<CheapContext<FurnitureUser>> CreateCheapContextOptions(DbContextOptions<FurniturePlannerContext> options)
    {
        var builder = new DbContextOptionsBuilder<CheapContext<FurnitureUser>>();

        // Copy the connection string and configuration from the FurniturePlannerContext options
        foreach (var extension in options.Extensions)
        {
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        }

        return builder.Options;
    }

    // Furniture Planner specific DbSets
    public DbSet<FurnitureItem> FurnitureItems { get; set; }
    public DbSet<RoomPlan> RoomPlans { get; set; }
    public DbSet<PlannerFurnitureItem> PlannerFurnitureItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Call base configuration first (includes CheapContext configuration)
        base.OnModelCreating(modelBuilder);

        ConfigureFurnitureEntities(modelBuilder);
        SeedDefaultData(modelBuilder);
    }

    private static void ConfigureFurnitureEntities(ModelBuilder modelBuilder)
    {
        // Configure FurnitureItem
        modelBuilder.Entity<FurnitureItem>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Indexes for performance
            entity.HasIndex(e => e.Code).IsUnique();
            entity.HasIndex(e => e.Type);
            entity.HasIndex(e => e.IsActive);
            entity.HasIndex(e => e.Name);

            // Default values for SQLite
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("DATETIME('now')");
            entity.Property(e => e.IsActive).HasDefaultValue(true);

            // Decimal precision for SQLite compatibility
            entity.Property(e => e.Width).HasColumnType("REAL");
            entity.Property(e => e.Length).HasColumnType("REAL");
            entity.Property(e => e.Height).HasColumnType("REAL");
            entity.Property(e => e.Weight).HasColumnType("REAL");
            entity.Property(e => e.Price).HasColumnType("REAL");
        });

        // Configure RoomPlan
        modelBuilder.Entity<RoomPlan>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Indexes
            entity.HasIndex(e => e.Name);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.CreatedBy);

            // Default values for SQLite
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("DATETIME('now')");
            entity.Property(e => e.ShowGrid).HasDefaultValue(true);
            entity.Property(e => e.PreventOverlap).HasDefaultValue(true);
            entity.Property(e => e.EnableSnapping).HasDefaultValue(true);
            entity.Property(e => e.GridSize).HasDefaultValue(10);
            entity.Property(e => e.Unit).HasDefaultValue("cm");

            // Decimal precision for SQLite compatibility
            entity.Property(e => e.Width).HasColumnType("REAL");
            entity.Property(e => e.Height).HasColumnType("REAL");
        });

        // Configure PlannerFurnitureItem
        modelBuilder.Entity<PlannerFurnitureItem>(entity =>
        {
            entity.HasKey(e => e.Id);

            // Indexes
            entity.HasIndex(e => e.RoomPlanId);
            entity.HasIndex(e => e.FurnitureItemId);
            entity.HasIndex(e => new { e.RoomPlanId, e.UIId }).IsUnique();
            entity.HasIndex(e => e.GroupId);

            // Default values for SQLite
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("DATETIME('now')");
            entity.Property(e => e.Rotation).HasDefaultValue(0);

            // Decimal precision for SQLite compatibility
            entity.Property(e => e.X).HasColumnType("REAL");
            entity.Property(e => e.Y).HasColumnType("REAL");
            entity.Property(e => e.Rotation).HasColumnType("REAL");

            // Foreign key relationships
            entity.HasOne(e => e.RoomPlan)
                .WithMany(r => r.FurnitureItems)
                .HasForeignKey(e => e.RoomPlanId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.FurnitureItem)
                .WithMany(f => f.PlannerItems)
                .HasForeignKey(e => e.FurnitureItemId)
                .OnDelete(DeleteBehavior.Restrict); // Don't delete furniture catalog items
        });
    }

    private static void SeedDefaultData(ModelBuilder modelBuilder)
    {
        // Seed some default furniture items
        modelBuilder.Entity<FurnitureItem>().HasData(
            new FurnitureItem
            {
                Id = 1,
                Code = "CHEAP-SOFA-001",
                Name = "Cheap 3-Seat Sofa",
                Description = "Comfortable 3-seat sofa for living room",
                Type = FurnitureType.Sofa,
                Width = 200,
                Length = 90,
                Height = 85,
                Weight = 45,
                Color = "Gray",
                Material = "Fabric",
                Price = 599.99m,
                Brand = "CheapFurniture",
                Model = "Comfort Plus",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new FurnitureItem
            {
                Id = 2,
                Code = "CHEAP-CHAIR-001",
                Name = "Cheap Office Chair",
                Description = "Ergonomic office chair with adjustable height",
                Type = FurnitureType.Chair,
                Width = 60,
                Length = 60,
                Height = 120,
                Weight = 15,
                Color = "Black",
                Material = "Mesh/Plastic",
                Price = 199.99m,
                Brand = "CheapOffice",
                Model = "Ergo Basic",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new FurnitureItem
            {
                Id = 3,
                Code = "CHEAP-TABLE-001",
                Name = "Cheap Dining Table",
                Description = "Rectangular dining table for 6 people",
                Type = FurnitureType.DiningTable,
                Width = 160,
                Length = 90,
                Height = 75,
                Weight = 35,
                Color = "Oak",
                Material = "Wood",
                Price = 399.99m,
                Brand = "CheapWood",
                Model = "Family",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new FurnitureItem
            {
                Id = 4,
                Code = "CHEAP-BED-001",
                Name = "Cheap Queen Bed",
                Description = "Queen size bed frame with headboard",
                Type = FurnitureType.Bed,
                Width = 160,
                Length = 200,
                Height = 100,
                Weight = 40,
                Color = "White",
                Material = "Wood/Metal",
                Price = 299.99m,
                Brand = "CheapSleep",
                Model = "Dream Queen",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new FurnitureItem
            {
                Id = 5,
                Code = "CHEAP-COFFEE-001",
                Name = "Cheap Coffee Table",
                Description = "Modern coffee table with storage",
                Type = FurnitureType.CoffeeTable,
                Width = 120,
                Length = 60,
                Height = 45,
                Weight = 20,
                Color = "Walnut",
                Material = "Wood",
                Price = 149.99m,
                Brand = "CheapStyle",
                Model = "Modern Store",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );

        // Seed a default room plan
        modelBuilder.Entity<RoomPlan>().HasData(
            new RoomPlan
            {
                Id = 1,
                Name = "Sample Living Room",
                Description = "A sample living room layout",
                Width = 500,
                Height = 400,
                Unit = "cm",
                GridSize = 10,
                ShowGrid = true,
                PreventOverlap = true,
                EnableSnapping = true,
                CreatedBy = "System",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}