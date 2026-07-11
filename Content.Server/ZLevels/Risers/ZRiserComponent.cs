/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

namespace Content.Server.ZLevels.Risers;

/// <summary>
/// Marker for entities whose <see cref="IZRiserNode"/>s bridge node groups (power nets,
/// pipenets) between z-levels. Used by <see cref="ZRiserSystem"/> to reflood the node
/// graph when z-network membership changes and to show link status on examine.
/// </summary>
[RegisterComponent]
public sealed partial class ZRiserComponent : Component;
