using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Melee.Disarming.Components;

/// <summary>
/// A short "staggered" state from being shoved - applies a small movement slowdown until
/// <see cref="StaggeredUntil"/>. Managed by <see cref="Systems.DisarmingSystem"/>, which expires it and
/// refreshes movement speed. Shoving an already-staggered target knocks a ranged weapon out of hand.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class StaggeredComponent : Component
{
    [DataField, AutoNetworkedField] public TimeSpan StaggeredUntil;
}
