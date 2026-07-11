/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server.Power.Nodes;
using Content.Shared.NodeContainer;
using Content.Shared.ZLevels.Core.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.ZLevels.Power;

/// <summary>
/// Bridges cable networks between z-levels, like /tg/station's multi-z cable hub.
/// Connects to cables on its own tile (inherited <see cref="CableDeviceNode"/> behaviour,
/// which cables also see back) and to matching risers on the same tile of the maps
/// directly above and below. Riser-to-riser connections are symmetric, so the node
/// group flood merges the power nets without extra bookkeeping.
/// </summary>
[DataDefinition]
public sealed partial class ZCableRiserNode : CableDeviceNode
{
    public override IEnumerable<Node> GetReachableNodes(
        Entity<TransformComponent> xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        EntityQuery<TransformComponent> xformQuery,
        Entity<MapGridComponent>? grid,
        IEntityManager entMan)
    {
        foreach (var node in base.GetReachableNodes(xform, nodeQuery, xformQuery, grid, entMan))
        {
            yield return node;
        }

        if (!xform.Comp.Anchored || grid is null || xform.Comp.MapUid is not { } mapUid)
            yield break;

        var mapSystem = entMan.System<SharedMapSystem>();
        var transformSystem = entMan.System<SharedTransformSystem>();
        var zLevels = entMan.System<SharedZLevelsSystem>();

        // Project our world position onto the other map-grids: z-level maps sit at the
        // world origin, so world coordinates are local coordinates there.
        var worldPos = transformSystem.GetWorldPosition(xform.Comp);

        for (var offset = -1; offset <= 1; offset += 2)
        {
            if (!zLevels.TryMapOffset(mapUid, offset, out var otherMap))
                continue;

            if (!entMan.TryGetComponent<MapGridComponent>(otherMap.Value.Owner, out var otherGrid))
                continue;

            var gridIndex = mapSystem.TileIndicesFor((otherMap.Value.Owner, otherGrid),
                new EntityCoordinates(otherMap.Value.Owner, worldPos));

            var anchored = mapSystem.GetAnchoredEntitiesEnumerator(otherMap.Value.Owner, otherGrid, gridIndex);
            while (anchored.MoveNext(out var otherUid))
            {
                if (!nodeQuery.TryGetComponent(otherUid.Value, out var container))
                    continue;

                foreach (var node in container.Nodes.Values)
                {
                    if (node is ZCableRiserNode riser && riser != this && riser.NodeGroupID == NodeGroupID)
                        yield return riser;
                }
            }
        }
    }
}
