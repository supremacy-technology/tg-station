using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Parallax;
using Content.Server.Weather;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Weather;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Lavaland;

[TestFixture]
public sealed class WeatherDiagTest : GameTest
{
    [Test]
    public async Task WeatherOnPlainVsPlanetMap()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var mapSys = server.System<SharedMapSystem>();
        var weather = server.System<WeatherSystem>();
        var biome = server.System<BiomeSystem>();
        var protoMan = server.ResolveDependency<IPrototypeManager>();

        string? plainError = null;
        string? planetError = null;
        string? planetState = null;

        await server.WaitPost(() =>
        {
            // Case A: plain map, no grid.
            mapSys.CreateMap(out var plainId);
            try
            {
                weather.TrySetWeather(plainId, "WeatherAshfallHeavy", out _, TimeSpan.FromSeconds(30));
            }
            catch (Exception e)
            {
                plainError = e.Message;
            }

            // Case B: EnsurePlanet map (grid on map entity).
            mapSys.CreateMap(out var planetId, runMapInit: false);
            var planetUid = mapSys.GetMap(planetId);
            biome.EnsurePlanet(planetUid, protoMan.Index<BiomeTemplatePrototype>("Lavaland"));
            entMan.AddComponent(planetUid, new Content.Shared.Salvage.RestrictedRangeComponent
            {
                Range = 256f,
            });
            var loader = server.System<Robust.Shared.EntitySerialization.Systems.MapLoaderSystem>();
            loader.TryLoadGrid(planetId, new Robust.Shared.Utility.ResPath("/Maps/Lavaland/mining_outpost.yml"), out _);
            mapSys.InitializeMap(planetId);
            try
            {
                weather.TrySetWeather(planetId, "WeatherAshfallHeavy", out _, TimeSpan.FromSeconds(30));
            }
            catch (Exception e)
            {
                planetError = e.Message;
            }

            var effect = entMan.EntityQuery<WeatherStatusEffectComponent>(true).LastOrDefault();
            if (effect != null)
            {
                var xform = entMan.GetComponent<TransformComponent>(effect.Owner);
                planetState = $"parent={entMan.ToPrettyString(xform.ParentUid)} localPos={xform.LocalPosition} anchored={xform.Anchored}";
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(plainError, Is.Null, $"plain map weather failed: {plainError}");
            Assert.That(planetError, Is.Null, $"planet map weather failed: {planetError} | effect state: {planetState}");
        });
    }
}
