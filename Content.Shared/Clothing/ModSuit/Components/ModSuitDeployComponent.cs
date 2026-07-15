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
    ///     How far down the control unit's synth chimes are pitched. SS13 plays them at a frequency of
    ///     6000 against 44.1kHz sources - 0.136x speed - stretching the 0.36s beep into a ~2.6s chime.
    ///
    ///     A sound this slow outlives the audio entity playing it, since that's despawned after the
    ///     length of the file on disk with no regard for pitch; ModSuitDeploySystem stretches the
    ///     lifetime back out to compensate.
    /// </summary>
    private const float SynthPitch = 6000f / 44100f;

    /// <summary>
    ///     Spread of the random pitch on <see cref="StepSound"/>. SS13 picks a playback frequency
    ///     uniformly from 32-55kHz against a 44.1kHz file, so 0.726x-1.247x. SS14 instead draws from a
    ///     normal distribution, so this is that uniform range's standard deviation - (b-a)/sqrt(12) -
    ///     giving the same spread rather than the same hard bounds.
    /// </summary>
    private const float StepPitchDeviation = 0.15f;

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
    ///     Delay between each part sealing/unsealing during the activation animation. Matches SS13's
    ///     MOD_ACTIVATION_STEP_TIME, the base every normal suit uses; over there only a few themes
    ///     override it (the infiltrator halves it, admin/debug suits are near-instant).
    /// </summary>
    [DataField]
    public TimeSpan StepDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Sound played whenever a single part moves on its own - deploying, retracting, sealing or
    ///     unsealing. SS13 uses mechmove03 for all four, randomising the pitch on every play.
    /// </summary>
    [DataField]
    public SoundSpecifier? StepSound = new SoundPathSpecifier("/Audio/Mecha/mechmove03.ogg")
    {
        Params = AudioParams.Default.WithVariation(StepPitchDeviation),
    };

    // --- Runtime sealing-animation state (server-side) ---

    /// <summary>Whether a seal/unseal animation is currently playing.</summary>
    [ViewVariables]
    public bool Sealing;

    /// <summary>The state the current animation is moving toward (true = sealing up).</summary>
    [ViewVariables]
    public bool SealingTarget;

    /// <summary>Index of the next entry of <see cref="SealSequence"/> to seal in the animation.</summary>
    [ViewVariables]
    public int SealStep;

    /// <summary>
    ///     What the current animation steps through, one entry per <see cref="StepDelay"/>: every
    ///     deployed part, plus the control unit itself. Retracted parts are left out, so they don't
    ///     chime from inside the unit. The control unit goes last when powering up and first when
    ///     powering down, matching SS13's order. Rebuilt on each <see cref="ToggleSeal"/>.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> SealSequence = new();

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
    ///     Sound played by the control unit as it boots, on the last step of the power-on sequence.
    ///     SS13 plays this pitched heavily down (its playsound frequency of 6000 against a 44.1kHz
    ///     file), turning a short beep into a drawn-out chime.
    /// </summary>
    [DataField]
    public SoundSpecifier? ActivateSound = new SoundPathSpecifier("/Audio/Machines/synth_yes.ogg")
    {
        Params = AudioParams.Default.WithPitchScale(SynthPitch),
    };

    /// <summary>
    ///     Sound played by the control unit as it goes offline, on the first step of the power-off
    ///     sequence. Pitched down to match <see cref="ActivateSound"/>.
    /// </summary>
    [DataField]
    public SoundSpecifier? DeactivateSound = new SoundPathSpecifier("/Audio/Machines/synth_no.ogg")
    {
        Params = AudioParams.Default.WithPitchScale(SynthPitch),
    };

    /// <summary>
    ///     Boot jingle played to the wearer alone once the suit finishes powering on, over the top of
    ///     <see cref="ActivateSound"/>. Plays at its natural pitch, as in SS13.
    /// </summary>
    [DataField]
    public SoundSpecifier? NominalSound = new SoundPathSpecifier("/Audio/Mecha/nominal.ogg");

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
