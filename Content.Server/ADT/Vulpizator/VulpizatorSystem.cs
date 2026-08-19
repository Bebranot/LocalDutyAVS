using Content.Server.Antag;
using Content.Server.Polymorph.Components;
using Content.Shared.Mindshield.Components;
using Content.Shared.Roles.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server.Vulpizator.System;

public sealed class VulpizatorSystem : EntitySystem
{
    [Dependency] private readonly AntagSelectionSystem _antag = default!;
    public const string Vulpa = "ADTMobRandomVulpkanin";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PolymorphedEntityComponent, StartCollideEvent>(OnPolymorphed);
    }
    private void OnPolymorphed(Entity<PolymorphedEntityComponent> uid, ref StartCollideEvent args)
    {
        if (uid.Comp.Configuration.Entity == Vulpa)
        {
            // Чтобы вульпе не приходило по 100 раз сообщение
            if (HasComp<RoleBriefingComponent>(uid))
            {
                return;
            }
            // _Duty: было в обратном порядке — MetaDataComponent есть практически у ЛЮБОЙ
            // сущности, так что первая ветка всегда срабатывала и специфичная проверка
            // MindShieldComponent (предупреждение про майндщит) была недостижима.
            // Специфичная проверка должна идти первой.
            if (HasComp<MindShieldComponent>(uid))
            {
                _antag.SendBriefing(uid, Loc.GetString("vulpa-role-mindshild"), Color.Red, null);
                EnsureComp<RoleBriefingComponent>(uid);
            }
            else if (HasComp<MetaDataComponent>(uid))
            {
                _antag.SendBriefing(uid, Loc.GetString("vulpa-role-greeting"), Color.Red, null);
                EnsureComp<RoleBriefingComponent>(uid);
            }
        }
    }
}
