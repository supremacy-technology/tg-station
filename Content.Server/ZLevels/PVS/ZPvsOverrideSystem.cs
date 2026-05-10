/*
 * This file is sublicensed under MIT License
 * https://github.com/space-wizards/space-station-14/blob/master/LICENSE.TXT
 */

using Robust.Server.GameStates;

namespace Content.Server.ZLevels.PVS;

public sealed partial class ZPvsOverrideSystem : EntitySystem
{
    [Dependency] private readonly PvsOverrideSystem _pvs = default!;
    public override void Initialize()
    {
        SubscribeLocalEvent<ZPvsOverrideComponent, ComponentStartup>(OnPvsStartup);
        SubscribeLocalEvent<ZPvsOverrideComponent, ComponentShutdown>(OnPvsShutdown);
    }

    private void OnPvsShutdown(Entity<ZPvsOverrideComponent> ent, ref ComponentShutdown args)
    {
        _pvs.RemoveGlobalOverride(ent);
    }

    private void OnPvsStartup(Entity<ZPvsOverrideComponent> ent, ref ComponentStartup args)
    {
        _pvs.AddGlobalOverride(ent);
    }
}
