using Content.Shared.Inventory;
using Robust.Shared.Audio;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Clothing.ModSuit.Components;

/// <summary>
///     Lets a MODsuit control unit (worn on the back, like a backpack) deploy a set of separate
///     clothing parts - helmet, chestplate, gauntlets and boots - onto the wearer, one per inventory
///     slot, then "seal" for pressure protection. A port of the tgstation13 MODsuit deploy/seal flow.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(ModSuitDeploySystem))]
public sealed partial class ModSuitDeployComponent : Component
{
    /// <summary>
    ///     Maps an inventory slot ("head", "outerClothing", "gloves", "shoes") to the clothing part
    ///     prototype that gets deployed into it. Networked so the client seal radial can list the parts.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public Dictionary<string, EntProtoId> Parts = new();

    /// <summary>
    ///     Whether the suit is currently deployed and sealed (all parts equipped).
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool Sealed;

    /// <summary>
    ///     Whether the suit is powered on. Only meaningful while deployed; drives the sealed sprites and
    ///     (in future) module power.
    /// </summary>
    [ViewVariables, AutoNetworkedField]
    public bool Active;

    /// <summary>
    ///     Equipped-sprite prefix applied to every part and the control unit while the suit is active,
    ///     switching them from their unsealed look to their sealed look.
    /// </summary>
    [DataField]
    public string ActivePrefix = "sealed";

    /// <summary>
    ///     Delay between each part sealing/unsealing during the activation animation.
    /// </summary>
    [DataField]
    public TimeSpan StepDelay = TimeSpan.FromSeconds(0.45);

    /// <summary>
    ///     Sound played as each individual part seals or unseals.
    /// </summary>
    [DataField]
    public SoundSpecifier? StepSound = new SoundPathSpecifier("/Audio/Mecha/mechmove03.ogg");

    // --- Runtime sealing-animation state (server-side) ---

    /// <summary>Whether a seal/unseal animation is currently playing.</summary>
    [ViewVariables]
    public bool Sealing;

    /// <summary>The state the current animation is moving toward (true = sealing up).</summary>
    [ViewVariables]
    public bool SealingTarget;

    /// <summary>Index of the next part to seal in the animation.</summary>
    [ViewVariables]
    public int SealStep;

    /// <summary>When the next animation step should run.</summary>
    [ViewVariables]
    public TimeSpan NextStep;

    /// <summary>
    ///     The action used to seal/unseal (deploy/retract) the suit.
    /// </summary>
    [DataField]
    public EntProtoId Action = "ActionModSuitSeal";

    [DataField, AutoNetworkedField]
    public EntityUid? ActionEntity;

    /// <summary>
    ///     The action used to power the suit on/off once it is deployed.
    /// </summary>
    [DataField]
    public EntProtoId ActivateAction = "ActionModSuitActivate";

    [DataField, AutoNetworkedField]
    public EntityUid? ActivateActionEntity;

    /// <summary>
    ///     Slot flags the control unit must be worn in for the seal action to be granted (the back slot).
    /// </summary>
    [DataField]
    public SlotFlags RequiredFlags = SlotFlags.BACK;

    /// <summary>
    ///     Prefix for the per-part storage containers on the control unit (one per slot).
    /// </summary>
    [DataField]
    public string ContainerPrefix = "modsuit-part-";

    /// <summary>
    ///     Inventory slots whose existing clothing is tucked away (and restored on retract) so the
    ///     matching part deploys over whatever the wearer already has on, instead of being blocked.
    ///     Defaults to the gauntlet and boot slots.
    /// </summary>
    [DataField]
    public HashSet<string> OverwearSlots = new() { "gloves", "shoes" };

    /// <summary>
    ///     Prefix for the per-slot containers holding clothing stowed to deploy an overwear part over it.
    /// </summary>
    [DataField]
    public string StowPrefix = "modsuit-stow-";

    /// <summary>
    ///     Sound played when the suit deploys/seals.
    /// </summary>
    [DataField]
    public SoundSpecifier? SealSound = new SoundPathSpecifier("/Audio/Mecha/sound_mecha_hydraulic.ogg");

    /// <summary>
    ///     Sound played when the suit retracts/unseals.
    /// </summary>
    [DataField]
    public SoundSpecifier? UnsealSound = new SoundPathSpecifier("/Audio/Items/hiss.ogg");

    /// <summary>
    ///     Sound played when the suit powers on.
    /// </summary>
    [DataField]
    public SoundSpecifier? ActivateSound = new SoundPathSpecifier("/Audio/Machines/reclaimer_startup.ogg");

    /// <summary>
    ///     Sound played when the suit powers off.
    /// </summary>
    [DataField]
    public SoundSpecifier? DeactivateSound = new SoundPathSpecifier("/Audio/Machines/button.ogg");

    /// <summary>
    ///     Spawned part entities, keyed by inventory slot. Populated server-side on map init.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, EntityUid> PartUids = new();

    /// <summary>
    ///     The container each retracted part is stored in, keyed by inventory slot.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, ContainerSlot> PartContainers = new();

    /// <summary>
    ///     Containers holding the wearer's stowed clothing while an overwear part is deployed, by slot.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, ContainerSlot> StowContainers = new();
}
