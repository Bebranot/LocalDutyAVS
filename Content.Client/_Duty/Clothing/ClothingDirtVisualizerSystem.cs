// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx. Урезано: у нас нет частей тела (BodyPartComponent/
// VisualOrganComponent), поэтому вся ветка «пятно прямо на коже» (UpdateBodyPart/
// OnPartRelationshipChanged/OnBodyPartVisualChanged/ClearBodyLayer) не перенесена — грязь
// рисуется только на самом предмете (в мире, в руке, надетым).

using Content.Client.Items.Systems;
using Content.Shared._Duty.Clothing;
using Content.Shared.Clothing;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Hands;
using Content.Shared.Item;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using System.Numerics;
using System.Linq;

namespace Content.Client._Duty.Clothing;

public sealed partial class ClothingDirtVisualizerSystem : EntitySystem
{
    private const string DirtShader = "DutyDirtCoverage";
    private const string WorldLayerPrefix = "duty-dirt-world-";

    [Dependency] private readonly SpriteSystem _sprite = default!;
    [Dependency] private readonly SharedItemSystem _item = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    private readonly Dictionary<EntityUid, List<string>> _worldLayers = [];
    private readonly HashSet<EntityUid> _pending = [];
    private readonly Dictionary<(EntityUid Entity, string Layer), ShaderInstance> _shaders = [];
    private readonly Dictionary<EntityUid, HashSet<(EntityUid Entity, string Layer)>> _itemShaders = [];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClothingDirtableComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<ClothingDirtableComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<ClothingDirtableComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<ClothingDirtableComponent, GetEquipmentVisualsEvent>(OnEquipment,
            after: new[] { typeof(ClothingSystem) });
        SubscribeLocalEvent<ClothingDirtableComponent, GetInhandVisualsEvent>(OnInhand,
            after: new[] { typeof(ItemSystem) });
        SubscribeLocalEvent<ClothingDirtableComponent, EquipmentVisualsUpdatedEvent>(OnEquipmentUpdated);
        SubscribeLocalEvent<ClothingDirtableComponent, HeldVisualsUpdatedEvent>(OnHeldUpdated);
    }

    public override void Shutdown()
    {
        foreach (var (target, key) in new List<(EntityUid Entity, string Layer)>(_shaders.Keys))
            RemoveShader(target, key);
        _itemShaders.Clear();
        base.Shutdown();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        foreach (var uid in _pending)
        {
            if (!TryComp(uid, out ClothingDirtableComponent? dirtable))
                continue;
            if (TryComp(uid, out SpriteComponent? sprite))
                UpdateWorldSprite((uid, dirtable), sprite);
            _item.VisualsChanged(uid);
        }
        _pending.Clear();
    }

    private void OnStartup(Entity<ClothingDirtableComponent> ent, ref ComponentStartup args)
    {
        // Чистой одежде (а это почти вся одежда на карте) делать нечего — без этой отсечки каждый
        // предмет при входе в PVS дёргал бы VisualsChanged впустую. Если грязь придёт позже,
        // предмет попадёт в очередь через OnState.
        if (ent.Comp.DirtColor != null)
            _pending.Add(ent);
    }

    private void OnRemove(Entity<ClothingDirtableComponent> ent, ref ComponentRemove args)
    {
        _pending.Remove(ent);
        ClearItemShaders(ent);
        ClearWorldLayers(ent);
    }

    private void OnState(Entity<ClothingDirtableComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        ClearItemShaders(ent);
        _pending.Add(ent);
    }

    private void OnEquipment(Entity<ClothingDirtableComponent> ent, ref GetEquipmentVisualsEvent args)
        => AddMaskedLayers(ent.Comp, args.Layers);

    private void OnInhand(Entity<ClothingDirtableComponent> ent, ref GetInhandVisualsEvent args)
        => AddMaskedLayers(ent.Comp, args.Layers);

    private void OnEquipmentUpdated(Entity<ClothingDirtableComponent> ent, ref EquipmentVisualsUpdatedEvent args)
        => ApplyShaders(ent, args.Equipee, args.RevealedLayers, ent.Comp.DirtColor);

    private void OnHeldUpdated(Entity<ClothingDirtableComponent> ent, ref HeldVisualsUpdatedEvent args)
        => ApplyShaders(ent, args.User, args.RevealedLayers, ent.Comp.DirtColor);

    private static void AddMaskedLayers(ClothingDirtableComponent component, List<(string, PrototypeLayerData)> layers)
    {
        if (component.DirtColor is not { } dirt)
            return;

        var count = layers.Count;
        for (var i = 0; i < count; i++)
        {
            var (key, source) = layers[i];
            var copy = Copy(source);
            copy.MapKeys = null;
            copy.Color = Color.White;
            layers.Add(($"{key}-dirt", copy));
        }
    }

    private void UpdateWorldSprite(Entity<ClothingDirtableComponent> ent, SpriteComponent sprite)
    {
        if (ent.Comp.DirtColor is not { } dirt)
        {
            ClearWorldLayers(ent);
            return;
        }

        if (_worldLayers.TryGetValue(ent, out var existing))
        {
            foreach (var key in existing)
                UpdateShader((ent.Owner, sprite), key, dirt);
            return;
        }

        var keys = new List<string>();
        var sourceCount = sprite.AllLayers.Count();
        for (var i = 0; i < sourceCount; i++)
        {
            if (sprite[i] is not SpriteComponent.Layer source || !source.Visible || source.Blank)
                continue;

            var key = $"{WorldLayerPrefix}{i}";
            var index = _sprite.LayerMapReserve((ent.Owner, sprite), key);
            SetMaskedLayer((ent.Owner, sprite), index, source, dirt);
            ApplyShader((ent.Owner, sprite), index, key, dirt);
            keys.Add(key);
        }
        if (keys.Count > 0)
            _worldLayers[ent] = keys;
    }

    private void SetMaskedLayer(Entity<SpriteComponent?> target, int index, SpriteComponent.Layer source, Color dirt)
    {
        var data = new PrototypeLayerData
        {
            RsiPath = source.State.IsValid ? source.ActualRsi?.Path.ToString() : null,
            State = source.State.IsValid ? source.State.Name : null,
            Scale = source.Scale,
            Rotation = source.Rotation,
            Offset = source.Offset,
            Visible = source.Visible,
            Color = Color.White,
            RenderingStrategy = source.RenderingStrategy,
            Cycle = source.Cycle,
            Loop = source.Loop,
        };
        _sprite.LayerSetData(target, index, data);
        if (!source.State.IsValid && source.Texture != null)
            _sprite.LayerSetTexture(target, index, source.Texture);
    }

    private void ClearWorldLayers(EntityUid uid)
    {
        if (!_worldLayers.Remove(uid, out var keys) || !TryComp(uid, out SpriteComponent? sprite))
            return;
        foreach (var key in keys)
        {
            RemoveShader(uid, key);
            _sprite.RemoveLayer((uid, sprite), key, false);
        }
    }

    private void ApplyShaders(EntityUid item, EntityUid target, HashSet<string> layers, Color? dirt)
    {
        if (dirt is not { } color || !TryComp(target, out SpriteComponent? sprite))
            return;
        foreach (var key in layers)
        {
            if (!key.Contains("-dirt") || !_sprite.LayerMapTryGet((target, sprite), key, out var index, false))
                continue;
            ApplyShader((target, sprite), index, key, color);
            if (!_itemShaders.TryGetValue(item, out var itemShaders))
            {
                itemShaders = [];
                _itemShaders[item] = itemShaders;
            }
            itemShaders.Add((target, key));
        }
    }

    private void ApplyShader(Entity<SpriteComponent?> target, int index, string key, Color dirt)
    {
        RemoveShader(target, key);
        var shader = _prototypes.Index<ShaderPrototype>(DirtShader).InstanceUnique();
        SetShaderParameters(shader, dirt);
        _shaders[(target, key)] = shader;
        if (target.Comp != null)
            target.Comp.LayerSetShader(index, shader, DirtShader);
    }

    private void UpdateShader(Entity<SpriteComponent?> target, string key, Color dirt)
    {
        if (_shaders.TryGetValue((target, key), out var shader))
            SetShaderParameters(shader, dirt);
        else if (_sprite.LayerMapTryGet(target, key, out var index, false))
            ApplyShader(target, index, key, dirt);
    }

    private static void SetShaderParameters(ShaderInstance shader, Color dirt)
    {
        shader.SetParameter("coverage", dirt.A);
        shader.SetParameter("dirt_color", new Vector3(dirt.R, dirt.G, dirt.B));
    }

    private void RemoveShader(EntityUid target, string key)
    {
        if (!_shaders.Remove((target, key), out var shader))
            return;

        if (TryComp(target, out SpriteComponent? sprite) &&
            _sprite.LayerMapTryGet((target, sprite), key, out var index, false) &&
            sprite[index] is SpriteComponent.Layer layer &&
            layer.Shader == shader)
            sprite.LayerSetShader(index, null, null);

        shader.Dispose();
    }

    private void ClearItemShaders(EntityUid item)
    {
        if (!_itemShaders.Remove(item, out var keys))
            return;
        foreach (var (target, key) in keys)
            RemoveShader(target, key);
    }

    private static PrototypeLayerData Copy(PrototypeLayerData source)
        => new()
        {
            Shader = source.Shader,
            TexturePath = source.TexturePath,
            RsiPath = source.RsiPath,
            State = source.State,
            Scale = source.Scale,
            Rotation = source.Rotation,
            Offset = source.Offset,
            Visible = source.Visible,
            Color = source.Color,
            RenderingStrategy = source.RenderingStrategy,
            CopyToShaderParameters = source.CopyToShaderParameters,
            Cycle = source.Cycle,
            Loop = source.Loop,
        };
}
