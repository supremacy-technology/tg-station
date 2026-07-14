using System.Collections.Generic;
using Content.Client.UserInterface.Controls;
using Content.Shared.Clothing.ModSuit;
using Content.Shared.Clothing.ModSuit.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Client.Clothing.ModSuit;

/// <summary>
///     Radial menu opened by the MODsuit modules action. Shows one button per installed toggleable
///     module (flashlight, magnetic stability, ...) and toggles it when clicked.
/// </summary>
[UsedImplicitly]
public sealed class ModSuitModuleRadialBoundUserInterface : BoundUserInterface
{
    private SimpleRadialMenu? _menu;

    public ModSuitModuleRadialBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _menu = this.CreateWindow<SimpleRadialMenu>();
        _menu.Track(Owner);
        _menu.SetButtons(BuildButtons());
        _menu.OpenOverMouseScreenPosition();
    }

    private IEnumerable<RadialMenuOptionBase> BuildButtons()
    {
        var buttons = new List<RadialMenuOptionBase>();

        if (!EntMan.TryGetComponent<ContainerManagerComponent>(Owner, out var containerMan) ||
            !containerMan.Containers.TryGetValue(ModSuitComponent.DefaultModuleContainerId, out var container))
        {
            return buttons;
        }

        foreach (var module in container.ContainedEntities)
        {
            if (!EntMan.TryGetComponent<ModSuitModuleComponent>(module, out var comp) || !comp.Toggleable)
                continue;

            var meta = EntMan.GetComponent<MetaDataComponent>(module);
            var netModule = EntMan.GetNetEntity(module);
            var icon = meta.EntityPrototype?.ID is { } protoId
                ? RadialMenuIconSpecifier.With(new EntProtoId(protoId))
                : null;

            buttons.Add(new RadialMenuActionOption<NetEntity>(OnModuleSelected, netModule)
            {
                ToolTip = Loc.GetString(comp.Active ? "modsuit-radial-module-on" : "modsuit-radial-module-off",
                    ("module", meta.EntityName)),
                IconSpecifier = icon,
                BackgroundColor = comp.Active ? Color.Green : null,
            });
        }

        return buttons;
    }

    private void OnModuleSelected(NetEntity module)
    {
        SendMessage(new ModSuitModuleToggleMessage(module));
    }
}
