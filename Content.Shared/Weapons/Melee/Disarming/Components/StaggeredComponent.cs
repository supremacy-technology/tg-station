using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Melee.Disarming.Components;

// Standalone rather than routed through whatever status-effect framework you have - ports cleanly
// either way. Swap it for your real system later if you'd rather keep one pipeline.
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StaggeredComponent : Component
{
    [DataField, AutoNetworkedField] public TimeSpan StaggeredUntil;
}
