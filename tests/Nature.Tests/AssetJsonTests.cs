using PropGenerator.Nature.Export;
using PropGenerator.Nature.Parameters;
using Xunit;

namespace PropGenerator.Nature.Tests;

public class AssetJsonTests
{
    [Fact]
    public void AStillPropIsWrittenAsEasyPropsWritesIt()
    {
        var parameters = new ParameterDictionary();
        parameters.Set("width", 1.5);
        parameters.Set("kind", "boulder");
        string json = AssetJson.Write("rock", "plain", ExportLook.Cel, [12, 4], null, new AssetDescription("cut-rock", 1, 7, "low-poly", parameters, "0.4.2"));
        Assert.Equal(
            """
            {
                "format": 1,
                "name": "rock",
                "engine": "plain",
                "look": "cel",
                "faces": {
                    "LOD0": 12,
                    "LOD1": 4
                },
                "wind": null,
                "far": null,
                "generator": "cut-rock",
                "generatorVersion": 1,
                "seed": 7,
                "style": "low-poly",
                "parameters": {
                    "width": 1.5,
                    "kind": "boulder"
                },
                "appVersion": "0.4.2"
            }

            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            json);
    }
}
