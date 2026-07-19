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
        var tendrils = 0;
        var drakes = 0;
        await server.WaitPost(() =>
        {
            foreach (var (meta, xform) in entMan.EntityQuery<MetaDataComponent, TransformComponent>(true))
            {
                if (xform.MapUid != planet)
                    continue;

                switch (meta.EntityPrototype?.ID)
                {
                    case "StructureTendril":
                        tendrils++;
                        break;
                    case "MobAshDrake":
                        drakes++;
                        break;
                }
            }
        });

        Assert.Multiple(() =>
        {
            Assert.That(childGrids, Is.EqualTo(10), $"Expected outpost + 8 ruins + necropolis, got {childGrids} grids");
            Assert.That(misaligned, Is.Empty, $"Grids not tile-aligned at: {misaligned}");
            Assert.That(tendrils, Is.EqualTo(10), $"Expected 6 scattered + 4 necropolis tendrils, got {tendrils}");
            Assert.That(drakes, Is.EqualTo(1), $"Expected one ash drake, got {drakes}");
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

        // Storm damage killing a mob mid-tick must not invalidate the damage enumeration
        // (legion death spawns skulls + deletes the corpse).
        var legion = EntityUid.Invalid;
        await server.WaitPost(() =>
        {
            legion = entMan.SpawnEntity("MobLegion",
                new Robust.Shared.Map.EntityCoordinates(planet, new System.Numerics.Vector2(50f, 50f)));

            var damageable = server.System<Content.Shared.Damage.Systems.DamageableSystem>();
            var protoMan = server.ResolveDependency<Robust.Shared.Prototypes.IPrototypeManager>();
            var damage = new Content.Shared.Damage.DamageSpecifier(
                protoMan.Index<Content.Shared.Damage.Prototypes.DamageTypePrototype>("Heat"), 74);
            damageable.TryChangeDamage(legion, damage, ignoreResistances: true);

            var comp = entMan.GetComponent<LavalandMapComponent>(planet);
            comp.StormEndTime = TimeSpan.FromHours(1);
            comp.NextDamageTime = TimeSpan.Zero;
        });

        await server.WaitRunTicks(10);
        Assert.That(entMan.Deleted(legion), "Legion should have died to the storm tick and shattered");
    }
}
