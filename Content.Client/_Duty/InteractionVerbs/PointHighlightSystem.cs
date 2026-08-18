// _Duty: клиентская обводка верба "Указать" (PointAt) — навешивает шейдер PointHighlightOutline
// (клон SelectionOutline с параметрами, при которых обводка реально гаснет в темноте, а не просто
// тускнеет — см. Resources/Prototypes/_Duty/Shaders/point_highlight.yml,
// Resources/Textures/Shaders/outline.swsl) на сущность, пока у неё есть PointHighlightComponent.
// Своей проверки освещённости тут нет и не нужно — шейдер сэмплит локальный свет за пиксель у
// каждого наблюдающего клиента отдельно (см. паттерн Content.Client/Stealth/StealthSystem.cs,
// Content.Client/ADT/Light/LightVisibilitySystem.cs).
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.InteractionVerbs.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._Duty.InteractionVerbs;

public sealed class PointHighlightSystem : EntitySystem
{
    private static readonly ProtoId<ShaderPrototype> ShaderId = "PointHighlightOutline";

    [Dependency] private readonly IPrototypeManager _protoMan = default!;

    private ShaderInstance _shader = default!;

    public override void Initialize()
    {
        base.Initialize();

        _shader = _protoMan.Index(ShaderId).InstanceUnique();

        SubscribeLocalEvent<PointHighlightComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<PointHighlightComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnStartup(Entity<PointHighlightComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<SpriteComponent>(ent.Owner, out var sprite))
            sprite.PostShader = _shader;
    }

    private void OnShutdown(Entity<PointHighlightComponent> ent, ref ComponentShutdown args)
    {
        if (TerminatingOrDeleted(ent.Owner))
            return;

        // Не затираем чужой шейдер, если что-то другое успело перехватить PostShader за это время.
        if (TryComp<SpriteComponent>(ent.Owner, out var sprite) && sprite.PostShader == _shader)
            sprite.PostShader = null;
    }
}
