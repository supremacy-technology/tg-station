using System.Collections.Generic;
using Content.Client.UserInterface.Controls;
using Content.Shared.Clothing.ModSuit;
using Content.Shared.Clothing.ModSuit.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Clothing.ModSuit;

/// <summary>
///     Radial menu opened by the MODsuit seal action. Shows one button per deployable part (toggle that
///     part) plus a "toggle all" button in the middle.
/// </summary>
[UsedImplicitly]
public sealed class ModSuitRadialBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private SimpleRadialMenu? _menu;

    public ModSuitRadialBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();

        if (!EntMan.TryGetComponent<ModSuitDeployComponent>(Owner, out var comp))
            return;

        _menu = this.CreateWindow<SimpleRadialMenu>();
        _menu.Track(Owner);
        _menu.SetButtons(BuildButtons(comp));
        _menu.OpenOverMouseScreenPosition();
    }

    private IEnumerable<RadialMenuOptionBase> BuildButtons(ModSuitDeployComponent comp)
    {
        var buttons = new List<RadialMenuOptionBase>();

        foreach (var (slot, proto) in comp.Parts)
        {
            var tooltip = _prototype.TryIndex(proto, out var entProto)
                ? Loc.GetString("modsuit-radial-toggle-part", ("part", entProto.Name))
                : slot;

            buttons.Add(new RadialMenuActionOption<string>(OnSlotSelected, slot)
            {
                ToolTip = tooltip,
                IconSpecifier = RadialMenuIconSpecifier.With(proto),
            });
        }

        buttons.Add(new RadialMenuActionOption<string>(OnSlotSelected, string.Empty)
        {
            ToolTip = Loc.GetString("modsuit-radial-toggle-all"),
            IconSpecifier = RadialMenuIconSpecifier.With(
                new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/outfit.svg.192dpi.png"))),
        });

        return buttons;
    }

    private void OnSlotSelected(string slot)
    {
        SendMessage(new ModSuitSealSlotMessage(slot));
    }
}
