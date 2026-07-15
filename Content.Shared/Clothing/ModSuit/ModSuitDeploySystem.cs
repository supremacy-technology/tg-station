using Content.Shared.Actions;
using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Clothing.ModSuit.Components;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.PowerCell;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.Shared.Clothing.ModSuit;

/// <summary>
///     Handles deploying/retracting and sealing a <see cref="ModSuitDeployComponent"/> control unit.
///     Deploying equips the suit's separate parts (helmet, chestplate, gauntlets, boots) into their own
///     inventory slots; because those parts carry pressure protection, a fully deployed suit keeps the
///     wearer safe from pressure - just like the tgstation13 MODsuit.
///
///     The seal action opens a radial menu that toggles individual parts, and a right-click verb seals
///     or unseals every part at once.
/// </summary>
public sealed class ModSuitDeploySystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ClothingSystem _clothing = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly PowerCellSystem _cell = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSuitDeployComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ModSuitDeployComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ModSuitDeployComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ModSuitDeployComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<ModSuitDeployComponent, ModSuitSealEvent>(OnSealAction);
        SubscribeLocalEvent<ModSuitDeployComponent, ModSuitActivateEvent>(OnActivateAction);
        SubscribeLocalEvent<ModSuitDeployComponent, ModSuitSealSlotMessage>(OnSealMessage);
        SubscribeLocalEvent<ModSuitDeployComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<ModSuitDeployComponent, GotUnequippedEvent>(OnControlUnitUnequipped);
        SubscribeLocalEvent<ModSuitDeployComponent, BeingUnequippedAttemptEvent>(OnUnequipAttempt);
        SubscribeLocalEvent<ModSuitDeployComponent, ModSuitPowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<ModSuitDeployComponent, PowerCellSlotEmptyEvent>(OnCellEmpty);

        SubscribeLocalEvent<ModSuitPartComponent, GotUnequippedEvent>(OnPartUnequipped);
    }

    private void OnInit(Entity<ModSuitDeployComponent> ent, ref ComponentInit args)
    {
        foreach (var slot in ent.Comp.Parts.Keys)
        {
            ent.Comp.PartContainers[slot] =
                _container.EnsureContainer<ContainerSlot>(ent, ent.Comp.ContainerPrefix + slot);
        }

        foreach (var slot in ent.Comp.OverwearSlots)
        {
            ent.Comp.StowContainers[slot] =
                _container.EnsureContainer<ContainerSlot>(ent, ent.Comp.StowPrefix + slot);
        }
    }

    private void OnMapInit(Entity<ModSuitDeployComponent> ent, ref MapInitEvent args)
    {
        _actionContainer.EnsureAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _actionContainer.EnsureAction(ent, ref ent.Comp.ActivateActionEntity, ent.Comp.ActivateAction);
        Dirty(ent);

        foreach (var (slot, proto) in ent.Comp.Parts)
        {
            if (!ent.Comp.PartContainers.TryGetValue(slot, out var container))
                continue;

            if (container.ContainedEntity is { } existing)
            {
                RegisterPart(ent, slot, existing);
                continue;
            }

            var part = Spawn(proto, Transform(ent).Coordinates);
            RegisterPart(ent, slot, part);
            _container.Insert(part, container);
        }
    }

    private void RegisterPart(Entity<ModSuitDeployComponent> ent, string slot, EntityUid part)
    {
        var comp = EnsureComp<ModSuitPartComponent>(part);
        comp.Master = ent;
        comp.Slot = slot;
        Dirty(part, comp);
        ent.Comp.PartUids[slot] = part;
    }

    private void OnShutdown(Entity<ModSuitDeployComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Comp.ActionEntity);
        _actions.RemoveAction(ent.Comp.ActivateActionEntity);
        foreach (var part in ent.Comp.PartUids.Values)
        {
            PredictedQueueDel(part);
        }

        // Stowed items are the wearer's own clothing - dump them out rather than delete them with us.
        foreach (var stow in ent.Comp.StowContainers.Values)
        {
            if (stow.ContainedEntity is { } item)
                _container.Remove(item, stow);
        }

    }

    private void OnGetActions(Entity<ModSuitDeployComponent> ent, ref GetItemActionsEvent args)
    {
        if ((args.SlotFlags & ent.Comp.RequiredFlags) != ent.Comp.RequiredFlags)
            return;

        if (ent.Comp.ActionEntity != null)
            args.AddAction(ent.Comp.ActionEntity.Value);
        if (ent.Comp.ActivateActionEntity != null)
            args.AddAction(ent.Comp.ActivateActionEntity.Value);
    }

    // Left-clicking the action button opens the per-part radial menu.
    private void OnSealAction(Entity<ModSuitDeployComponent> ent, ref ModSuitSealEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _ui.OpenUi(ent.Owner, ModSuitUiKey.Seal, args.Performer);
    }

    // The activate action seals/unseals the deployed suit as a part-by-part animation.
    private void OnActivateAction(Entity<ModSuitDeployComponent> ent, ref ModSuitActivateEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        ToggleSeal(ent, args.Performer);
    }

    // Can't take the control unit off while any part is deployed or the suit is powered on.
    private void OnUnequipAttempt(Entity<ModSuitDeployComponent> ent, ref BeingUnequippedAttemptEvent args)
    {
        if (!ent.Comp.Active && !AnyDeployed(ent, args.UnEquipTarget))
            return;

        _popup.PopupClient(Loc.GetString("modsuit-cant-remove-deployed", ("suit", ent.Owner)), ent, args.User);
        args.Cancel();
    }

    /// <summary>
    ///     Starts the seal (power-on) or unseal (power-off) animation. The animation itself is driven
    ///     server-side by <see cref="Update"/>, sealing one part per <see cref="ModSuitDeployComponent.StepDelay"/>.
    /// </summary>
    public void ToggleSeal(Entity<ModSuitDeployComponent> ent, EntityUid wearer)
    {
        // Server-authoritative: the animation's sprite/power changes network to the client.
        if (_net.IsClient || ent.Comp.Sealing)
            return;

        // The suit can be powered on with any (or no) parts deployed; it just won't be airtight against
        // space unless every sealing part is out. Pressure protection comes from the deployed parts.
        var target = !ent.Comp.Active;

        // Powering on needs a cell with charge in it (SS13: "no power source!"). HasDrawCharge popups
        // the reason to the wearer itself. Powering off is always allowed - never strand someone sealed.
        if (target && !_cell.HasDrawCharge(ent.Owner, user: wearer))
            return;

        BuildSealSequence(ent, wearer, target);

        ent.Comp.Sealing = true;
        ent.Comp.SealingTarget = target;
        ent.Comp.SealStep = 0;
        ent.Comp.NextStep = _timing.CurTime;

        // Powering down turns modules off up front, then unseals part by part.
        if (!target)
        {
            ent.Comp.Active = false;
            Dirty(ent);
            var ev = new ModSuitPowerChangedEvent(false);
            RaiseLocalEvent(ent, ref ev);
        }
    }

    /// <summary>
    ///     Works out what the seal animation steps through, in SS13's order: powering down takes the
    ///     control unit offline first and then unseals each deployed part, while powering up seals the
    ///     parts first and boots the control unit last. Parts still retracted inside the unit are
    ///     skipped entirely rather than sealed in place.
    /// </summary>
    private void BuildSealSequence(Entity<ModSuitDeployComponent> ent, EntityUid wearer, bool target)
    {
        var sequence = ent.Comp.SealSequence;
        sequence.Clear();

        if (!target)
            sequence.Add(ent.Owner);

        foreach (var slot in ent.Comp.Parts.Keys)
        {
            if (IsDeployed(ent, wearer, slot) && ent.Comp.PartUids.TryGetValue(slot, out var part))
                sequence.Add(part);
        }

        if (target)
            sequence.Add(ent.Owner);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_net.IsClient)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<ModSuitDeployComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.Sealing && now >= comp.NextStep)
                StepSeal((uid, comp), now);
        }
    }

    // Seals/unseals the next entry of the sequence, one per StepDelay, so the suit closes up around
    // the wearer piece by piece instead of all at once.
    private void StepSeal(Entity<ModSuitDeployComponent> ent, TimeSpan now)
    {
        var uid = ent.Comp.SealSequence[ent.Comp.SealStep];

        // A part can be destroyed mid-animation; just skip its step rather than sealing a corpse.
        if (!TerminatingOrDeleted(uid))
        {
            SetSealed(ent, uid, ent.Comp.SealingTarget);

            // The control unit gets the power on/off chime, each part gets the mechanical step sound.
            var sound = uid == ent.Owner
                ? (ent.Comp.SealingTarget ? ent.Comp.ActivateSound : ent.Comp.DeactivateSound)
                : ent.Comp.StepSound;
            KeepAlivePitched(_audio.PlayPvs(sound, uid));
        }

        ent.Comp.SealStep++;
        ent.Comp.NextStep = now + ent.Comp.StepDelay;

        if (ent.Comp.SealStep < ent.Comp.SealSequence.Count)
            return;

        // Animation finished.
        ent.Comp.Sealing = false;

        var wearer = Transform(ent).ParentUid;

        string message;
        if (!ent.Comp.SealingTarget)
        {
            message = "modsuit-deactivated";
        }
        else
        {
            ent.Comp.Active = true;
            Dirty(ent);
            var ev = new ModSuitPowerChangedEvent(true);
            RaiseLocalEvent(ent, ref ev);

            // The boot jingle is for the wearer's ears only (SS13 plays it via playsound_local), on
            // top of the control unit's chime everyone else hears. Played global rather than off the
            // control unit: it's a noise inside your own helmet, so it wants no positioning, and
            // hanging it off the unit parents the audio inside the back slot's container.
            _audio.PlayGlobal(ent.Comp.NominalSound, wearer);

            // Fully sealed = airtight; otherwise it powers on but doesn't seal against space.
            message = AllDeployed(ent, wearer) ? "modsuit-activated" : "modsuit-activated-partial";
        }

        _popup.PopupEntity(Loc.GetString(message, ("suit", ent.Owner)), ent, wearer);
    }

    // The suit only draws from its cell while it's actually running. Every power transition routes
    // through ModSuitPowerChangedEvent, so this one handler keeps the draw in sync with Active.
    private void OnPowerChanged(Entity<ModSuitDeployComponent> ent, ref ModSuitPowerChangedEvent args)
    {
        _cell.SetDrawEnabled(ent.Owner, args.Active);
    }

    // Cell ran dry, or was pulled out, while running (SS13: power_off()). Parts stay deployed, the
    // suit just dies. Server-authoritative like the rest of the power flow - Active networks down.
    private void OnCellEmpty(Entity<ModSuitDeployComponent> ent, ref PowerCellSlotEmptyEvent args)
    {
        if (_net.IsClient || !ent.Comp.Active)
            return;

        // DeactivateInstant raises ModSuitPowerChangedEvent(false), which switches modules off and
        // disables the draw above.
        DeactivateInstant(ent);
        _popup.PopupEntity(Loc.GetString("modsuit-power-empty", ("suit", ent.Owner)), ent, Transform(ent).ParentUid);
    }

    /// <summary>
    ///     Keeps a slowed-down sound alive long enough to finish.
    ///
    ///     Audio is despawned after the length of the file on disk, worked out before any pitch is
    ///     applied and never divided by it (see SharedAudioSystem.SetupAudio). Anything pitched below
    ///     1x therefore plays for longer than its own entity lives and gets cut off mid-note - our
    ///     synth chimes run at about a seventh speed, so they'd lose half the chime. Rebuild the
    ///     lifetime from how long the sound actually takes.
    /// </summary>
    private void KeepAlivePitched((EntityUid Entity, AudioComponent Component)? audio)
    {
        if (audio is not { } played)
            return;

        var pitch = played.Component.Params.Pitch;
        if (pitch <= 0f || pitch >= 1f)
            return;

        if (!TryComp<TimedDespawnComponent>(played.Entity, out var despawn))
            return;

        var fileLength = despawn.Lifetime - SharedAudioSystem.AudioDespawnBuffer;
        despawn.Lifetime = fileLength / pitch + SharedAudioSystem.AudioDespawnBuffer;
    }

    /// <summary>
    ///     Opens or closes a single part (or the control unit): swaps its worn sprite, and flags it so
    ///     the protection it only carries while shut can be applied or stripped. The one place seal
    ///     state changes, so the sprite and the protection can never disagree.
    /// </summary>
    private void SetSealed(Entity<ModSuitDeployComponent> ent, EntityUid uid, bool isSealed)
    {
        _clothing.SetEquippedPrefix(uid, isSealed ? ent.Comp.ActivePrefix : null);

        // The control unit is in the sequence too, but it's a backpack - it has no sealed state.
        if (!TryComp<ModSuitPartComponent>(uid, out var part) || part.Sealed == isSealed)
            return;

        part.Sealed = isSealed;
        Dirty(uid, part);

        // The wearer comes off the control unit rather than the part: the unit stays on their back,
        // while a part that's being retracted has already left the body by now.
        var ev = new ModSuitPartSealedEvent(isSealed, Transform(ent).ParentUid);
        RaiseLocalEvent(uid, ref ev);
    }

    // Instantly powers the suit down and clears its sealed sprites (used when retracting parts).
    private void DeactivateInstant(Entity<ModSuitDeployComponent> ent)
    {
        ent.Comp.Sealing = false;
        var wasActive = ent.Comp.Active;
        ent.Comp.Active = false;

        foreach (var part in ent.Comp.PartUids.Values)
            SetSealed(ent, part, false);
        SetSealed(ent, ent.Owner, false);
        Dirty(ent);

        if (!wasActive)
            return;

        var ev = new ModSuitPowerChangedEvent(false);
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnSealMessage(Entity<ModSuitDeployComponent> ent, ref ModSuitSealSlotMessage args)
    {
        var wearer = Transform(ent).ParentUid;
        if (wearer != args.Actor)
            return;

        if (string.IsNullOrEmpty(args.Slot))
            ToggleAll(ent, wearer);
        else
            TogglePart(ent, wearer, args.Slot);
    }

    // Right-click verb: seal/unseal everything at once.
    private void OnGetVerbs(Entity<ModSuitDeployComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var wearer = Transform(ent).ParentUid;
        if (wearer != args.User)
            return;

        var user = args.User;
        var allDeployed = AllDeployed(ent, wearer);
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(allDeployed ? "modsuit-verb-unseal-all" : "modsuit-verb-seal-all"),
            Act = () => ToggleAll(ent, user),
        });
    }

    private void OnControlUnitUnequipped(Entity<ModSuitDeployComponent> ent, ref GotUnequippedEvent args)
    {
        // Taking the control unit off the back retracts everything.
        RetractAll(ent, args.EquipTarget);
    }

    private void OnPartUnequipped(Entity<ModSuitPartComponent> ent, ref GotUnequippedEvent args)
    {
        // A part left a slot (retract, stripping) - always pull it back into the control unit.
        if (ent.Comp.Master is not { } master || !TryComp<ModSuitDeployComponent>(master, out var deploy))
            return;

        // Off the body it can't be sealed, whatever it was doing before. Covers stripping and any
        // other route out of the slot, not just a tidy retract.
        SetSealed((master, deploy), ent.Owner, false);

        if (deploy.PartContainers.TryGetValue(ent.Comp.Slot, out var container))
            _container.Insert(ent.Owner, container);

        UpdateSealed((master, deploy));
    }

    // ---- Per-part / all helpers ------------------------------------------------------------------

    private bool IsDeployed(Entity<ModSuitDeployComponent> ent, EntityUid wearer, string slot)
    {
        return ent.Comp.PartUids.TryGetValue(slot, out var part)
            && _inventory.TryGetSlotEntity(wearer, slot, out var existing)
            && existing == part;
    }

    private bool AllDeployed(Entity<ModSuitDeployComponent> ent, EntityUid wearer)
    {
        foreach (var slot in ent.Comp.Parts.Keys)
        {
            if (!IsDeployed(ent, wearer, slot))
                return false;
        }

        return ent.Comp.Parts.Count > 0;
    }

    private bool AnyDeployed(Entity<ModSuitDeployComponent> ent, EntityUid wearer)
    {
        foreach (var slot in ent.Comp.Parts.Keys)
        {
            if (IsDeployed(ent, wearer, slot))
                return true;
        }

        return false;
    }

    public void TogglePart(Entity<ModSuitDeployComponent> ent, EntityUid wearer, string slot)
    {
        if (IsDeployed(ent, wearer, slot))
            RetractPart(ent, wearer, slot);
        else
            DeployPart(ent, wearer, slot);
    }

    /// <param name="silent">
    ///     Suppresses this part's own hiss, for when a bulk deploy/retract plays one sound for the
    ///     whole set instead. SS13 does the same by passing a null user through to its deploy/retract.
    /// </param>
    public void DeployPart(Entity<ModSuitDeployComponent> ent, EntityUid wearer, string slot, bool silent = false)
    {
        if (!ent.Comp.PartUids.TryGetValue(slot, out var part))
            return;

        // Something other than our part is already in the slot.
        if (_inventory.TryGetSlotEntity(wearer, slot, out var existing) && existing != part)
        {
            // Overwear slots (gauntlets/boots) tuck the wearer's own clothing into the control unit
            // so the part deploys over it; it's restored on retract. Other slots stay blocked.
            if (!TryStowExisting(ent, wearer, slot, existing.Value))
            {
                _popup.PopupClient(Loc.GetString("modsuit-deploy-slot-blocked",
                    ("suit", ent.Owner), ("slot", slot)), ent, wearer);
                return;
            }
        }

        if (!_inventory.TryEquip(wearer, wearer, part, slot, silent: true, force: true, predicted: true))
            return;

        // Deploying into a suit that's already running seals the part on the spot, as SS13 does -
        // otherwise it'd sit open on a live suit, leaving a hole in the wearer's protection.
        if (ent.Comp.Active)
            SetSealed(ent, part, true);

        if (!silent)
            _audio.PlayPredicted(ent.Comp.StepSound, ent.Owner, wearer);

        UpdateSealed(ent);
    }

    /// <param name="silent">See <see cref="DeployPart"/>.</param>
    public void RetractPart(Entity<ModSuitDeployComponent> ent, EntityUid wearer, string slot, bool silent = false)
    {
        if (!ent.Comp.PartUids.TryGetValue(slot, out var part))
            return;

        var wasDeployed = _inventory.TryGetSlotEntity(wearer, slot, out var existing) && existing == part;
        if (wasDeployed)
            _inventory.TryUnequip(wearer, wearer, slot, silent: true, force: true);

        // Put back any clothing we tucked away to deploy over it.
        RestoreStowed(ent, wearer, slot);

        // Only hiss if a part actually came back in - retracting an already-stowed slot is a no-op.
        if (!silent && wasDeployed)
            _audio.PlayPredicted(ent.Comp.StepSound, ent.Owner, wearer);

        UpdateSealed(ent);
    }

    // Tucks the wearer's existing clothing in this slot into the control unit so an overwear part
    // can take the slot. Returns false if the slot isn't an overwear slot (caller then blocks).
    private bool TryStowExisting(Entity<ModSuitDeployComponent> ent, EntityUid wearer, string slot, EntityUid existing)
    {
        if (!ent.Comp.OverwearSlots.Contains(slot)
            || !ent.Comp.StowContainers.TryGetValue(slot, out var stow))
            return false;

        if (!_inventory.TryUnequip(wearer, wearer, slot, silent: true, force: true, predicted: true))
            return false;

        _container.Insert(existing, stow);
        return true;
    }

    // Restores clothing stowed for an overwear slot back onto the wearer (or drops it if that fails).
    private void RestoreStowed(Entity<ModSuitDeployComponent> ent, EntityUid wearer, string slot)
    {
        if (!ent.Comp.StowContainers.TryGetValue(slot, out var stow)
            || stow.ContainedEntity is not { } item)
            return;

        _container.Remove(item, stow);

        // If it can't go back on (e.g. the wearer is gone), it's left at the control unit's feet.
        _inventory.TryEquip(wearer, wearer, item, slot, silent: true, force: true, predicted: true);
    }

    public void DeployAll(Entity<ModSuitDeployComponent> ent, EntityUid wearer)
    {
        // silent: deploying the lot is one hiss for the whole set, not one per part, as in SS13.
        foreach (var slot in ent.Comp.Parts.Keys)
            DeployPart(ent, wearer, slot, silent: true);

        _audio.PlayPredicted(ent.Comp.StepSound, ent, wearer);
        _popup.PopupClient(Loc.GetString("modsuit-sealed", ("suit", ent.Owner)), ent, wearer);
    }

    public void RetractAll(Entity<ModSuitDeployComponent> ent, EntityUid wearer)
    {
        // Retracting always powers the suit down first (instantly - the parts are being pulled in).
        DeactivateInstant(ent);

        var any = false;
        foreach (var slot in ent.Comp.Parts.Keys)
        {
            if (IsDeployed(ent, wearer, slot))
                any = true;
            RetractPart(ent, wearer, slot, silent: true);
        }

        if (any)
        {
            _audio.PlayPredicted(ent.Comp.StepSound, ent, wearer);
            _popup.PopupClient(Loc.GetString("modsuit-unsealed", ("suit", ent.Owner)), ent, wearer);
        }
    }

    public void ToggleAll(Entity<ModSuitDeployComponent> ent, EntityUid wearer)
    {
        if (AllDeployed(ent, wearer))
            RetractAll(ent, wearer);
        else
            DeployAll(ent, wearer);
    }

    private void UpdateSealed(Entity<ModSuitDeployComponent> ent)
    {
        var wearer = Transform(ent).ParentUid;

        // Stick the control unit to the wearer while any part is deployed (adds the "stuck" examine
        // text and blocks self-removal via SelfUnremovableClothingSystem).
        if (AnyDeployed(ent, wearer))
            EnsureComp<SelfUnremovableClothingComponent>(ent);
        else
            RemComp<SelfUnremovableClothingComponent>(ent);

        var sealedNow = AllDeployed(ent, wearer);
        if (ent.Comp.Sealed == sealedNow)
            return;

        ent.Comp.Sealed = sealedNow;
        Dirty(ent);
    }
}

/// <summary>
///     Raised by the seal action to open the MODsuit seal radial menu.
/// </summary>
public sealed partial class ModSuitSealEvent : InstantActionEvent
{
}

/// <summary>
///     Raised by the activate action to power the MODsuit on or off.
/// </summary>
public sealed partial class ModSuitActivateEvent : InstantActionEvent
{
}

/// <summary>
///     Raised on a MODsuit control unit when it is powered on or off, so the module system can react.
/// </summary>
[ByRefEvent]
public readonly record struct ModSuitPowerChangedEvent(bool Active);

/// <summary>
///     Raised on a MODsuit part when the suit seals or unseals it, so the protection it only carries
///     while shut can be applied or stripped.
/// </summary>
/// <param name="Wearer">
///     Who the suit is on. Passed along because a part being retracted has already left the body by
///     the time this is raised, so its own transform no longer points at them.
/// </param>
[ByRefEvent]
public readonly record struct ModSuitPartSealedEvent(bool Sealed, EntityUid Wearer);
