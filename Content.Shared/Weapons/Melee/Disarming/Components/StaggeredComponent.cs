using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Melee.Disarming.Components;

// Standalone rather than routed through whatever status-effect framework you have - ports cleanly
// either way. Swap it for your real system later if you'd rather keep one pipeline.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StaggeredComponent : Component
{
    [DataField, AutoNetworkedField] public TimeSpan StaggeredUntil;

    /// <summary>
    /// While CurTime is under this, a shove on this already-staggered target refreshes the stagger
    /// instead of chaining into the side-kick finisher - mirrors SS13's no_side_kick status so you
    /// can't kick someone onto their side over and over.
    /// </summary>
    [DataField, AutoNetworkedField] public TimeSpan NoSideKickUntil;
}
