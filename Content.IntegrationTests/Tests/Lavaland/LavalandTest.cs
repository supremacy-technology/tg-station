using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Lavaland.Components;
using Content.Server.Lavaland.Systems;
using Content.Shared.Weather;
using Robust.Shared.GameObjects;
using Robust.Shared.Map.Components;

namespace Content.IntegrationTests.Tests.Lavaland;

[TestFixture, TestOf(typeof(LavalandPlanetSystem))]
public sealed class LavalandTest : GameTest
{
    /// <summary>
    /// Generates the Lavaland planet, checks the outpost/ruin grids loaded,
    /// then forces an ash storm and checks the weather actually starts.
    /// </summary>
    [Test]
    public async Task GeneratePlanetAndStorm()
    {
        var server = Pair.Server;
        var entMan = server.EntMan;
        var sys = server.System<LavalandPlanetSystem>();

        var planet = EntityUid.Invalid;
        await server.WaitPost(() => planet = sys.GeneratePlanet());
        await server.WaitRunTicks(5);

        Assert.Multiple(() =>
        {
            Assert.That(entMan.HasComponent<LavalandMapComponent>(planet), "No LavalandMapComponent on planet map");
            Assert.That(entMan.HasComponent<MapGridComponent>(planet), "Planet map has no biome grid");
        });

        // Mining outpost + 8 ruins parented to the planet map.
        var childGrids = 0;
        var misaligned = "";
        await server.WaitPost(() =>
        {
            foreach (var (_, xform) in entMan.EntityQuery<MapGridComponent, TransformComponent>(true))
            {
                if (xform.MapUid != planet || xform.GridUid == planet)
                    continue;

                childGrids++;
                var pos = xform.LocalPosition;
                if (pos.X != MathF.Round(pos.X) || pos.Y != MathF.Round(pos.Y))
                    misaligned += $"{pos} ";
            }
        });
        Assert.Multiple(() =>
        {
            Assert.That(childGrids, Is.EqualTo(9), $"Expected outpost + 8 ruins, got {childGrids} grids");
            Assert.That(misaligned, Is.Empty, $"Grids not tile-aligned at: {misaligned}");
        });

        // Force the storm scheduler to fire now.
        await server.WaitPost(() =>
        {
            var comp = entMan.GetComponent<LavalandMapComponent>(planet);
            comp.NextStormTime = TimeSpan.Zero;
        });
        await server.WaitRunTicks(10);

        var stormActive = false;
        await server.WaitPost(() =>
        {
            stormActive = entMan.EntityQuery<WeatherStatusEffectComponent>(true).Any();
        });
        Assert.That(stormActive, "Ash storm weather never started after NextStormTime elapsed");
    }
}
