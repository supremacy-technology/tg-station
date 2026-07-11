/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server.NodeContainer.EntitySystems;
using Content.Server.ZLevels.Core;
using Content.Shared.Examine;
using Content.Shared.NodeContainer;
using Content.Shared.ZLevels.Core.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.ZLevels.Risers;

/// <summary>
/// Support system for <see cref="IZRiserNode"/>s (power and pipe risers): refloods riser
/// nodes when z-network membership changes (nodes group before maps get linked, and the
/// node graph has no idea the linkage happened), and reports vertical link status on examine.
/// </summary>
public sealed class ZRiserSystem : EntitySystem
{
    [Dependency] private readonly NodeGroupSystem _nodeGroup = default!;
    [Dependency] private readonly SharedZLevelsSystem _zLevels = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private EntityQuery<ZRiserComponent> _riserQuery;
    private EntityQuery<MapGridComponent> _gridQuery;

    public override void Initialize()
    {
        base.Initialize();

        _riserQuery = GetEntityQuery<ZRiserComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();

        SubscribeLocalEvent<ZLevelNetworkUpdatedEvent>(OnNetworkUpdated);
        SubscribeLocalEvent<ZRiserComponent, ExaminedEvent>(OnExamined);
    }

    private void OnNetworkUpdated(ZLevelNetworkUpdatedEvent args)
    {
        var networkMaps = new HashSet<EntityUid>();
        foreach (var (_, netMap) in args.Network.Comp.ZLevels)
        {
            if (netMap != null)
                networkMaps.Add(netMap.Value);
        }

        var query = EntityQueryEnumerator<ZRiserComponent, NodeContainerComponent, TransformComponent>();
        while (query.MoveNext(out _, out _, out var container, out var xform))
        {
            if (xform.MapUid is not { } mapUid || !networkMaps.Contains(mapUid))
                continue;

            foreach (var node in container.Nodes.Values)
            {
                if (node is IZRiserNode)
                    _nodeGroup.QueueReflood(node);
            }
        }
    }

    private void OnExamined(Entity<ZRiserComponent> ent, ref ExaminedEvent args)
    {
        var above = HasRiserOnOffsetMap(ent, 1);
        var below = HasRiserOnOffsetMap(ent, -1);

        args.PushMarkup(Loc.GetString(above
            ? "zlevel-riser-linked-above"
            : "zlevel-riser-unlinked-above"));
        args.PushMarkup(Loc.GetString(below
            ? "zlevel-riser-linked-below"
            : "zlevel-riser-unlinked-below"));
    }

    private bool HasRiserOnOffsetMap(EntityUid riser, int offset)
    {
        var xform = Transform(riser);

        if (!xform.Anchored || xform.MapUid is not { } mapUid)
            return false;

        if (!_zLevels.TryMapOffset(mapUid, offset, out var otherMap))
            return false;

        if (!_gridQuery.TryComp(otherMap.Value.Owner, out var otherGrid))
            return false;

        // Project our world position onto the other map-grid (z-level maps sit at the world origin).
        var worldPos = _transform.GetWorldPosition(xform);
        var gridIndex = _map.TileIndicesFor((otherMap.Value.Owner, otherGrid),
            new EntityCoordinates(otherMap.Value.Owner, worldPos));

        var anchored = _map.GetAnchoredEntitiesEnumerator(otherMap.Value.Owner, otherGrid, gridIndex);
        while (anchored.MoveNext(out var otherUid))
        {
            if (_riserQuery.HasComp(otherUid.Value))
                return true;
        }

        return false;
    }
}
