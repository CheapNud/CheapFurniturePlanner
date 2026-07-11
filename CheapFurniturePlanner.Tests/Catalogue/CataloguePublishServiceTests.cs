using CheapFurniturePlanner.Catalogue;
using CheapFurniturePlanner.Data;
using CheapFurniturePlanner.Domain.Bom;
using CheapFurniturePlanner.Domain.Catalog;
using CheapFurniturePlanner.Domain.Fabrics;
using CheapFurniturePlanner.Domain.Masters;
using CheapFurniturePlanner.Domain.Options;
using CheapFurniturePlanner.Domain.Pricing;
using CheapFurniturePlanner.Domain.Serialization;
using CheapFurniturePlanner.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CheapFurniturePlanner.Tests.Catalogue;

public class CataloguePublishServiceTests
{
    private static (IDbContextFactory<FurniturePlannerContext> Factory, SqliteConnection Connection) NewFactory()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<FurniturePlannerContext>().UseSqlite(connection).Options;
        using (var migrateContext = new FurniturePlannerContext(options))
        {
            migrateContext.Database.Migrate();
        }
        return (new TestDbContextFactory(options), connection);
    }

    [Fact]
    public async Task PublishAsync_ValidSnapshot_WritesRowFlipsCurrentAndReturnsNextVersion()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            using (var seedContext = factory.CreateDbContext())
            {
                seedContext.PublishedCatalogues.Add(new PublishedCatalogue
                {
                    Version = "3",
                    ContentHash = "irrelevant-hash",
                    BundleJson = CanonicalJson.Serialize(new CatalogueSnapshot { Version = "3" }),
                    IsCurrent = true,
                });
                seedContext.SaveChanges();
            }

            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                PriceGroups = [new PriceGroup { Code = "PGA", RatePerMeter = 21.50m }],
                FabricGroups = [new FabricGroup { Code = "AQUA", PriceGroupCode = "PGA" }],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.True(result.Success);
            Assert.Empty(result.Errors);
            Assert.Equal("4", result.Version);
            Assert.True(source.Invalidated);

            using var verifyContext = factory.CreateDbContext();
            var rows = verifyContext.PublishedCatalogues.OrderBy(c => c.Version).ToList();
            Assert.Equal(2, rows.Count);
            Assert.False(rows.Single(r => r.Version == "3").IsCurrent);
            var newRow = rows.Single(r => r.Version == "4");
            Assert.True(newRow.IsCurrent);
            Assert.NotEmpty(newRow.ContentHash);
        }
    }

    [Fact]
    public async Task PublishAsync_DanglingPriceGroupReference_FailsAndWritesNoRow()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                FabricGroups = [new FabricGroup { Code = "AQUA", PriceGroupCode = "MISSING" }],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("MISSING"));
            Assert.Null(result.Version);
            Assert.False(source.Invalidated);

            using var verifyContext = factory.CreateDbContext();
            Assert.Empty(verifyContext.PublishedCatalogues);
        }
    }

    [Fact]
    public async Task PublishAsync_ReservedMaterialOptionCode_FailsValidation()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                Models =
                [
                    new FurnitureModel
                    {
                        Code = "M1",
                        Name = "Model One",
                        Elements =
                        [
                            new Element
                            {
                                Code = "E1",
                                Name = "Element One",
                                Options = [new ChoiceOption { OptionDefinitionCode = VariantCode.MaterialDefCode }],
                            },
                        ],
                    },
                ],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains(VariantCode.MaterialDefCode));
            Assert.Null(result.Version);

            using var verifyContext = factory.CreateDbContext();
            Assert.Empty(verifyContext.PublishedCatalogues);
        }
    }

    [Fact]
    public async Task PublishAsync_DanglingCottonMaterialReference_FailsAndWritesNoRow()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                Models =
                [
                    new FurnitureModel
                    {
                        Code = "M1",
                        Name = "Model One",
                        Elements =
                        [
                            new Element
                            {
                                Code = "E1",
                                Name = "Element One",
                                Bom = new BomDocument
                                {
                                    Sections =
                                    [
                                        new BomSection
                                        {
                                            Kind = BomSectionKind.Cotton,
                                            Lines =
                                            [
                                                new CottonBomLine { LineKey = "cotton1", CottonQualityCode = "MISSING" },
                                            ],
                                        },
                                    ],
                                },
                            },
                        ],
                    },
                ],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("MISSING"));
            Assert.Null(result.Version);
            Assert.False(source.Invalidated);

            using var verifyContext = factory.CreateDbContext();
            Assert.Empty(verifyContext.PublishedCatalogues);
        }
    }

    [Fact]
    public async Task PublishAsync_DanglingCutSortSecondaryPriceGroupReference_FailsAndWritesNoRow()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                Models =
                [
                    new FurnitureModel
                    {
                        Code = "M1",
                        Name = "Model One",
                        Elements =
                        [
                            new Element
                            {
                                Code = "E1",
                                Name = "Element One",
                                Bom = new BomDocument
                                {
                                    Sections =
                                    [
                                        new BomSection
                                        {
                                            Kind = BomSectionKind.CutSort,
                                            Lines =
                                            [
                                                new CutSortBomLine
                                                {
                                                    LineKey = "cutsort1",
                                                    Metrage = 1m,
                                                    SecondaryGroupMetrages = new Dictionary<string, decimal> { ["MISSING"] = 0.5m },
                                                },
                                            ],
                                        },
                                    ],
                                },
                            },
                        ],
                    },
                ],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("MISSING"));
            Assert.Null(result.Version);
            Assert.False(source.Invalidated);

            using var verifyContext = factory.CreateDbContext();
            Assert.Empty(verifyContext.PublishedCatalogues);
        }
    }

    [Fact]
    public async Task PublishAsync_DanglingFabricGroupReference_FailsAndWritesNoRow()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                Models =
                [
                    new FurnitureModel
                    {
                        Code = "M1",
                        Name = "Model One",
                        Elements =
                        [
                            new Element
                            {
                                Code = "E1",
                                Name = "Element One",
                                Options = [new FabricOption { OptionDefinitionCode = "FABRIC", FabricGroupCodes = ["MISSING"] }],
                            },
                        ],
                    },
                ],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("MISSING"));
            Assert.Null(result.Version);
            Assert.False(source.Invalidated);

            using var verifyContext = factory.CreateDbContext();
            Assert.Empty(verifyContext.PublishedCatalogues);
        }
    }

    [Fact]
    public async Task PublishAsync_DanglingSubstitutionMaterialReference_FailsAndWritesNoRow()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                Models =
                [
                    new FurnitureModel
                    {
                        Code = "M1",
                        Name = "Model One",
                        Elements =
                        [
                            new Element
                            {
                                Code = "E1",
                                Name = "Element One",
                                Substitutions =
                                [
                                    new SubstitutionRule(new ApplicabilityCondition([]), "FM-STD", "MISSING", null),
                                ],
                            },
                        ],
                    },
                ],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("MISSING"));
            Assert.Null(result.Version);
            Assert.False(source.Invalidated);

            using var verifyContext = factory.CreateDbContext();
            Assert.Empty(verifyContext.PublishedCatalogues);
        }
    }

    [Fact]
    public async Task PublishAsync_DanglingSubstitutionReplaceMaterialReference_FailsAndWritesNoRow()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                Materials = [new Material("FM-FIRM", "Foam Firm", 10m, "pc")],
                Models =
                [
                    new FurnitureModel
                    {
                        Code = "M1",
                        Name = "Model One",
                        Elements =
                        [
                            new Element
                            {
                                Code = "E1",
                                Name = "Element One",
                                Substitutions =
                                [
                                    new SubstitutionRule(new ApplicabilityCondition([]), "MISSING", "FM-FIRM", null),
                                ],
                            },
                        ],
                    },
                ],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("substitution replaces missing material") && e.Contains("MISSING"));
            Assert.Null(result.Version);
            Assert.False(source.Invalidated);

            using var verifyContext = factory.CreateDbContext();
            Assert.Empty(verifyContext.PublishedCatalogues);
        }
    }

    [Fact]
    public async Task PublishAsync_DanglingSubstitutionCondition_FailsAndWritesNoRow()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                Materials = [new Material("FM-STD", "Foam Standard", 8m, "pc"), new Material("FM-FIRM", "Foam Firm", 10m, "pc")],
                Models =
                [
                    new FurnitureModel
                    {
                        Code = "M1",
                        Name = "Model One",
                        Elements =
                        [
                            new Element
                            {
                                Code = "E1",
                                Name = "Element One",
                                Substitutions =
                                [
                                    new SubstitutionRule(new ApplicabilityCondition([new SelectionKey("MECH2", "REC")]), "FM-STD", "FM-FIRM", null),
                                ],
                            },
                        ],
                    },
                ],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("substitution has a condition referencing unknown selection") && e.Contains("MECH2:REC"));
            Assert.Null(result.Version);
            Assert.False(source.Invalidated);

            using var verifyContext = factory.CreateDbContext();
            Assert.Empty(verifyContext.PublishedCatalogues);
        }
    }

    [Fact]
    public async Task PublishAsync_DanglingVisibilityRuleTrigger_FailsAndWritesNoRow()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                Models =
                [
                    new FurnitureModel
                    {
                        Code = "M1",
                        Name = "Model One",
                        Elements =
                        [
                            new Element
                            {
                                Code = "E1",
                                Name = "Element One",
                                Options =
                                [
                                    new ChoiceOption
                                    {
                                        OptionDefinitionCode = "HEAD",
                                        Values = [new ProductOptionValue { OptionChoiceCode = "HS1", IsDefault = true }],
                                        VisibilityRules = [new VisibilityRule("MECH", "REC", "HEAD")],
                                    },
                                ],
                            },
                        ],
                    },
                ],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("MECH:REC"));
            Assert.Null(result.Version);
            Assert.False(source.Invalidated);

            using var verifyContext = factory.CreateDbContext();
            Assert.Empty(verifyContext.PublishedCatalogues);
        }
    }

    [Fact]
    public async Task PublishAsync_ValidVisibilityRuleTrigger_PublishesSuccessfully()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                Models =
                [
                    new FurnitureModel
                    {
                        Code = "M1",
                        Name = "Model One",
                        Elements =
                        [
                            new Element
                            {
                                Code = "E1",
                                Name = "Element One",
                                Options =
                                [
                                    new ChoiceOption
                                    {
                                        OptionDefinitionCode = "MECH",
                                        Values = [new ProductOptionValue { OptionChoiceCode = "REC", IsDefault = true }],
                                    },
                                    new ChoiceOption
                                    {
                                        OptionDefinitionCode = "HEAD",
                                        Values = [new ProductOptionValue { OptionChoiceCode = "HS1", IsDefault = true }],
                                        VisibilityRules = [new VisibilityRule("MECH", "REC", "HEAD")],
                                    },
                                ],
                            },
                        ],
                    },
                ],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.True(result.Success);
            Assert.Empty(result.Errors);
            Assert.NotNull(result.Version);
        }
    }

    [Fact]
    public async Task PublishAsync_DanglingBomLineCondition_FailsAndWritesNoRow()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                Models =
                [
                    new FurnitureModel
                    {
                        Code = "M1",
                        Name = "Model One",
                        Elements =
                        [
                            new Element
                            {
                                Code = "E1",
                                Name = "Element One",
                                Options =
                                [
                                    new ChoiceOption
                                    {
                                        OptionDefinitionCode = "DEPTH2",
                                        Values = [new ProductOptionValue { OptionChoiceCode = "STD", IsDefault = true }],
                                    },
                                ],
                                Bom = new BomDocument
                                {
                                    Sections =
                                    [
                                        new BomSection
                                        {
                                            Kind = BomSectionKind.Foam,
                                            Lines =
                                            [
                                                new FoamBomLine
                                                {
                                                    LineKey = "foam1",
                                                    FoamCode = "FM-STD",
                                                    Condition = new ApplicabilityCondition([new SelectionKey("DEPTH2", "DEEP")]),
                                                },
                                            ],
                                        },
                                    ],
                                },
                            },
                        ],
                    },
                ],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("condition referencing unknown selection") && e.Contains("DEPTH2:DEEP"));
            Assert.Null(result.Version);
            Assert.False(source.Invalidated);

            using var verifyContext = factory.CreateDbContext();
            Assert.Empty(verifyContext.PublishedCatalogues);
        }
    }

    [Fact]
    public async Task PublishAsync_ValidBomLineCondition_PublishesSuccessfully()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                Materials = [new Material("FM-STD", "Standard Foam", 1m, "Unit")],
                Models =
                [
                    new FurnitureModel
                    {
                        Code = "M1",
                        Name = "Model One",
                        Elements =
                        [
                            new Element
                            {
                                Code = "E1",
                                Name = "Element One",
                                Options =
                                [
                                    new ChoiceOption
                                    {
                                        OptionDefinitionCode = "DEPTH2",
                                        Values =
                                        [
                                            new ProductOptionValue { OptionChoiceCode = "STD", IsDefault = true },
                                            new ProductOptionValue { OptionChoiceCode = "DEEP" },
                                        ],
                                    },
                                ],
                                Bom = new BomDocument
                                {
                                    Sections =
                                    [
                                        new BomSection
                                        {
                                            Kind = BomSectionKind.Foam,
                                            Lines =
                                            [
                                                new FoamBomLine
                                                {
                                                    LineKey = "foam1",
                                                    FoamCode = "FM-STD",
                                                    Condition = new ApplicabilityCondition([new SelectionKey("DEPTH2", "DEEP")]),
                                                },
                                            ],
                                        },
                                    ],
                                },
                            },
                        ],
                    },
                ],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.True(result.Success);
            Assert.Empty(result.Errors);
            Assert.NotNull(result.Version);
        }
    }

    [Fact]
    public async Task PublishAsync_ModelWithNoElements_FailsAndWritesNoRow()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);
            var snapshot = new CatalogueSnapshot
            {
                Version = "irrelevant",
                Models =
                [
                    new FurnitureModel { Code = "EMPTY", Name = "Empty Model", Elements = [] },
                ],
            };

            var result = await service.PublishAsync(snapshot);

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("no elements"));
            Assert.Null(result.Version);
            Assert.False(source.Invalidated);

            using var verifyContext = factory.CreateDbContext();
            Assert.Empty(verifyContext.PublishedCatalogues);
        }
    }

    [Fact]
    public async Task PublishAsync_EmbeddedFjordSeed_PublishesSuccessfully()
    {
        var (factory, connection) = NewFactory();
        using (connection)
        {
            var source = new FakeCatalogueSource();
            var service = new CataloguePublishService(factory, source);

            var asm = typeof(CataloguePublishService).Assembly;
            using var stream = asm.GetManifestResourceStream("CheapFurniturePlanner.Seed.demo-catalogue.json");
            Assert.NotNull(stream);
            using var reader = new StreamReader(stream!);
            var json = reader.ReadToEnd();
            var snapshot = CanonicalJson.Deserialize<CatalogueSnapshot>(json);
            Assert.NotNull(snapshot);

            var result = await service.PublishAsync(snapshot!);

            Assert.True(result.Success);
            Assert.Empty(result.Errors);
            Assert.Equal("1", result.Version);

            using var verifyContext = factory.CreateDbContext();
            var row = Assert.Single(verifyContext.PublishedCatalogues);
            Assert.True(row.IsCurrent);
        }
    }

    private sealed class FakeCatalogueSource : ICatalogueSource
    {
        public bool Invalidated { get; private set; }
        public Task<CatalogueSnapshot> GetCurrentAsync() => throw new NotSupportedException();
        public void Invalidate() => Invalidated = true;
    }

    private sealed class TestDbContextFactory(DbContextOptions<FurniturePlannerContext> options) : IDbContextFactory<FurniturePlannerContext>
    {
        public FurniturePlannerContext CreateDbContext() => new(options);

        public Task<FurniturePlannerContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
}
