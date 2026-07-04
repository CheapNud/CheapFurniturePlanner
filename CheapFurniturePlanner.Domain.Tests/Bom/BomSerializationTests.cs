using System.Text.Json;
using CheapFurniturePlanner.Domain.Bom;
using Xunit;

namespace CheapFurniturePlanner.Domain.Tests.Bom;

public class BomSerializationTests
{
    [Fact]
    public void SerializeAndDeserializeBomDocument_WithAllLineTypes_PreservesStructure()
    {
        // Arrange
        var bomDocument = new BomDocument
        {
            Sections =
            [
                new BomSection
                {
                    Kind = BomSectionKind.Frame,
                    Lines =
                    [
                        new FrameBomLine
                        {
                            LineKey = "frame-1",
                            FrameBodyCode = "FB-001",
                            Colored = true,
                            Quantity = 1m
                        }
                    ]
                },
                new BomSection
                {
                    Kind = BomSectionKind.Foam,
                    Lines =
                    [
                        new FoamBomLine
                        {
                            LineKey = "foam-1",
                            FoamCode = "FOAM-50",
                            HardnessCode = "SOFT",
                            Quantity = 2.5m
                        }
                    ]
                },
                new BomSection
                {
                    Kind = BomSectionKind.Cotton,
                    Lines =
                    [
                        new CottonBomLine
                        {
                            LineKey = "cotton-1",
                            CottonQualityCode = "CQ-100",
                            Measurement = 10m,
                            CutUnits = 5m,
                            UnitConversionFactor = 1.5m
                        }
                    ]
                },
                new BomSection
                {
                    Kind = BomSectionKind.CutSort,
                    Lines =
                    [
                        new CutSortBomLine
                        {
                            LineKey = "cutsort-1",
                            Metrage = 100m,
                            CutUnits = 50m,
                            SecondaryGroupMetrages = new Dictionary<string, decimal> { { "Group1", 25m } }
                        }
                    ]
                },
                new BomSection
                {
                    Kind = BomSectionKind.Misc,
                    Lines =
                    [
                        new MiscBomLine
                        {
                            LineKey = "misc-1",
                            MaterialCode = "MAT-001",
                            Quantity = 4m,
                            UnitConversionFactor = 0.8m
                        }
                    ]
                },
                new BomSection
                {
                    Kind = BomSectionKind.Labor,
                    Lines =
                    [
                        new LaborBomLine
                        {
                            LineKey = "labor-1",
                            OperationCode = "OP-ASSEMBLY",
                            Units = 2m
                        }
                    ]
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(bomDocument);
        var deserialized = JsonSerializer.Deserialize<BomDocument>(json)!;

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(6, deserialized.Sections.Count);

        // Verify each section and line is preserved
        Assert.Equal(BomSectionKind.Frame, deserialized.Sections[0].Kind);
        var frameLine = Assert.IsType<FrameBomLine>(deserialized.Sections[0].Lines[0]);
        Assert.Equal("frame-1", frameLine.LineKey);
        Assert.Equal("FB-001", frameLine.FrameBodyCode);
        Assert.True(frameLine.Colored);

        Assert.Equal(BomSectionKind.Foam, deserialized.Sections[1].Kind);
        var foamLine = Assert.IsType<FoamBomLine>(deserialized.Sections[1].Lines[0]);
        Assert.Equal("foam-1", foamLine.LineKey);
        Assert.Equal("FOAM-50", foamLine.FoamCode);
        Assert.Equal("SOFT", foamLine.HardnessCode);

        Assert.Equal(BomSectionKind.Cotton, deserialized.Sections[2].Kind);
        var cottonLine = Assert.IsType<CottonBomLine>(deserialized.Sections[2].Lines[0]);
        Assert.Equal("cotton-1", cottonLine.LineKey);
        Assert.Equal("CQ-100", cottonLine.CottonQualityCode);

        Assert.Equal(BomSectionKind.CutSort, deserialized.Sections[3].Kind);
        var cutSortLine = Assert.IsType<CutSortBomLine>(deserialized.Sections[3].Lines[0]);
        Assert.Equal("cutsort-1", cutSortLine.LineKey);

        Assert.Equal(BomSectionKind.Misc, deserialized.Sections[4].Kind);
        var miscLine = Assert.IsType<MiscBomLine>(deserialized.Sections[4].Lines[0]);
        Assert.Equal("misc-1", miscLine.LineKey);

        Assert.Equal(BomSectionKind.Labor, deserialized.Sections[5].Kind);
        var laborLine = Assert.IsType<LaborBomLine>(deserialized.Sections[5].Lines[0]);
        Assert.Equal("labor-1", laborLine.LineKey);
    }

    [Fact]
    public void SerializedJson_ContainsPolymorphicTypeDiscriminator()
    {
        // Arrange
        var bomDocument = new BomDocument
        {
            Sections =
            [
                new BomSection
                {
                    Kind = BomSectionKind.Frame,
                    Lines = [new FrameBomLine { LineKey = "f1", FrameBodyCode = "FB-001" }]
                }
            ]
        };

        // Act
        var json = JsonSerializer.Serialize(bomDocument);

        // Assert
        Assert.Contains("\"$type\"", json);
        Assert.Contains("\"frame\"", json);
    }
}
