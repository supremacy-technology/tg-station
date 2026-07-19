using Content.Server.GameTicking.Events;
using Content.Server.Lavaland.Components;
using Content.Server.Parallax;
using Content.Server.Shuttles.Systems;
using Content.Server.Weather;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Light.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Salvage;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Lavaland.Systems;

/// <summary>
/// Generates the persistent Lavaland mining planet at round start and runs its ash storms.
/// </summary>
public sealed class LavalandPlanetSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly BiomeSystem _biome = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;
    [Dependency] private readonly MetaDataSystem _metaData = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ShuttleSystem _shuttle = default!;
    [Dependency] private readonly WeatherSystem _weather = default!;

    private static readonly ProtoId<BiomeTemplatePrototype> BiomeTemplate = "Lavaland";
    private static readonly EntProtoId StormWeather = "WeatherAshfallHeavy";
    private static readonly ProtoId<DamageTypePrototype> StormDamageType = "Heat";

    private static readonly string[] OreLayers =
    {
        "OreIron", "OreQuartz", "OreGold", "OreSilver", "OrePlasma", "OreUranium", "OreDiamond",
    };

    private static readonly string[] MobLayers =
    {
        "WatchersLavaland", "WatchersMagmawing", "GoliathLavaland",
    };

    private static readonly ResPath OutpostPath = new("/Maps/Lavaland/mining_outpost.yml");

    private static readonly ResPath[] RuinPool =
    {
        new("/Maps/Lavaland/bridge.yml"),
        new("/Maps/Lavaland/broken_cargo.yml"),
        new("/Maps/Lavaland/catwalk_crossroad.yml"),
        new("/Maps/Lavaland/cave_murder.yml"),
        new("/Maps/Lavaland/clown_room.yml"),
        new("/Maps/Lavaland/commieoutpost.yml"),
        new("/Maps/Lavaland/crashed_pod_trail.yml"),
        new("/Maps/Lavaland/crasheddropship.yml"),
        new("/Maps/Lavaland/crashedinstigator.yml"),
        new("/Maps/Lavaland/crashedsloop.yml"),
        new("/Maps/Lavaland/enclosure.yml"),
        new("/Maps/Lavaland/escape_pod_crash.yml"),
        new("/Maps/Lavaland/fleshlab.yml"),
        new("/Maps/Lavaland/front_desk.yml"),
        new("/Maps/Lavaland/generator_scrapyard.yml"),
        new("/Maps/Lavaland/hermit_base.yml"),
        new("/Maps/Lavaland/labour_camp.yml"),
        new("/Maps/Lavaland/lava_farm.yml"),
        new("/Maps/Lavaland/lava_lake_village.yml"),
        new("/Maps/Lavaland/lava_river.yml"),
        new("/Maps/Lavaland/miming_drill.yml"),
        new("/Maps/Lavaland/minefield.yml"),
        new("/Maps/Lavaland/miner_tomb.yml"),
        new("/Maps/Lavaland/mineshaft.yml"),
        new("/Maps/Lavaland/mug_factory.yml"),
        new("/Maps/Lavaland/penalcolony.yml"),
        new("/Maps/Lavaland/pizza_party.yml"),
        new("/Maps/Lavaland/ripley.yml"),
        new("/Maps/Lavaland/river_village.yml"),
        new("/Maps/Lavaland/roundel.yml"),
        new("/Maps/Lavaland/shinobi_graveyard.yml"),
        new("/Maps/Lavaland/solemn_lament.yml"),
        new("/Maps/Lavaland/temple.yml"),
        new("/Maps/Lavaland/Envy.yml"),
        new("/Maps/Lavaland/Gluttony.yml"),
        new("/Maps/Lavaland/Greed.yml"),
        new("/Maps/Lavaland/Pride.yml"),
        new("/Maps/Lavaland/Wrath.yml"),
        new("/Maps/Lavaland/sloth.yml"),
    };

    private const int RuinCount = 8;
    private const float RuinMinRadius = 90f;
    private const float RuinMaxRadius = 220f;
    private const float PlanetRange = 256f;

    private const float StormCooldownMinSeconds = 480f;
    private const float StormCooldownMaxSeconds = 900f;
    // First storm hits early so miners learn the danger before settling in.
    private const float FirstStormMinSeconds = 120f;
    private const float FirstStormMaxSeconds = 300f;
    private static readonly TimeSpan StormDuration = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan StormDamageInterval = TimeSpan.FromSeconds(2);

    private DamageSpecifier _stormDamage = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);

        _stormDamage = new DamageSpecifier(_protoManager.Index(StormDamageType), FixedPoint2.New(3));
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        if (!_cfg.GetCVar(CCVars.LavalandEnabled))
            return;

        GeneratePlanet();
    }

    public EntityUid GeneratePlanet()
    {
        var seed = _random.Next();
        _mapSystem.CreateMap(out var mapId, runMapInit: false);
        var mapUid = _mapSystem.GetMap(mapId);

        _metaData.SetEntityName(mapUid, Loc.GetString("lavaland-map-name"));
        _biome.EnsurePlanet(mapUid, _protoManager.Index(BiomeTemplate), seed, mapLight: Color.FromHex("#A34931"));

        AddComp(mapUid, new RestrictedRangeComponent
        {
            Range = PlanetRange,
        });

        var biome = Comp<BiomeComponent>(mapUid);

        foreach (var layer in OreLayers)
        {
            _biome.AddMarkerLayer(mapUid, biome, layer);
        }

        foreach (var layer in MobLayers)
        {
            _biome.AddMarkerLayer(mapUid, biome, layer);
        }

        // Miner base at the planet origin, doubles as the shuttle landing area.
        if (_loader.TryLoadGrid(mapId, OutpostPath, out var outpost))
            SnapToTileGrid(outpost.Value);
        else
            Log.Error($"Lavaland failed to load mining outpost grid {OutpostPath}");

        PlaceRuins(mapId);

        var lavaland = AddComp<LavalandMapComponent>(mapUid);
        lavaland.NextStormTime = _timing.CurTime + TimeSpan.FromSeconds(_random.NextFloat(FirstStormMinSeconds, FirstStormMaxSeconds));

        _mapSystem.InitializeMap(mapId);

        // Visible on every shuttle console, no disk needed — the "mining shuttle" is whatever you fly there.
        _shuttle.TryAddFTLDestination(mapId, true, false, false, out _);

        return mapUid;
    }

    private void PlaceRuins(MapId mapId)
    {
        var pool = new List<ResPath>(RuinPool);

        // ponytail: fixed angular slots + random radius, no overlap solver; revisit if ruins ever collide.
        for (var i = 0; i < RuinCount; i++)
        {
            var path = pool[_random.Next(pool.Count)];
            pool.Remove(path);

            var angle = MathHelper.TwoPi * i / RuinCount + _random.NextFloat(-0.15f, 0.15f);
            var radius = _random.NextFloat(RuinMinRadius, RuinMaxRadius);
            // Whole-tile offsets so ruin tiles line up with the planet's tile grid.
            var offset = new System.Numerics.Vector2(
                MathF.Round(MathF.Cos(angle) * radius),
                MathF.Round(MathF.Sin(angle) * radius));

            if (_loader.TryLoadGrid(mapId, path, out var ruin, offset: offset))
                SnapToTileGrid(ruin.Value);
            else
                Log.Error($"Lavaland failed to load ruin grid {path}");
        }
    }

    /// <summary>
    /// Saved grids carry fractional root positions from mapping; round the final
    /// position so their tiles line up with the planet's tile grid.
    /// </summary>
    private void SnapToTileGrid(Entity<MapGridComponent> grid)
    {
        var pos = Transform(grid).LocalPosition;
        _transform.SetLocalPosition(grid, new System.Numerics.Vector2(MathF.Round(pos.X), MathF.Round(pos.Y)));
    }

    private TimeSpan NextStormCooldown()
    {
        return TimeSpan.FromSeconds(_random.NextFloat(StormCooldownMinSeconds, StormCooldownMaxSeconds));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<LavalandMapComponent, MapComponent>();

        while (query.MoveNext(out var uid, out var lavaland, out var map))
        {
            if (now >= lavaland.NextStormTime)
            {
                lavaland.NextStormTime = now + NextStormCooldown();
                lavaland.StormEndTime = now + StormDuration;
                _weather.TrySetWeather(map.MapId, StormWeather, out _, StormDuration);
                Log.Info($"Ash storm started on {ToPrettyString(uid)}");
            }

            if (now < lavaland.StormEndTime && now >= lavaland.NextDamageTime)
            {
                lavaland.NextDamageTime = now + StormDamageInterval;
                DamageExposed(uid);
            }
        }
    }

    /// <summary>
    /// Ash storm burns every mob on the planet standing under open sky. Interiors
    /// (roofed planet tiles, ruin/shuttle floors with weather-proof tiles) are shelter.
    /// </summary>
    private void DamageExposed(EntityUid mapUid)
    {
        var mobs = EntityQueryEnumerator<MobStateComponent, TransformComponent>();

        while (mobs.MoveNext(out var mob, out _, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (xform.GridUid is { } gridUid && TryComp(gridUid, out MapGridComponent? grid))
            {
                var tile = _mapSystem.GetTileRef(gridUid, grid, xform.Coordinates);
                TryComp(gridUid, out RoofComponent? roof);

                if (!_weather.CanWeatherAffect((gridUid, grid, roof), tile))
                    continue;
            }

            _damageable.TryChangeDamage(mob, _stormDamage, interruptsDoAfters: false);
        }
    }
}
