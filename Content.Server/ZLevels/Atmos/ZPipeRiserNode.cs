/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Content.Server.NodeContainer.Nodes;
using Content.Server.ZLevels.Risers;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;

namespace Content.Server.ZLevels.Atmos;

/// <summary>
/// Bridges pipenets between z-levels, like /tg/station's multi-z pipe
/// (code/modules/atmospherics/machinery/pipes/multiz.dm). Connects like a normal pipe on
/// its own level (inherited <see cref="PipeNode"/> behaviour) and to matching risers on
/// the same tile of the maps directly above and below. Riser-to-riser connections are
/// symmetric, so the node group flood merges the pipenets without extra bookkeeping.
/// </summary>
[DataDefinition]
public sealed partial class ZPipeRiserNode : PipeNode, IZRiserNode
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

        if (grid is null)
            yield break;

        foreach (var riser in ZRiserHelper.GetVerticalNodes<ZPipeRiserNode>(xform, nodeQuery, entMan))
        {
            if (riser != this && riser.NodeGroupID == NodeGroupID && riser.CurrentPipeLayer == CurrentPipeLayer)
                yield return riser;
        }
    }
}
