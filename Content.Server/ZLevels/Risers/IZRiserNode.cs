/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

namespace Content.Server.ZLevels.Risers;

/// <summary>
/// Marker for nodes that bridge node groups between z-levels (power/pipe risers).
/// <see cref="ZRiserSystem"/> refloods these when z-network membership changes.
/// </summary>
public interface IZRiserNode;
