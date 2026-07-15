using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Shared.Clothing.ModSuit;
using Content.Shared.Clothing.ModSuit.Components;

namespace Content.Server.Clothing.ModSuit;

/// <summary>
///     Gates a MODsuit part's pressure and temperature protection on the suit having sealed it shut.
///     Deployed but open, a part is just plating strapped over your clothes; it only keeps the vacuum
///     and the cold out once the suit powers on and closes it. This mirrors tgstation13, where a MOD
///     part's protection is applied in seal_part and stripped again on unseal.
///
///     Lives server-side because both kinds of protection do.
/// </summary>
public sealed class ModSuitPartProtectionSystem : EntitySystem
{
    [Dependency] private readonly BarotraumaSystem _barotrauma = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ModSuitPartComponent, GetPressureProtectionValuesEvent>(OnGetProtection);
        SubscribeLocalEvent<ModSuitPartComponent, GetTemperatureProtectionEvent>(OnGetTemperatureProtection);
        SubscribeLocalEvent<ModSuitPartComponent, ModSuitPartSealedEvent>(OnSealed);
    }

    /// <summary>
    ///     Strips an open part's insulation. A coefficient of 1 passes the full temperature change
    ///     through, i.e. no protection at all.
    ///
    ///     Unlike pressure, this is judged per part rather than across the whole suit. Temperature
    ///     relays to every worn slot and multiplies each one's coefficient together, so each part
    ///     already contributes its own share and losing one costs you only that share - which matches
    ///     SS13, where a part's heat_protection covers just its own body parts.
    /// </summary>
    private void OnGetTemperatureProtection(Entity<ModSuitPartComponent> ent, ref GetTemperatureProtectionEvent args)
    {
        if (ent.Comp.Sealed)
            return;

        args.Coefficient = 1f;
    }

    /// <summary>
    ///     Neutralises a part's protection unless the whole suit is shut. Barotrauma reads a slot
    ///     reporting 1x/0 as bare skin, so this leaves the wearer exposed.
    ///
    ///     SS13 requires the chest, head, hands and feet all to be sealed before it calls you
    ///     pressurised, so an open gauntlet is as fatal as an open helmet. SS14 only ever checks the
    ///     head and outerClothing slots (see BarotraumaComponent.ProtectionSlots) and never looks at
    ///     gloves or shoes - so gating those two slots on the entire suit being sealed is what
    ///     reproduces SS13's rule here. Widening ProtectionSlots instead would demand sealed gloves
    ///     of every other hardsuit in the game.
    /// </summary>
    private void OnGetProtection(Entity<ModSuitPartComponent> ent, ref GetPressureProtectionValuesEvent args)
    {
        if (ent.Comp.Sealed && AllPartsSealed(ent.Comp.Master))
            return;

        args.HighPressureMultiplier = 1f;
        args.HighPressureModifier = 0f;
        args.LowPressureMultiplier = 1f;
        args.LowPressureModifier = 0f;
    }

    /// <summary>
    ///     Whether every part of the suit is deployed and sealed. A retracted part is never sealed, so
    ///     this covers both pulling a part in and leaving it deployed but open.
    /// </summary>
    private bool AllPartsSealed(EntityUid? master)
    {
        if (!TryComp<ModSuitDeployComponent>(master, out var deploy))
            return false;

        foreach (var part in deploy.PartUids.Values)
        {
            if (!TryComp<ModSuitPartComponent>(part, out var comp) || !comp.Sealed)
                return false;
        }

        return true;
    }

    /// <summary>
    ///     Protection is cached on the wearer as equipment goes on and off, so sealing or retracting a
    ///     part changes nothing until the cache is rebuilt. Barotrauma won't do it for us here: it
    ///     only refreshes for slots in its ProtectionSlots, which gloves and shoes aren't in.
    /// </summary>
    private void OnSealed(Entity<ModSuitPartComponent> ent, ref ModSuitPartSealedEvent args)
    {
        _barotrauma.RefreshPressureResistance(args.Wearer);
    }
}
