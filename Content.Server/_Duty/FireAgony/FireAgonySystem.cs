using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Chat.Systems;
using Content.Shared._Duty.FireAgony;
using Content.Shared.Atmos.Components;
using Content.Shared.Chat;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.FireAgony;

/// <summary>
/// _Duty: серверная машина состояний «Агонии от огня». Когда игровой гуманоид с
/// <see cref="FireAgonyComponent"/> горит выше порога — запускает принудительную сцену:
/// paralyze (блок ввода + падение + дроп вещей встроенно), авто-тушение, периодические крики
/// (ИС-чат + сетевой сигнал звука клиенту) и попапы, приближение камеры. Сцена рвётся при
/// потухании / <see cref="FireAgonyComponent.SafetyTimeout"/> / крите-смерти.
/// </summary>
public sealed class FireAgonySystem : EntitySystem
{
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly FlammableSystem _flammable = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly SharedContentEyeSystem _eye = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private EntityQuery<FlammableComponent> _flammableQuery;

    /// <summary>Как часто крутится машина состояний (сек). Достаточно грубого шага под тик огня.</summary>
    private const float UpdateInterval = 1f;

    /// <summary>Длительность каждого рефреша паралича — заведомо больше шага, чтобы не лапснуть.</summary>
    private static readonly TimeSpan ParalyzeRefresh = TimeSpan.FromSeconds(2.5);

    /// <summary>Короткий паралич на выходе — чтобы персонаж быстро встал, но не мгновенно дёрнулся.</summary>
    private static readonly TimeSpan ParalyzeRelease = TimeSpan.FromSeconds(0.15);

    /// <summary>Разброс вещей из рук при падении: дистанция (тайлы) и скорость броска.</summary>
    private const float ScatterMinDistance = 1.0f;
    private const float ScatterMaxDistance = 1.5f;
    private const float ScatterThrowSpeed = 4f;

    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();

        _flammableQuery = GetEntityQuery<FlammableComponent>();
        SubscribeLocalEvent<FireAgonyComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator += frameTime;
        if (_accumulator < UpdateInterval)
            return;

