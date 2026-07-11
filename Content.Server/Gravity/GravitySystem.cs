using Content.Shared.Gravity;
using Content.Shared.ZLevels.Core.EntitySystems;
using JetBrains.Annotations;
using Robust.Shared.Map.Components;

namespace Content.Server.Gravity
{
    [UsedImplicitly]
    public sealed class GravitySystem : SharedGravitySystem
    {
        [Dependency] private readonly SharedZLevelsSystem _zLevels = default!;

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<GravityComponent, ComponentInit>(OnGravityInit);
        }

        /// <summary>
        /// Iterates gravity components and checks if this entity can have gravity applied.
        /// </summary>
        public void RefreshGravity(EntityUid uid, GravityComponent? gravity = null)
        {
            if (!GravityQuery.Resolve(uid, ref gravity))
                return;

            if (gravity.Inherent)
                return;

            var enabled = false;

            // Grids and maps in a z-level network share gravity across the whole stack:
            // an active generator anywhere in the network powers every level.
            HashSet<EntityUid>? networkMaps = null;
            if (Transform(uid).MapUid is { } mapUid && _zLevels.TryZNetwork(mapUid, out var network))
            {
                networkMaps = new HashSet<EntityUid>();
                foreach (var (_, netMap) in network.Value.Comp.ZLevels)
                {
                    if (netMap != null)
                        networkMaps.Add(netMap.Value);
                }
            }

            foreach (var (comp, xform) in EntityQuery<GravityGeneratorComponent, TransformComponent>(true))
            {
                if (!comp.GravityActive)
                    continue;

                if (xform.ParentUid != uid &&
                    (networkMaps == null || xform.MapUid is not { } generatorMap || !networkMaps.Contains(generatorMap)))
                    continue;

                enabled = true;
                break;
            }

            if (enabled != gravity.Enabled)
            {
                gravity.Enabled = enabled;
                var ev = new GravityChangedEvent(uid, enabled);
                RaiseLocalEvent(uid, ref ev, true);
                Dirty(uid, gravity);

                if (HasComp<MapGridComponent>(uid))
                {
                    StartGridShake(uid);
                }
            }
        }

        private void OnGravityInit(EntityUid uid, GravityComponent component, ComponentInit args)
        {
            RefreshGravity(uid);
        }

        /// <summary>
        /// Enables gravity. Note that this is a fast-path for GravityGeneratorSystem.
        /// This means it does nothing if Inherent is set and it might be wiped away with a refresh
        ///  if you're not supposed to be doing whatever you're doing.
        /// </summary>
        public void EnableGravity(EntityUid uid, GravityComponent? gravity = null)
        {
            if (!GravityQuery.Resolve(uid, ref gravity))
                return;

            if (gravity.Enabled || gravity.Inherent)
                return;

            gravity.Enabled = true;
            var ev = new GravityChangedEvent(uid, true);
            RaiseLocalEvent(uid, ref ev, true);
            Dirty(uid, gravity);

            if (HasComp<MapGridComponent>(uid))
            {
                StartGridShake(uid);
            }
        }
    }
}
