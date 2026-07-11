/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server.Gravity;
using Content.Server.ZLevels.Core;
using Content.Shared.Gravity;
using Content.Shared.ZLevels.Core.Components;
using Content.Shared.ZLevels.Core.EntitySystems;
using Robust.Shared.Map.Components;

namespace Content.Server.ZLevels.Gravity;

/// <summary>
/// Shares gravity across a z-level network: an active gravity generator anywhere in
/// the stack provides gravity to every map in the network and every grid on those maps.
/// The actual generator lookup lives in <see cref="GravitySystem.RefreshGravity"/>;
/// this system only makes sure the other levels re-evaluate when one level changes.
/// </summary>
public sealed class ZGravitySystem : EntitySystem
{
    [Dependency] private readonly GravitySystem _gravity = default!;
    [Dependency] private readonly SharedZLevelsSystem _zLevels = default!;

    private EntityQuery<GravityComponent> _gravityQuery;

    private bool _propagating;

    public override void Initialize()
    {
        base.Initialize();

        _gravityQuery = GetEntityQuery<GravityComponent>();

        SubscribeLocalEvent<GravityChangedEvent>(OnGravityChanged);
        // Broadcast subscription: the directed <ZLevelsNetworkComponent, ZLevelNetworkUpdatedEvent>
        // pair is already claimed by RoofSystem, and RobustToolbox allows only one directed
        // subscription per (component, event) pair across all systems.
        SubscribeLocalEvent<ZLevelNetworkUpdatedEvent>(OnNetworkUpdated);
    }

    /// <summary>
    /// Levels linked into (or merged between) networks inherit gravity from
    /// generators elsewhere in the network.
    /// </summary>
    private void OnNetworkUpdated(ZLevelNetworkUpdatedEvent args)
    {
        RefreshNetwork(CollectNetworkMaps(args.Network), exclude: null);
    }

    private void OnGravityChanged(ref GravityChangedEvent args)
    {
        // RefreshNetwork re-raises GravityChangedEvent for every level that flips;
        // the first pass already covers the whole network, so nested events are no-ops.
        if (_propagating)
            return;

        if (Transform(args.ChangedGridIndex).MapUid is not { } mapUid ||
            !_zLevels.TryZNetwork(mapUid, out var network))
            return;

        RefreshNetwork(CollectNetworkMaps(network.Value), args.ChangedGridIndex);
    }

    private static HashSet<EntityUid> CollectNetworkMaps(Entity<ZLevelsNetworkComponent> network)
    {
        var maps = new HashSet<EntityUid>();
        foreach (var (_, netMap) in network.Comp.ZLevels)
        {
            if (netMap != null)
                maps.Add(netMap.Value);
        }

        return maps;
    }

    /// <summary>
    /// Recomputes gravity for every map in the network and every grid on those maps.
    /// </summary>
    private void RefreshNetwork(HashSet<EntityUid> networkMaps, EntityUid? exclude)
    {
        if (_propagating)
            return;

        _propagating = true;
        try
        {
            foreach (var map in networkMaps)
            {
                if (map != exclude && _gravityQuery.HasComp(map))
                    _gravity.RefreshGravity(map);
            }

            var grids = AllEntityQuery<MapGridComponent, TransformComponent>();
            while (grids.MoveNext(out var gridUid, out _, out var gridXform))
            {
                if (gridUid == exclude)
                    continue;

                // Map-grids were already refreshed as maps above.
                if (gridXform.MapUid is not { } gridMap || gridMap == gridUid || !networkMaps.Contains(gridMap))
                    continue;

                if (_gravityQuery.HasComp(gridUid))
                    _gravity.RefreshGravity(gridUid);
            }
        }
        finally
        {
            _propagating = false;
        }
    }
}
