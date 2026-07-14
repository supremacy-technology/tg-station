using Content.Shared.Actions;
using Content.Shared.Clothing.ModSuit.Components;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Shared.Clothing.ModSuit;

/// <summary>
///     Handles installing, listing, removing and toggling modules on a <see cref="ModSuitComponent"/>.
///     A lightweight port of the tgstation13 MODsuit module system to SS14.
/// </summary>
public sealed class ModSuitSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly ActionContainerSystem _actionContainer = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSuitComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<ModSuitComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ModSuitComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ModSuitComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<ModSuitComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ModSuitComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<ModSuitComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<ModSuitComponent, ModSuitModulesEvent>(OnModulesAction);
        SubscribeLocalEvent<ModSuitComponent, ModSuitPanelEvent>(OnPanelAction);
        SubscribeLocalEvent<ModSuitComponent, ModSuitModuleToggleMessage>(OnModuleToggle);
        SubscribeLocalEvent<ModSuitComponent, ModSuitPowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<ModSuitComponent, GotEquippedEvent>(OnSuitEquipped);
        SubscribeLocalEvent<ModSuitComponent, GotUnequippedEvent>(OnSuitUnequipped);

        SubscribeLocalEvent<ModSuitModuleComponent, ModSuitModuleActionEvent>(OnModuleActionUsed);
    }

    // --- Per-module hotkey actions ---------------------------------------------------------------

    private void OnSuitEquipped(Entity<ModSuitComponent> ent, ref GotEquippedEvent args)
    {
        if ((args.SlotFlags & SlotFlags.BACK) != SlotFlags.BACK || ent.Comp.ModuleContainer == null)
            return;

        foreach (var module in ent.Comp.ModuleContainer.ContainedEntities)
            GrantModuleAction(module, args.EquipTarget);
    }

    private void OnSuitUnequipped(Entity<ModSuitComponent> ent, ref GotUnequippedEvent args)
    {
        if (ent.Comp.ModuleContainer == null)
            return;

        foreach (var module in ent.Comp.ModuleContainer.ContainedEntities)
            RemoveModuleAction(module, args.EquipTarget);
    }

    private void GrantModuleAction(EntityUid module, EntityUid wearer)
    {
        // Granting spawns an action entity - server-authoritative to avoid predicting networked spawns.
        if (_net.IsClient || !TryComp<ModSuitModuleComponent>(module, out var comp) || !comp.Toggleable)
            return;

        _actions.AddAction(wearer, ref comp.ToggleActionEntity, comp.ToggleAction, module);
        if (comp.ToggleActionEntity != null)
            _actions.SetEntityIcon(comp.ToggleActionEntity.Value, module);
        Dirty(module, comp);
    }

    private void RemoveModuleAction(EntityUid module, EntityUid wearer)
    {
        if (_net.IsClient)
            return;

        if (TryComp<ModSuitModuleComponent>(module, out var comp) && comp.ToggleActionEntity != null)
            _actions.RemoveAction(wearer, comp.ToggleActionEntity.Value);
    }

    // Toggling a module through its own hotkey action.
    private void OnModuleActionUsed(Entity<ModSuitModuleComponent> ent, ref ModSuitModuleActionEvent args)
    {
        if (args.Handled || ent.Comp.InstalledSuit is not { } suit)
            return;

        args.Handled = true;

        if (!TryComp<ModSuitDeployComponent>(suit, out var deploy) || !deploy.Active)
        {
            _popup.PopupClient(Loc.GetString("modsuit-module-unpowered", ("suit", suit)), suit, args.Performer);
            return;
        }

        SetModuleActive(ent, args.Performer, !ent.Comp.Active);
    }

    // Grants/removes a module's action if the suit is currently worn on someone's back.
    private void RefreshModuleAction(Entity<ModSuitComponent> ent, EntityUid module, bool grant)
    {
        var wearer = Transform(ent).ParentUid;
        if (!_inventory.TryGetSlotEntity(wearer, "back", out var back) || back != ent.Owner)
            return;

        if (grant)
            GrantModuleAction(module, wearer);
        else
            RemoveModuleAction(module, wearer);
    }

    private void OnInit(Entity<ModSuitComponent> ent, ref ComponentInit args)
    {
        ent.Comp.ModuleContainer = _container.EnsureContainer<Container>(ent, ent.Comp.ModuleContainerId);
    }

    private void OnMapInit(Entity<ModSuitComponent> ent, ref MapInitEvent args)
    {
        _actionContainer.EnsureAction(ent, ref ent.Comp.ModulesActionEntity, ent.Comp.ModulesAction);
        _actionContainer.EnsureAction(ent, ref ent.Comp.PanelActionEntity, ent.Comp.PanelAction);
        Dirty(ent);
    }

    private void OnShutdown(Entity<ModSuitComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Comp.ModulesActionEntity);
        _actions.RemoveAction(ent.Comp.PanelActionEntity);
    }

    private void OnGetActions(Entity<ModSuitComponent> ent, ref GetItemActionsEvent args)
    {
        // Only offer the module menu and panel while the suit is worn on the back.
        if ((args.SlotFlags & SlotFlags.BACK) != SlotFlags.BACK)
            return;

        if (ent.Comp.ModulesActionEntity != null)
            args.AddAction(ent.Comp.ModulesActionEntity.Value);
        if (ent.Comp.PanelActionEntity != null)
            args.AddAction(ent.Comp.PanelActionEntity.Value);
    }

    private void OnModulesAction(Entity<ModSuitComponent> ent, ref ModSuitModulesEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _ui.OpenUi(ent.Owner, ModSuitModuleUiKey.Modules, args.Performer);
    }

    private void OnPanelAction(Entity<ModSuitComponent> ent, ref ModSuitPanelEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        _ui.OpenUi(ent.Owner, ModSuitPanelUiKey.Key, args.Performer);
    }

    private void OnModuleToggle(Entity<ModSuitComponent> ent, ref ModSuitModuleToggleMessage args)
    {
        var wearer = Transform(ent).ParentUid;
        if (wearer != args.Actor)
            return;

        var module = GetEntity(args.Module);
        if (!TryComp<ModSuitModuleComponent>(module, out var moduleComp))
            return;

        // Module must actually be installed in this suit.
        if (moduleComp.InstalledSuit != ent.Owner || !moduleComp.Toggleable)
            return;

        // The suit must be powered on to run modules.
        if (!TryComp<ModSuitDeployComponent>(ent, out var deploy) || !deploy.Active)
        {
            _popup.PopupClient(Loc.GetString("modsuit-module-unpowered", ("suit", ent.Owner)), ent, wearer);
            return;
        }

        SetModuleActive((module, moduleComp), wearer, !moduleComp.Active);
    }

    private void OnPowerChanged(Entity<ModSuitComponent> ent, ref ModSuitPowerChangedEvent args)
    {
        if (args.Active || ent.Comp.ModuleContainer == null)
            return;

        // Powering down switches every active module off.
        foreach (var installed in ent.Comp.ModuleContainer.ContainedEntities)
        {
            if (TryComp<ModSuitModuleComponent>(installed, out var module) && module.Active)
                SetModuleActive((installed, module), Transform(ent).ParentUid, false);
        }
    }

    /// <summary>
    ///     Switches a toggleable module on or off, granting/removing its effect components on the wearer.
    /// </summary>
    public void SetModuleActive(Entity<ModSuitModuleComponent> module, EntityUid wearer, bool active)
    {
        if (module.Comp.Active == active)
            return;

        if (active)
        {
            module.Comp.AppliedTo = wearer;
            EntityManager.AddComponents(wearer, module.Comp.WearerComponents);
        }
        else if (module.Comp.AppliedTo is { } target && !TerminatingOrDeleted(target))
        {
            EntityManager.RemoveComponents(target, module.Comp.RemoveComponents ?? module.Comp.WearerComponents);
            module.Comp.AppliedTo = null;
        }

        module.Comp.Active = active;
        Dirty(module);
    }

    private void OnInteractUsing(Entity<ModSuitComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<ModSuitModuleComponent>(args.Used, out var module))
            return;

        args.Handled = TryInstallModule(ent, (args.Used, module), args.User);
    }

    /// <summary>
    ///     Attempts to install a module into the suit, respecting the complexity budget.
    /// </summary>
    public bool TryInstallModule(Entity<ModSuitComponent> ent, Entity<ModSuitModuleComponent> module, EntityUid user)
    {
        if (ent.Comp.ModuleContainer == null)
            return false;

        if (module.Comp.InstalledSuit != null)
            return false;

        if (ent.Comp.UsedComplexity + module.Comp.Complexity > ent.Comp.MaxComplexity)
        {
            _popup.PopupClient(Loc.GetString("modsuit-install-no-complexity",
                    ("module", module.Owner), ("suit", ent.Owner)), ent, user);
            return false;
        }

        if (!_container.Insert(module.Owner, ent.Comp.ModuleContainer))
            return false;

        module.Comp.InstalledSuit = ent;
        Dirty(module);
        RecalculateComplexity(ent);
        RefreshModuleAction(ent, module.Owner, grant: true);

        _popup.PopupClient(Loc.GetString("modsuit-install-success",
                ("module", module.Owner), ("suit", ent.Owner)), ent, user);
        return true;
    }

    /// <summary>
    ///     Removes an installed module from the suit, dropping it into the user's hands where possible.
    /// </summary>
    public bool TryRemoveModule(Entity<ModSuitComponent> ent, Entity<ModSuitModuleComponent> module, EntityUid user)
    {
        if (ent.Comp.ModuleContainer == null || !module.Comp.Removable)
            return false;

        // Turn the module off first so its effects are stripped from the wearer.
        if (module.Comp.Active)
            SetModuleActive(module, user, false);
        RefreshModuleAction(ent, module.Owner, grant: false);

        if (!_container.Remove(module.Owner, ent.Comp.ModuleContainer))
            return false;

        module.Comp.InstalledSuit = null;
        Dirty(module);
        RecalculateComplexity(ent);

        _hands.PickupOrDrop(user, module);

        _popup.PopupClient(Loc.GetString("modsuit-remove-success",
                ("module", module.Owner), ("suit", ent.Owner)), ent, user);
        return true;
    }

    private void RecalculateComplexity(Entity<ModSuitComponent> ent)
    {
        if (ent.Comp.ModuleContainer == null)
            return;

        var used = 0;
        foreach (var installed in ent.Comp.ModuleContainer.ContainedEntities)
        {
            if (TryComp<ModSuitModuleComponent>(installed, out var module))
                used += module.Complexity;
        }

        ent.Comp.UsedComplexity = used;
        Dirty(ent);
    }

    private void OnExamined(Entity<ModSuitComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("modsuit-examine-complexity",
            ("used", ent.Comp.UsedComplexity), ("max", ent.Comp.MaxComplexity)));

        if (ent.Comp.ModuleContainer is not { Count: > 0 } container)
            return;

        args.PushMarkup(Loc.GetString("modsuit-examine-modules-header"));
        foreach (var module in container.ContainedEntities)
        {
            args.PushMarkup(Loc.GetString("modsuit-examine-module-entry", ("module", module)));
        }
    }

    private void OnGetVerbs(Entity<ModSuitComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.ModuleContainer == null)
            return;

        var user = args.User;
        foreach (var installed in ent.Comp.ModuleContainer.ContainedEntities)
        {
            if (!TryComp<ModSuitModuleComponent>(installed, out var module) || !module.Removable)
                continue;

            var moduleEnt = new Entity<ModSuitModuleComponent>(installed, module);
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("modsuit-verb-remove-module", ("module", installed)),
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/eject.svg.192dpi.png")),
                Act = () => TryRemoveModule(ent, moduleEnt, user),
            });
        }
    }
}
