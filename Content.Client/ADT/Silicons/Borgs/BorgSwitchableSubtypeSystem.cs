using Content.Shared.ADT.Silicons.Borgs;
using Content.Shared.ADT.Silicons.Borgs.Components;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations;

namespace Content.Client.ADT.Silicons.Borgs;

public sealed partial class BorgSwitchableSubtypeSystem : SharedBorgSwitchableSubtypeSystem
{
    [Dependency] private readonly IResourceCache _resourceCache = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly AppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgSwitchableSubtypeComponent, ComponentStartup>(OnComponentStartup);
        SubscribeLocalEvent<BorgSwitchableSubtypeComponent, AfterAutoHandleStateEvent>(AfterStateHandler);
    }

    private void OnComponentStartup(Entity<BorgSwitchableSubtypeComponent> ent, ref ComponentStartup args)
    {
        UpdateVisuals(ent);
    }

    private void AfterStateHandler(Entity<BorgSwitchableSubtypeComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateVisuals(ent);
    }

    protected override void SetAppearanceFromSubtype(Entity<BorgSwitchableSubtypeComponent> ent, ProtoId<BorgSubtypePrototype> subtype)
    {
        if (!_prototypeManager.TryIndex(subtype, out var subtypePrototype)
            || !_prototypeManager.TryIndex(subtypePrototype.ParentBorgType, out var borgType)
            || !TryComp(ent, out SpriteComponent? sprite))
            return;

        var rsiPath = SpriteSpecifierSerializer.TextureRoot / subtypePrototype.Sprite;

        if (_resourceCache.TryGetResource<RSIResource>(rsiPath, out var resource))
        {
            // _Duty: раньше эти четыре поля присваивались прямо в
            // subtypePrototype (singleton-инстанс из IPrototypeManager,
            // общий на ВСЕ сущности с этим прототипом) — фактически
            // перманентно перезаписывало общий прототип значениями
            // родительского borgType при каждом обновлении внешнего вида
            // одного конкретного борджа. Сейчас в yml состояния подтипов и
            // типов совпадают по дефолту ("robot"/"robot_l"/...), поэтому
            // видимого эффекта пока нет, но при малейшем расхождении
            // (кастомный подтип/тип со своими спрайт-стейтами, или
            // hot-reload прототипов) это тихо ломало бы внешний вид ВСЕХ
            // борджей этого подтипа, а не только текущего. Используем
            // локальные переменные вместо мутации прототипа.
            var bodyState = borgType.SpriteBodyState;
            var toggleLightState = borgType.SpriteToggleLightState;
            var hasMindState = borgType.SpriteHasMindState;
            var noMindState = borgType.SpriteNoMindState;

            if (!_appearance.TryGetData<bool>(ent, BorgVisuals.HasPlayer, out var hasPlayer))
                hasPlayer = false;

            sprite.LayerSetState(BorgVisualLayers.Body, bodyState);
            sprite.LayerSetState(BorgVisualLayers.Light, hasPlayer ? hasMindState : noMindState);
            sprite.LayerSetState(BorgVisualLayers.LightStatus, toggleLightState);

            sprite.LayerSetRSI(BorgVisualLayers.Body.GetHashCode(), resource.RSI);
            sprite.LayerSetRSI(BorgVisualLayers.Light.GetHashCode(), resource.RSI);
            sprite.LayerSetRSI(BorgVisualLayers.LightStatus.GetHashCode(), resource.RSI);
        }
    }
}
