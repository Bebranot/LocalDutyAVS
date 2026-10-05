// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Concussion;
using Content.Shared._RMC14.Attachable.Systems;
using Content.Shared.Armor;
using Content.Shared.CCVar;
using Content.Shared.Clumsy;
using Content.Shared.Hands.Components;
using Content.Shared.Inventory;
using Content.Shared.Projectiles;
using Content.Shared.Tag;
using Content.Shared.Weapons.Hitscan.Components;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._Duty.Recoil;

/// <summary>
/// Общая часть отдачи: мощность калибра, влияние одежды и глобальный множитель разброса.
/// Камера считается на клиенте (DutyGunRecoilSystem) по коэффициентам отсюда; нарастание разброса
/// от одежды правится здесь же перед серверным выстрелом (SelfBeforeGunShotEvent).
/// </summary>
public sealed partial class SharedDutyRecoilSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IComponentFactory _factory = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SharedGunSystem _gun = default!;

    /// <summary>Лазеры почти не толкают — остаётся лёгкий импульс, чтобы выстрел ощущался.</summary>
    public const float HitscanPower = 0.3f;

    /// <summary>Метательные «стволы» (пневмопушка и т.п.) — мягкий толчок.</summary>
    public const float ThrowPower = 0.4f;

    /// <summary>Патрон, о котором ничего не известно (нет снаряда/урона).</summary>
    private const float DefaultPower = 0.5f;

    /// <summary>
    /// Мощность по урону: sqrt(урон / ReferenceDamage). 20 урона ≈ 7.62x39 = 1.0. Корень — чтобы
    /// мощные экзотические снаряды не давали кратного рывка. Пределы — чтобы ни игрушка, ни
    /// какой-нибудь админский снаряд на 500 урона не выбивались из шкалы таблицы.
    /// </summary>
    private const float ReferenceDamage = 20f;
    private const float MinDerivedPower = 0.3f;
    private const float MaxDerivedPower = 1.8f;

    private const string OuterSlot = "outerClothing";
    private const string HeadSlot = "head";

    /// <summary>
    /// Нарастание разброса от калибра: 0.75 + 0.3 × мощность в пределах [0.85; 1.4]. 9x18 ≈ 0.88,
    /// 5.45 ≈ 0.96, 7.62x39 ≈ 1.05, 7.62x54R ≈ 1.15, 12.7x108 — 1.4: в очереди крупный калибр
    /// задирает ствол сильнее. Мягко — у стволов свой angleIncrease, он уже частично учитывает патрон.
    /// </summary>
    private const float SpreadCaliberBase = 0.75f;
    private const float SpreadCaliberPerPower = 0.3f;
    private const float SpreadCaliberMin = 0.85f;
    private const float SpreadCaliberMax = 1.4f;

    /// <summary>Нижняя граница угла: GetRecoilAngle на сервере падает на assert при отрицательных углах.</summary>
    private const double MinTheta = 0.001;

    private readonly Dictionary<ProtoId<TagPrototype>, float> _tagPower = new();
    private readonly Dictionary<string, float> _powerCache = new();

    private float _spreadMultiplier = 1f;

    public override void Initialize()
    {
        base.Initialize();

        // После вычитающих модификаторов (бонус хвата, обвесы RMC): множитель тогда режет итоговый
        // разброс пропорционально и не загоняет углы в минус раньше, чем их вычтут.
        SubscribeLocalEvent<GunComponent, GunRefreshModifiersEvent>(OnGunRefreshModifiers,
            after: new[] { typeof(SharedWieldableSystem), typeof(AttachableHolderSystem) });

        // Поднимается только сервером (перед расчётом угла в GunSystem.Shoot) — на клиенте подписка
        // молчит. После тех, кто может отменить выстрел: отменённый выстрел не должен копить разброс.
        SubscribeLocalEvent<HandsComponent, SelfBeforeGunShotEvent>(OnBeforeGunShot,
            after: new[] { typeof(ClumsySystem), typeof(InventorySystem) });

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        Subs.CVar(_cfg, DutyCCVars.GunSpreadMultiplier, OnSpreadMultiplierChanged, true);

        RebuildTagTable();
    }

    #region Калибр

    /// <summary>
    /// Мощность отдачи выстрела: тег калибра из таблицы → иначе по урону пули × число дробин.
    /// Кэшируется по прототипу патрона — считается один раз, а не на каждый выстрел.
    /// </summary>
    public float GetCaliberPower(EntityUid? ammo, IShootable? shootable)
    {
        if (shootable is HitscanAmmoComponent)
            return HitscanPower;

        if (ammo is not { } uid || MetaData(uid).EntityPrototype is not { } proto)
            return DefaultPower;

        if (_powerCache.TryGetValue(proto.ID, out var cached))
            return cached;

        var power = ComputePower(proto);
        _powerCache[proto.ID] = power;
        return power;
    }

    private float ComputePower(EntityPrototype proto)
    {
        // Несколько тегов из таблицы бывает у подмножеств (лёгкая дробь носит и ShellShotgun, и
        // ShellShotgunLight) — берём меньший: подмножество всегда «слабее» общего калибра, а порядок
        // обхода HashSet не определён.
        if (proto.TryGetComponent<TagComponent>(out var tags, _factory))
        {
            var found = float.MaxValue;
            foreach (var tag in tags.Tags)
            {
                if (_tagPower.TryGetValue(tag, out var tagged))
                    found = Math.Min(found, tagged);
            }

            if (found < float.MaxValue)
                return found;
        }

        // Патрон стреляет другим прототипом (пулей), «новые» боеприпасы — сами себя.
        var bullet = proto;
        if (proto.TryGetComponent<CartridgeAmmoComponent>(out var cartridge, _factory) &&
            !_proto.TryIndex(cartridge.Prototype, out bullet))
        {
            return DefaultPower;
        }

        if (!bullet.TryGetComponent<ProjectileComponent>(out var projectile, _factory))
            return DefaultPower;

        var count = 1;
        if (bullet.TryGetComponent<ProjectileSpreadComponent>(out var spread, _factory))
            count = Math.Max(spread.Count, 1);

        var damage = (float) projectile.Damage.GetTotal() * count;
        if (damage <= 0f)
            return MinDerivedPower;

        return Math.Clamp(MathF.Sqrt(damage / ReferenceDamage), MinDerivedPower, MaxDerivedPower);
    }

    private void RebuildTagTable()
    {
        _tagPower.Clear();
        _powerCache.Clear();

        foreach (var proto in _proto.EnumeratePrototypes<DutyCaliberRecoilPrototype>())
        {
            _tagPower[proto.Tag] = proto.Power;
        }
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<DutyCaliberRecoilPrototype>() || args.WasModified<EntityPrototype>())
            RebuildTagTable();
    }

    #endregion

    #region Одежда

    /// <summary>
    /// Что надето из «гасящего» отдачу. Логика нарочито игровая: броня и шлем упираются в плечо/держат
    /// голову — отдача меньше; без них — больше.
    /// </summary>
    public (bool Armor, bool Helmet) GetWear(EntityUid user)
    {
        var armor = _inventory.TryGetSlotEntity(user, OuterSlot, out var outer) && IsPhysicalArmor(outer.Value);

        var helmet = _inventory.TryGetSlotEntity(user, HeadSlot, out var head) &&
                     (HasComp<ConcussionProtectionComponent>(head) || IsPhysicalArmor(head.Value));

        return (armor, helmet);
    }

    /// <summary>Множитель отдачи камеры от одежды. Без инвентаря (борги, животные) — нейтрально.</summary>
    public float GetCameraWearMultiplier(EntityUid user)
    {
        if (!HasComp<InventoryComponent>(user))
            return 1f;

        return GetWear(user) switch
        {
            (true, true) => 0.8f,
            (true, false) => 0.95f,
            (false, true) => 1f,
            _ => 1.15f,
        };
    }

    /// <summary>Множитель нарастания разброса от одежды — «совсем немного», заметно только в очереди.</summary>
    public float GetSpreadWearMultiplier(EntityUid user)
    {
        if (!HasComp<InventoryComponent>(user))
            return 1f;

        return GetWear(user) switch
        {
            (true, true) => 0.92f,
            (true, false) => 0.98f,
            (false, true) => 1f,
            _ => 1.08f,
        };
    }

    /// <summary>
    /// Броня — только та, что держит пулю/удар. Халат с защитой от кислоты или шуба
    /// с защитой от холода бронёй не считаются.
    /// </summary>
    private bool IsPhysicalArmor(EntityUid item)
    {
        if (!TryComp<ArmorComponent>(item, out var armor))
            return false;

        var modifiers = armor.Modifiers;
        return modifiers.Coefficients.GetValueOrDefault("Piercing", 1f) < 1f ||
               modifiers.Coefficients.GetValueOrDefault("Blunt", 1f) < 1f ||
               modifiers.FlatReduction.GetValueOrDefault("Piercing", 0f) > 0f;
    }

    #endregion

    #region Разброс

    private void OnGunRefreshModifiers(Entity<GunComponent> ent, ref GunRefreshModifiersEvent args)
    {
        var mult = _spreadMultiplier;

        var min = Math.Max(args.MinAngle.Theta * mult, MinTheta);
        var max = Math.Max(args.MaxAngle.Theta * mult, min);

        args.MinAngle = new Angle(min);
        args.MaxAngle = new Angle(max);
        args.AngleIncrease = new Angle(Math.Max(args.AngleIncrease.Theta * mult, 0));
    }

    /// <summary>
    /// Калибр и одежда меняют нарастание разброса. Разброс задаётся стволу (RefreshModifiers), а патрон
    /// и одежда известны только в момент выстрела, поэтому правим накопленный угол прямо перед ним:
    /// GetRecoilAngle считает Current + Increase, и добавка (k − 1)·Increase к Current в точности равна
    /// умножению прироста этого выстрела на k (итог всё равно клэмпится в [Min; Max]).
    /// </summary>
    private void OnBeforeGunShot(Entity<HandsComponent> ent, ref SelfBeforeGunShotEvent args)
    {
        if (args.Cancelled)
            return;

        var k = GetSpreadWearMultiplier(ent);

        if (args.Ammo.Count > 0)
        {
            var (ammo, shootable) = args.Ammo[0];
            var power = GetCaliberPower(ammo, shootable);
            k *= Math.Clamp(SpreadCaliberBase + SpreadCaliberPerPower * power, SpreadCaliberMin, SpreadCaliberMax);
        }

        if (MathHelper.CloseTo(k, 1f))
            return;

        var gun = args.Gun.Comp;
        gun.CurrentAngle = new Angle(gun.CurrentAngle.Theta + (k - 1f) * gun.AngleIncreaseModified.Theta);
    }

    private void OnSpreadMultiplierChanged(float value)
    {
        var clamped = Math.Clamp(value, 0.05f, 3f);
        if (MathHelper.CloseTo(clamped, _spreadMultiplier))
            return;

        _spreadMultiplier = clamped;

        // Смена на живом сервере: пересчитать уже существующие стволы. Клиент получит новые
        // значения состоянием компонента — у себя пересчитывать не нужно.
        if (!_net.IsServer)
            return;

        var query = EntityQueryEnumerator<GunComponent>();
        while (query.MoveNext(out var uid, out var gun))
        {
            _gun.RefreshModifiers((uid, gun));
        }
    }

    #endregion
}
