/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

namespace Content.Server.ZLevels.Power;

/// <summary>
/// Marker for entities whose <see cref="ZCableRiserNode"/>s bridge power nets between
/// z-levels. Used by <see cref="ZCableRiserSystem"/> to reflood the node graph when
/// z-network membership changes and to show link status on examine.
/// </summary>
[RegisterComponent]
public sealed partial class ZCableRiserComponent : Component;
