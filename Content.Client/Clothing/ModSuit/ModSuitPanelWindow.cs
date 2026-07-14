using System.Numerics;
using Content.Client.Message;
using Content.Shared.Clothing.ModSuit.Components;
using Content.Shared.Power.Components;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Containers;
using Robust.Shared.IoC;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.Clothing.ModSuit;

/// <summary>
///     The MOD interface panel: a live, read-only readout of the suit's status, power cell,
///     complexity, installed modules and deploy state. Rebuilt each frame from networked component data.
/// </summary>
public sealed class ModSuitPanelWindow : DefaultWindow
{
    private readonly IEntityManager _ent;
    private readonly IPrototypeManager _proto;
    private readonly EntityUid _owner;
    private readonly BoxContainer _content;

    public ModSuitPanelWindow(IEntityManager ent, EntityUid owner)
    {
        _ent = ent;
        _owner = owner;
        _proto = IoCManager.Resolve<IPrototypeManager>();

        Title = Loc.GetString("modsuit-panel-title");
        MinSize = new Vector2(380, 320);
        Contents.AddChild(_content = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical });
        Refresh();
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        Refresh();
    }

    private void Refresh()
    {
        _content.RemoveAllChildren();

        if (!_ent.TryGetComponent<ModSuitComponent>(_owner, out var mod))
            return;

        _ent.TryGetComponent<ModSuitDeployComponent>(_owner, out var deploy);
        _ent.TryGetComponent<ContainerManagerComponent>(_owner, out var containers);

        // --- Status ---
        AddHeader(Loc.GetString("modsuit-panel-section-status"));

        var status = deploy is { Active: true }
            ? Loc.GetString("modsuit-panel-status-active")
            : deploy is { Sealed: true }
                ? Loc.GetString("modsuit-panel-status-deployed")
                : Loc.GetString("modsuit-panel-status-stowed");
        AddLine(Loc.GetString("modsuit-panel-status", ("status", status)));
        AddLine(Loc.GetString("modsuit-panel-complexity", ("used", mod.UsedComplexity), ("max", mod.MaxComplexity)));

        var cellText = Loc.GetString("modsuit-panel-no-cell");
        if (containers != null &&
            containers.Containers.TryGetValue("cell_slot", out var cellCont) &&
            cellCont.ContainedEntities.Count > 0 &&
            _ent.TryGetComponent<BatteryComponent>(cellCont.ContainedEntities[0], out var battery))
        {
            cellText = Loc.GetString("modsuit-panel-cell", ("cap", (int) (battery.MaxCharge / 1000)));
        }
        AddLine(cellText);

        // --- Modules ---
        AddHeader(Loc.GetString("modsuit-panel-section-modules"));
        if (containers != null &&
            containers.Containers.TryGetValue(ModSuitComponent.DefaultModuleContainerId, out var modCont) &&
            modCont.ContainedEntities.Count > 0)
        {
            foreach (var module in modCont.ContainedEntities)
            {
                if (!_ent.TryGetComponent<ModSuitModuleComponent>(module, out var mc))
                    continue;

                var name = _ent.GetComponent<MetaDataComponent>(module).EntityName;
                var state = !mc.Toggleable
                    ? Loc.GetString("modsuit-panel-module-passive")
                    : mc.Active
                        ? Loc.GetString("modsuit-panel-module-on")
                        : Loc.GetString("modsuit-panel-module-off");
                AddLine(Loc.GetString("modsuit-panel-module-entry",
                    ("name", name), ("state", state), ("complexity", mc.Complexity)));
            }
        }
        else
        {
            AddLine(Loc.GetString("modsuit-panel-no-modules"));
        }

        // --- Hardware ---
        if (deploy != null)
        {
            AddHeader(Loc.GetString("modsuit-panel-section-hardware"));
            foreach (var (slot, proto) in deploy.Parts)
            {
                // An empty part container means the part is deployed out onto the wearer.
                var deployed = containers != null &&
                               containers.Containers.TryGetValue("modsuit-part-" + slot, out var pc) &&
                               pc.ContainedEntities.Count == 0;

                var partName = _proto.TryIndex(proto, out var entProto) ? entProto.Name : slot;
                var state = deployed
                    ? Loc.GetString("modsuit-panel-part-deployed")
                    : Loc.GetString("modsuit-panel-part-stowed");
                AddLine(Loc.GetString("modsuit-panel-part-entry", ("name", partName), ("state", state)));
            }
        }
    }

    private void AddHeader(string text)
    {
        var label = new RichTextLabel { Margin = new Thickness(0, 6, 0, 2) };
        label.SetMarkupPermissive($"[bold]{text}[/bold]");
        _content.AddChild(label);
    }

    private void AddLine(string text)
    {
        // RichTextLabel parses [color]/[bold] markup; a plain Label would show the tags verbatim.
        var label = new RichTextLabel();
        label.SetMarkupPermissive(text);
        _content.AddChild(label);
    }
}
