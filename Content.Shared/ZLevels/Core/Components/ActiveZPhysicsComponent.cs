/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Robust.Shared.GameStates;

namespace Content.Shared.ZLevels.Core.Components;

/// <summary>
/// A marker that indicates entities that can actively move between z-levels.
/// </summary>
[RegisterComponent, NetworkedComponent, UnsavedComponent]
public sealed partial class ActiveZPhysicsComponent : Component;
