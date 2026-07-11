/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Shared.NodeContainer;
using Content.Shared.ZLevels.Core.EntitySystems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.ZLevels.Risers;

public static class ZRiserHelper
{
    /// <summary>
    /// Yields nodes of type <typeparamref name="T"/> anchored at the same world tile on the
    /// maps directly above and below. Callers filter for group/layer compatibility.
    /// </summary>
    public static IEnumerable<T> GetVerticalNodes<T>(
        Entity<TransformComponent> xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        IEntityManager entMan) where T : Node
    {
        if (!xform.Comp.Anchored || xform.Comp.MapUid is not { } mapUid)
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
                    if (node is T typed)
                        yield return typed;
                }
            }
        }
    }
}