        var dt = _accumulator;
        _accumulator = 0f;

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<FireAgonyComponent>();
        while (query.MoveNext(out var uid, out var agony))
        {
            if (!_flammableQuery.TryComp(uid, out var flammable))
                continue;

            if (agony.Active)
                HandleActive(uid, agony, flammable, dt, curTime);
            else
                TryStart(uid, agony, flammable, curTime);
        }
    }

    private void TryStart(EntityUid uid, FireAgonyComponent agony, FlammableComponent flammable, TimeSpan curTime)
    {
        // Только горящие игровые (player-controlled) гуманоиды выше порога; NPC горят по-старому.
        if (!flammable.OnFire || flammable.FireStacks < agony.EnterThreshold || !HasComp<ActorComponent>(uid))
            return;

        // Не в крите/мёртвых (иначе паралич трупа) и не в рефрактере после прошлого обрыва.
        if (_mobState.IsIncapacitated(uid) || curTime < agony.NextAllowedStart)
            return;

        agony.Active = true;
        agony.StartTime = curTime;
        Dirty(uid, agony);

        // Роняем вещи из рук В СТОРОНУ с силой (встроенный дроп паралича кидает по вектору скорости
        // носителя, а он стоит на месте → вещи падали под ноги). Делаем ДО паралича, чтобы его
        // собственный DropHandItemsEvent нашёл уже пустые руки.
        ScatterHands(uid);

        // Паралич: блокирует ввод, роняет лежачий спрайт.
        _stun.TryUpdateParalyzeDuration(uid, ParalyzeRefresh);

        // Камера приближается (сетевой предсказанный зум, ignoreLimits — ближе нормального предела).
        _eye.SetZoom(uid, agony.SceneZoom, ignoreLimits: true);

        // Позиционный зацикленный крик для окружающих (сам игрок слышит свой клиентский поток).
        agony.ScreamStream = _audio.PlayEntity(
            agony.ScreamSound,
            Filter.Pvs(uid).RemovePlayerByAttachedEntity(uid),
            uid,
            false,
            AudioParams.Default.WithLoop(true).WithVolume(agony.BystanderScreamVolumeDb))?.Entity;

        // Первый эмоут и попап — сразу, чтобы сцена читалась мгновенно, а не после случайной паузы.
        // (Звук крика — отдельный непрерывный поток на клиенте, стартует по флагу Active.)
        SendAgonyEmote(uid, agony);
        ShowAgonyPopup(uid, agony);
        ScheduleEmote(agony, curTime);
        SchedulePopup(agony, curTime);
    }

    private void HandleActive(EntityUid uid, FireAgonyComponent agony, FlammableComponent flammable, float dt, TimeSpan curTime)
    {
        // Удерживаем паралич, пока сцена идёт.
        _stun.TryUpdateParalyzeDuration(uid, ParalyzeRefresh);

        // Авто-тушение «сбиванием пламени» — вместо старого ускоренного Resist, без участия игрока.
        _flammable.AdjustFireStacks(uid, agony.ExtinguishRatePerSecond * dt, flammable);

        // Основной выход: огонь потух — но не раньше минимальной длительности, иначе кратковременный
        // всплеск firestacks дал бы «мигнул и потух».
        if ((!flammable.OnFire || flammable.FireStacks <= 0f)
            && curTime - agony.StartTime >= agony.MinSceneDuration)
        {
            EndScene(uid, agony);
            return;
        }

        // Safety-cap: непотухающий огонь не должен запирать игрока навсегда.
        if (curTime - agony.StartTime >= agony.SafetyTimeout)
        {
            EndScene(uid, agony);
            return;
        }

        if (curTime >= agony.NextScreamTime)
        {
            SendAgonyEmote(uid, agony);
            ScheduleEmote(agony, curTime);
        }

        if (curTime >= agony.NextPopupTime)
        {
            ShowAgonyPopup(uid, agony);
            SchedulePopup(agony, curTime);
        }
    }

    private void OnMobStateChanged(Entity<FireAgonyComponent> ent, ref MobStateChangedEvent args)
    {
        if (!ent.Comp.Active)
            return;

        // При крите/смерти stun снимается движком сам; нам остаётся оборвать сцену (флаг + камера).
        if (args.NewMobState is MobState.Critical or MobState.Dead)
            EndScene(ent, ent.Comp, releaseParalyze: false);
    }

    private void EndScene(EntityUid uid, FireAgonyComponent agony, bool releaseParalyze = true)
    {
        agony.Active = false;
        agony.NextAllowedStart = _timing.CurTime + agony.RearmDelay;
        Dirty(uid, agony);

        // Обрываем позиционный крик для окружающих (клиентский крик игрока гаснет по флагу Active).
        if (agony.ScreamStream != null)
        {
            _audio.Stop(agony.ScreamStream);
            agony.ScreamStream = null;
        }

        _eye.ResetZoom(uid);

        // Укорачиваем паралич, чтобы персонаж поднялся сам (autoStand по истечении стана).
        if (releaseParalyze)
            _stun.TryUpdateParalyzeDuration(uid, ParalyzeRelease);
    }

    /// <summary>Роняет предметы из ОБЕИХ рук и кидает каждый в случайную сторону на 1.0–1.5 тайла.</summary>
    private void ScatterHands(EntityUid uid)
    {
        if (!TryComp<HandsComponent>(uid, out var hands))
            return;

        // ToArray: TryDrop мутирует состояние рук по ходу перебора.
        foreach (var handId in hands.Hands.Keys.ToArray())
        {
            if (!_hands.TryGetHeldItem((uid, hands), handId, out var item))
                continue;

            if (!_hands.TryDrop((uid, hands), handId, checkActionBlocker: false))
                continue;

            var direction = _random.NextAngle().ToVec() * _random.NextFloat(ScatterMinDistance, ScatterMaxDistance);
            _throwing.TryThrow(item.Value, direction, ScatterThrowSpeed, uid, doSpin: true);
        }
    }

    private void SendAgonyEmote(EntityUid uid, FireAgonyComponent agony)
    {
        // Только текстовый эмоут в ИС-чат (сервер-авторитетно, минуя блок стана). Звук крика —
        // отдельный непрерывный поток на клиенте (стартует при падении, гаснет на конце сцены).
        var line = Loc.GetString($"fire-agony-scream-{_random.Next(1, agony.ScreamLineCount + 1)}");
        _chat.TrySendInGameICMessage(uid, line, InGameICChatType.Emote, hideChat: false, hideLog: true, ignoreActionBlocker: true);
    }

    private void ShowAgonyPopup(EntityUid uid, FireAgonyComponent agony)
    {
        var msg = Loc.GetString($"fire-agony-popup-{_random.Next(1, agony.PopupLineCount + 1)}");
        _popup.PopupEntity(msg, uid, uid, PopupType.LargeCaution);
    }

    private void ScheduleEmote(FireAgonyComponent agony, TimeSpan curTime)
    {
        agony.NextScreamTime = curTime + RandomInterval(agony.ScreamIntervalMin, agony.ScreamIntervalMax);
    }

    private void SchedulePopup(FireAgonyComponent agony, TimeSpan curTime)
    {
        agony.NextPopupTime = curTime + RandomInterval(agony.PopupIntervalMin, agony.PopupIntervalMax);
    }

    private TimeSpan RandomInterval(TimeSpan min, TimeSpan max)
    {
        if (max <= min)
            return min;

        return TimeSpan.FromSeconds(_random.NextDouble(min.TotalSeconds, max.TotalSeconds));
    }
}
