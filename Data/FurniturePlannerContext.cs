using CheapFurniturePlanner.Models;
using CheapHelpers.EF;
using CheapHelpers.EF.Infrastructure;
using CheapHelpers.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL;

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
    public DbSet<PublishedCatalogue> PublishedCatalogues { get; set; }
    public DbSet<ModelStateRecord> ModelStates => Set<ModelStateRecord>();
    public DbSet<AuthoringModelDocument> AuthoringModels => Set<AuthoringModelDocument>();
    public DbSet<AuthoringMastersDocument> AuthoringMasters => Set<AuthoringMastersDocument>();
    public DbSet<AuthoringArticlesDocument> AuthoringArticles => Set<AuthoringArticlesDocument>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<Seller> Sellers => Set<Seller>();
    public DbSet<Consumer> Consumers => Set<Consumer>();
    public DbSet<DiscountRule> DiscountRules => Set<DiscountRule>();
    public DbSet<ServiceTicket> ServiceTickets => Set<ServiceTicket>();
    public DbSet<ServiceTicketLine> ServiceTicketLines => Set<ServiceTicketLine>();
    public DbSet<ServiceTicketLog> ServiceTicketLogs => Set<ServiceTicketLog>();
    public DbSet<ServiceTicketPhoto> ServiceTicketPhotos => Set<ServiceTicketPhoto>();
    public DbSet<InternalRepair> InternalRepairs => Set<InternalRepair>();
    public DbSet<SupplierReport> SupplierReports => Set<SupplierReport>();
    public DbSet<ProductionUnit> ProductionUnits => Set<ProductionUnit>();
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<MarketVatRate> MarketVatRates => Set<MarketVatRate>();
    public DbSet<CreditNote> CreditNotes => Set<CreditNote>();
    public DbSet<Region> Regions => Set<Region>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Firm> Firms => Set<Firm>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<ConsumerDeliveryAddress> ConsumerDeliveryAddresses => Set<ConsumerDeliveryAddress>();
    public DbSet<SupplierModelMap> SupplierModelMaps => Set<SupplierModelMap>();
    public DbSet<SupplierOrder> SupplierOrders => Set<SupplierOrder>();
    public DbSet<SupplierDelivery> SupplierDeliveries => Set<SupplierDelivery>();
    public DbSet<MaterialStock> MaterialStocks => Set<MaterialStock>();
    public DbSet<MaterialOrder> MaterialOrders => Set<MaterialOrder>();
    public DbSet<MaterialOrderLine> MaterialOrderLines => Set<MaterialOrderLine>();
    public DbSet<MaterialProfile> MaterialProfiles => Set<MaterialProfile>();
    public DbSet<MaterialSupplierTerm> MaterialSupplierTerms => Set<MaterialSupplierTerm>();
    public DbSet<MaterialMovement> MaterialMovements => Set<MaterialMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Call base configuration first (includes CheapContext configuration)
        base.OnModelCreating(modelBuilder);

        ConfigureFurnitureEntities(modelBuilder);
        SeedDefaultData(modelBuilder);
    }

    // Instance method (not static) so the ConsumerDeliveryAddress index below can read
    // this.Database.IsNpgsql() to pick the right filtered-index syntax for the provider.
    private void ConfigureFurnitureEntities(ModelBuilder modelBuilder)
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

            // Default value for CreatedAt: SQLite and Npgsql speak different SQL for "now" ("DATETIME('now')"
            // is SQLite-only syntax, invalid on Postgres). Branch on the same Database.IsNpgsql() pattern
            // used elsewhere in this method - SQLite side is untouched so has-pending-model-changes stays clean.
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(Database.IsNpgsql() ? "now()" : "DATETIME('now')");
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

            // Default value for CreatedAt - see the FurnitureItem block above for why this branches.
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(Database.IsNpgsql() ? "now()" : "DATETIME('now')");
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

            // Default value for CreatedAt - see the FurnitureItem block above for why this branches.
            entity.Property(e => e.CreatedAt).HasDefaultValueSql(Database.IsNpgsql() ? "now()" : "DATETIME('now')");
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
                .OnDelete(DeleteBehavior.Restrict) // Don't delete furniture catalog items
                .IsRequired(false);
        });

        // Configure PublishedCatalogue
        modelBuilder.Entity<PublishedCatalogue>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Version).IsUnique();
            entity.HasIndex(e => e.IsCurrent);
            // Default value for PublishedAt - see the FurnitureItem block above for why this branches.
            entity.Property(e => e.PublishedAt).HasDefaultValueSql(Database.IsNpgsql() ? "now()" : "DATETIME('now')");
        });

        modelBuilder.Entity<ModelStateRecord>().HasIndex(s => s.ModelCode).IsUnique();
        modelBuilder.Entity<ModelStateRecord>().Property(s => s.State).HasConversion<string>();

        modelBuilder.Entity<AuthoringModelDocument>(entity =>
        {
            entity.HasIndex(m => m.ModelCode).IsUnique();
            // MB1: SQLite has no rowversion, so Version is a plain int concurrency token bumped
            // explicitly by AuthoringCatalogueStore's save path (the simplest option per the plan -
            // no SaveChanges interceptor). A stale save (loaded-then-changed-underneath) throws
            // DbUpdateConcurrencyException, which the store turns into a friendly "reload" error.
            entity.Property(m => m.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<AuthoringMastersDocument>().Property(m => m.Version).IsConcurrencyToken();
        modelBuilder.Entity<AuthoringArticlesDocument>().Property(m => m.Version).IsConcurrencyToken();

        modelBuilder.Entity<ServiceTicket>(entity =>
        {
            entity.HasIndex(t => t.TicketNumber).IsUnique();
            entity.Property(t => t.State).HasConversion<string>();
            entity.Property(t => t.Flow).HasConversion<string>();
            entity.HasMany(t => t.Lines).WithOne().HasForeignKey(l => l.TicketId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(t => t.Logs).WithOne().HasForeignKey(l => l.TicketId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(t => t.Photos).WithOne().HasForeignKey(p => p.TicketId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ServiceTicketPhoto>().Property(p => p.Kind).HasConversion<string>();
        modelBuilder.Entity<InternalRepair>(entity =>
        {
            entity.HasKey(r => r.TicketId);
            entity.HasOne<ServiceTicket>().WithOne(t => t.InternalRepair)
                .HasForeignKey<InternalRepair>(r => r.TicketId).OnDelete(DeleteBehavior.Cascade);
            entity.Property(r => r.Outcome).HasConversion<string>();
        });
        modelBuilder.Entity<SupplierReport>(entity =>
        {
            entity.HasKey(r => r.TicketId);
            entity.HasOne<ServiceTicket>().WithOne(t => t.SupplierReport)
                .HasForeignKey<SupplierReport>(r => r.TicketId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.Property(o => o.DeliverToWarehouse).HasDefaultValue(true);
        });
        modelBuilder.Entity<ProductionUnit>(entity =>
        {
            entity.HasIndex(u => u.UnitCode).IsUnique();
            entity.HasIndex(u => u.OrderId);
            entity.Property(u => u.State).HasConversion<string>();
            entity.HasOne(u => u.Order).WithMany().HasForeignKey(u => u.OrderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<OrderLine>().WithMany().HasForeignKey(u => u.OrderLineId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(u => u.Trip).WithMany(t => t.Units).HasForeignKey(u => u.TripId).OnDelete(DeleteBehavior.SetNull);
            // MB1: concurrency token over State/TripId/LoadPosition - see AuthoringModelDocument's
            // Version above for why it's a plain bumped int rather than a rowversion.
            entity.Property(u => u.Version).IsConcurrencyToken();
        });
        modelBuilder.Entity<Trip>(entity =>
        {
            entity.HasIndex(t => t.TripCode).IsUnique();
            entity.Property(t => t.State).HasConversion<string>();
            entity.HasOne(t => t.Region).WithMany().HasForeignKey(t => t.RegionId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasIndex(i => i.InvoiceNumber).IsUnique();
            entity.HasIndex(i => i.OrderId).IsUnique();
            entity.HasOne(i => i.Order).WithMany().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(i => i.CreditNotes).WithOne().HasForeignKey(c => c.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<MarketVatRate>().HasIndex(r => r.MarketCode).IsUnique();
        modelBuilder.Entity<CreditNote>(entity =>
        {
            entity.HasIndex(c => c.CreditNoteNumber).IsUnique();
            entity.Property(c => c.Reason).HasConversion<string>();
        });

        modelBuilder.Entity<Region>().HasIndex(r => r.Code).IsUnique();
        modelBuilder.Entity<Address>(entity =>
        {
            entity.HasOne(a => a.Region).WithMany().HasForeignKey(a => a.RegionId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.HasIndex(s => s.Code).IsUnique();
            entity.HasOne(s => s.Address).WithMany().HasForeignKey(s => s.AddressId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Firm>(entity =>
        {
            entity.HasIndex(f => f.Code).IsUnique();
            entity.HasOne(f => f.Address).WithMany().HasForeignKey(f => f.AddressId).OnDelete(DeleteBehavior.Restrict);
            // MB1 backstop, same pattern as the ConsumerDeliveryAddresses HD1 index below:
            // FirmService.SetDefaultAsync already enforces "exactly one default firm" in code, but a
            // filtered unique index holds the invariant against a raw insert that bypasses the
            // service too. Unlike the consumer address index this isn't scoped to a parent id - there
            // is exactly one default firm system-wide, so the filtered column stands alone. Same
            // provider branch as the ConsumerDeliveryAddresses index (see its comment for why).
            if (Database.IsNpgsql())
            {
                entity.HasIndex(f => f.IsDefault).IsUnique().HasFilter("\"IsDefault\"").HasDatabaseName("IX_Firms_OneDefault");
            }
            else
            {
                entity.HasIndex(f => f.IsDefault).IsUnique().HasFilter("IsDefault = 1").HasDatabaseName("IX_Firms_OneDefault");
            }
        });
        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasIndex(c => c.Code).IsUnique();
            entity.HasOne(c => c.Firm).WithMany().HasForeignKey(c => c.FirmId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Order>().HasOne(o => o.Firm).WithMany().HasForeignKey(o => o.FirmId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ConsumerDeliveryAddress>(entity =>
        {
            // HD1 backstop: PartyService.SetDefaultDeliveryAddressAsync already enforces "one default
            // per consumer" in code (clear siblings, then set), but a filtered unique index makes the
            // invariant hold even against a raw insert that bypasses the service. Replaces the old
            // plain ConsumerId index - EF Core keys index builders by property set, so a second
            // HasIndex(d => d.ConsumerId) call reconfigures the same index rather than adding a
            // distinct one.
            //
            // The filter predicate is raw provider SQL, so it branches on Database.ProviderName
            // (populated straight from the DbContextOptions the caller supplied - no open connection
            // needed, so this resolves correctly under the design-time factory, the real app, and
            // the in-memory-SQLite test harness every existing test uses). SQLite takes a plain
            // boolean predicate over the stored INTEGER 0/1 column; Npgsql needs the quoted,
            // case-sensitive column identifier as the boolean predicate itself.
            if (Database.IsNpgsql())
            {
                entity.HasIndex(d => d.ConsumerId).IsUnique().HasFilter("\"IsDefault\"").HasDatabaseName("IX_ConsumerDeliveryAddresses_ConsumerId_OneDefault");
            }
            else
            {
                entity.HasIndex(d => d.ConsumerId).IsUnique().HasFilter("IsDefault = 1").HasDatabaseName("IX_ConsumerDeliveryAddresses_ConsumerId_OneDefault");
            }
            entity.HasOne<Consumer>().WithMany().HasForeignKey(d => d.ConsumerId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(d => d.Address).WithMany().HasForeignKey(d => d.AddressId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<Seller>().HasOne(s => s.Address).WithMany().HasForeignKey(s => s.AddressId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Consumer>().HasOne(c => c.PrimaryAddress).WithMany().HasForeignKey(c => c.PrimaryAddressId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Order>().HasOne(o => o.DeliveryAddress).WithMany().HasForeignKey(o => o.DeliveryAddressId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<OrderLine>().HasOne(l => l.Supplier).WithMany().HasForeignKey(l => l.SupplierId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<SupplierReport>().HasOne(r => r.Supplier).WithMany().HasForeignKey(r => r.SupplierId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<SupplierModelMap>(entity =>
        {
            entity.HasIndex(m => m.ModelCode).IsUnique();
            entity.HasOne<Supplier>().WithMany().HasForeignKey(m => m.SupplierId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<SupplierOrder>(entity =>
        {
            entity.HasIndex(o => o.PoNumber).IsUnique();
            entity.Property(o => o.State).HasConversion<string>();
            entity.HasOne(o => o.Supplier).WithMany().HasForeignKey(o => o.SupplierId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(o => o.Units).WithOne(u => u.SupplierOrder).HasForeignKey(u => u.SupplierOrderId).OnDelete(DeleteBehavior.SetNull);
        });
        modelBuilder.Entity<SupplierDelivery>(entity =>
        {
            entity.HasIndex(d => new { d.SupplierId, d.Reference }).IsUnique();
            entity.HasOne(d => d.Supplier).WithMany().HasForeignKey(d => d.SupplierId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(d => d.Units).WithOne(u => u.SupplierDelivery).HasForeignKey(u => u.SupplierDeliveryId).OnDelete(DeleteBehavior.SetNull);
        });

        // Task 4b: same disease as the DiscountRule backstop below, on these three unique indexes -
        // HardnessCode stays null for every non-Foam material (and hardness-less Foam), and SQLite/
        // Postgres both compare NULL <> NULL under a unique index, so two writers racing for the same
        // material identity could both insert, silently splitting one balance/profile/term across two
        // rows. No schema change fixes this (a filtered/COALESCE index is DDL this task doesn't touch) -
        // instead every row-layer read and write of HardnessCode on these three tables (and their
        // shared retry helper, MaterialStockUpsertRetry) now normalizes to the empty-string sentinel
        // instead of null, so the SAME index genuinely collides on a real race. MaterialHardnessBackfill
        // rewrites pre-existing null rows to "" at startup (Program.cs, right after Database.Migrate()),
        // merging any split it finds. The Domain layer (MaterialRequirements.Resolve, MaterialNeedLine)
        // keeps its null-for-non-Foam output unchanged - the sentinel is a row-layer concern only.
        modelBuilder.Entity<MaterialStock>(entity =>
        {
            entity.HasIndex(s => new { s.Kind, s.Code, s.HardnessCode }).IsUnique();
            entity.Property(s => s.Kind).HasConversion<string>();
        });
        modelBuilder.Entity<MaterialOrder>(entity =>
        {
            entity.HasIndex(o => o.Number).IsUnique();
            entity.Property(o => o.State).HasConversion<string>();
            entity.HasOne(o => o.Supplier).WithMany().HasForeignKey(o => o.SupplierId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.MaterialOrderId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<MaterialOrderLine>(entity => entity.Property(l => l.Kind).HasConversion<string>());
        modelBuilder.Entity<MaterialProfile>(entity =>
        {
            entity.HasIndex(p => new { p.Kind, p.Code, p.HardnessCode }).IsUnique();
            entity.Property(p => p.Kind).HasConversion<string>();
        });
        modelBuilder.Entity<MaterialSupplierTerm>(entity =>
        {
            entity.HasIndex(t => new { t.Kind, t.Code, t.HardnessCode, t.SupplierId }).IsUnique();
            entity.Property(t => t.Kind).HasConversion<string>();
            entity.HasOne(t => t.Supplier).WithMany().HasForeignKey(t => t.SupplierId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<MaterialMovement>(entity =>
        {
            entity.Property(m => m.Type).HasConversion<string>();
            entity.Property(m => m.Kind).HasConversion<string>();
        });

        // MB1 backstop, corrected: a raw composite index over the 8 nullable scope columns can never
        // fire against DiscountService.AddRuleAsync's own validated output, because every valid rule
        // shape (Validate's needsElement/needsModel/needsModelType/needsMaterialType exclusivity)
        // leaves several of those columns null, and SQLite/Postgres both compare NULL <> NULL in a
        // unique index - so two identical legal rules never collide. Instead the service computes
        // DiscountRule.IdentityKey, a single string collapsing every null scope column to a fixed
        // sentinel token, and this index is unique on (SellerId, IdentityKey) - now it genuinely
        // catches an identical rule inserted outside AddRuleAsync's own guard.
        //
        // Filtered to non-empty keys: pre-MB1 rows (and rows between migration and the startup
        // backfill in DiscountService.BackfillIdentityKeysAsync, called from Program.cs right after
        // Database.Migrate()) carry the IdentityKey default (""), and several such rows under one
        // seller would otherwise collide with each other and break the migration itself. Once the
        // backfill runs, no row is left with an empty key, so the filter never hides a real duplicate
        // in practice - it only protects the narrow migration-time window. Same provider-branched
        // filter syntax as the ConsumerDeliveryAddresses/Firms indexes above (see their comments).
        modelBuilder.Entity<DiscountRule>(entity =>
        {
            if (Database.IsNpgsql())
            {
                entity.HasIndex(r => new { r.SellerId, r.IdentityKey }).IsUnique()
                    .HasFilter("\"IdentityKey\" <> ''").HasDatabaseName("IX_DiscountRules_SellerId_IdentityKey");
            }
            else
            {
                entity.HasIndex(r => new { r.SellerId, r.IdentityKey }).IsUnique()
                    .HasFilter("IdentityKey <> ''").HasDatabaseName("IX_DiscountRules_SellerId_IdentityKey");
            }
        });

        // MB-1 Task 2: Npgsql maps DateTime/DateTime? to "timestamp with time zone" (timestamptz)
        // by default. That default is correct for every INSTANT property on this model (CreatedAt,
        // SentAt, PlacedAt, IssuedAt, OccurredAt, UpdatedAt, ExportedAt and the rest) - each one is
        // written from DateTime.UtcNow (verified against every write site), so timestamptz fits
        // exactly and none of them are touched here.
        //
        // A handful of properties are CALENDAR DATES instead: day-precision values a person picks
        // (a promised delivery day, an expected delivery day) with no meaningful time zone -
        // callers pass DateTimeKind.Unspecified values, which Npgsql's modern (non-legacy)
        // timestamp behavior refuses to write against timestamptz. Each is pinned to
        // "timestamp without time zone" here, scoped to the Npgsql branch only - SQLite has no
        // separate tz-aware type, so it is unaffected and this whole block is a no-op there.
        // No property CLR type changes (no DateOnly migration) - zero model ripple, per plan.
        //
        // RULE: classify by the DateTimeKind the write sites actually produce, never by semantic
        // day-ness. A property that reads like a "day" in the domain (an invoice due date, a
        // catalogue effective date) is still an INSTANT if every write site carries Kind=Utc - a
        // Utc-kinded day-precision value is an instant, and pinning it here throws under Npgsql
        // instead of fixing anything (Npgsql rejects a Utc DateTime against
        // "timestamp without time zone" the same way it rejects Unspecified against timestamptz).
        // Invoice.DueDate and PublishedCatalogue.EffectiveDate look like calendar dates but are
        // Utc-kinded at every write site (PublishVersionDialog.razor SpecifyKind(...,Utc);
        // CataloguePublishService falls back to UtcNow; InvoicingService derives DueDate from
        // issuedAt.AddDays(30), and issuedAt is UtcNow) - they stay off this list.
        if (Database.IsNpgsql())
        {
            modelBuilder.Entity<Order>().Property(o => o.PromisedDeliveryDate).HasColumnType("timestamp without time zone");
            modelBuilder.Entity<SupplierDelivery>().Property(d => d.ExpectedDate).HasColumnType("timestamp without time zone");
            modelBuilder.Entity<Trip>().Property(t => t.DepartureDate).HasColumnType("timestamp without time zone");
            modelBuilder.Entity<InternalRepair>().Property(r => r.ExecutionDate).HasColumnType("timestamp without time zone");
        }
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