using Content.Client.Silicons.Borgs;
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
    // _Duty-start: [Dependency] без readonly (RA0051) и SpriteSystem вместо устаревших методов SpriteComponent
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private BorgSystem _borgSystem = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    // _Duty-end

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

            // _Duty-start: RSI и состояние ставим одним вызовом и по ключу слоя. Раньше RSI искался по
            // индексу GetHashCode() enum-а — совпадало с ключами лишь при текущем порядке слоёв в yml.
            Entity<SpriteComponent?> spriteEnt = (ent.Owner, sprite);
            _sprite.LayerSetRsi(spriteEnt, BorgVisualLayers.Body, resource.RSI, bodyState);
            _sprite.LayerSetRsi(spriteEnt, BorgVisualLayers.Light, resource.RSI, hasPlayer ? hasMindState : noMindState);
            _sprite.LayerSetRsi(spriteEnt, BorgVisualLayers.LightStatus, resource.RSI, toggleLightState);
            // _Duty-end

            if (TryComp(ent, out BorgChassisComponent? chassis))
            {
                // _Duty: состояния берём те же, что выставлены слоям выше (от типа). Поля подтипа ни в
                // одном yml не заданы и всегда дефолтные "robot_e"/"robot_e_r", которых в RSI подтипов
                // нет — индикатор разума у всех подтипов киборгов ломался (DummyIconTest).
                _borgSystem.SetMindStates(
                    (ent.Owner, chassis),
                    hasMindState,
                    noMindState);

                if (TryComp(ent, out AppearanceComponent? appearance))
                {
                    _appearance.QueueUpdate(ent, appearance);
                }
            }
        }
    }
}
