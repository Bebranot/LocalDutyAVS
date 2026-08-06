using System.Linq;
using Content.Shared._Duty.Parry;
using Content.Shared._Duty.Parry.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Stunnable;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Duty.Parry;

/// <summary>
/// QTE-дуэль: катсцена из трёх этапов, запускаемая успешным парированием контр-атаки.
/// Сервер — единственный судья: клиент присылает только факт нажатия, а какая клавиша была
/// нужна, уложился ли игрок в окно и чей клик на этапе 3 точнее — решается здесь.
/// </summary>
public sealed class QteDuelSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedContentEyeSystem _eye = default!;

    /// <summary>Зум камеры на время катсцены. Меньше единицы — камера ближе к бойцам.</summary>
    private static readonly System.Numerics.Vector2 CutsceneZoom = new(0.55f, 0.55f);

    private static readonly EntProtoId BarrierProto = "DutyQteBarrier";
    private static readonly ProtoId<SoundCollectionPrototype> MusicCollection = "DutyQteSong";
    private static readonly SoundSpecifier ParrySound = new SoundPathSpecifier("/Audio/_Duty/WIP/parry.ogg");

    private static readonly QtePromptKey[] DirectionKeys = [QtePromptKey.W, QtePromptKey.A, QtePromptKey.S, QtePromptKey.D];
    private static readonly QtePromptKey[] LetterKeys =
        [QtePromptKey.Q, QtePromptKey.T, QtePromptKey.E, QtePromptKey.R, QtePromptKey.G, QtePromptKey.F, QtePromptKey.H];

    private const int DirectionPromptCount = 6;
    private const int LetterPromptMin = 4;
    private const int LetterPromptMax = 6;

    /// <summary>Окно на одну подсказку — случайное в этих границах, чтобы ритм не заучивался.</summary>
    private static readonly TimeSpan PromptWindowMin = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan PromptWindowMax = TimeSpan.FromSeconds(0.8);

    /// <summary>Сколько сжимается шкала этапа 3 до идеального момента.</summary>
    private static readonly TimeSpan FinalWindup = TimeSpan.FromSeconds(1.2);

    /// <summary>Сколько ещё принимается клик после идеального момента, прежде чем этап провален.</summary>
    private static readonly TimeSpan FinalGrace = TimeSpan.FromSeconds(0.4);

    /// <summary>Полуширина идеальной зоны: клик с отклонением меньше этого считается попаданием.</summary>
    private const float PerfectWindowSeconds = 0.15f;

    private static readonly TimeSpan WatchdogTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan WinnerStun = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MutualStun = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan InterruptStun = TimeSpan.FromSeconds(1);
    private const float WinnerDamage = 50f;
    private const float MutualDamage = 25f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<QteDuelStartRequestEvent>(OnDuelStartRequest);

        SubscribeAllEvent<QtePromptInputEvent>(OnPromptInput);
        SubscribeAllEvent<QteFinalInputEvent>(OnFinalInput);

        // Ближний бой посторонних по участникам гасится (барьер — первый рубеж, это второй),
        // дальнобойное попадание проходит и прерывает сцену.
        SubscribeLocalEvent<QteParticipantComponent, AttackedEvent>(OnParticipantAttacked);
        SubscribeLocalEvent<QteParticipantComponent, DamageModifyEvent>(OnParticipantDamageModify);
        SubscribeLocalEvent<QteParticipantComponent, DamageDealtEvent>(OnParticipantDamaged);

        // Если участник умер/удалился посреди сцены — не оставляем висеть барьер и лок ввода.
        SubscribeLocalEvent<QteParticipantComponent, EntityTerminatingEvent>(OnParticipantTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<QteDuelComponent>();

        while (query.MoveNext(out var duelUid, out var duel))
        {
            if (now >= duel.Watchdog)
            {
                TeardownDuel(duelUid, duel);
                continue;
            }

            if (!ParticipantsAlive(duel))
            {
                TeardownDuel(duelUid, duel);
                continue;
            }

            switch (duel.Stage)
            {
                case QteStage.Directions:
                case QteStage.Letters:
                    UpdatePromptStage(duelUid, duel, now);
                    break;

                case QteStage.Final:
                    UpdateFinalStage(duelUid, duel, now);
                    break;
            }
        }
    }

    // ── Запуск ────────────────────────────────────────────────

    private void OnDuelStartRequest(ref QteDuelStartRequestEvent args)
    {
        // Ни один из участников не должен уже быть в дуэли (например, двойной удар в один тик).
        if (HasComp<QteParticipantComponent>(args.Blocker) || HasComp<QteParticipantComponent>(args.Parrier))
            return;

        var duelUid = Spawn(null, MapCoordinates.Nullspace);
        var duel = AddComp<QteDuelComponent>(duelUid);

        duel.Blocker.Entity = args.Blocker;
        duel.Parrier.Entity = args.Parrier;
        duel.Stage = QteStage.Directions;
        duel.Watchdog = _timing.CurTime + WatchdogTimeout;

        var musicTrack = PickMusicTrack();

        SetupParticipant(duelUid, duel.Blocker, args.Parrier, musicTrack);
        SetupParticipant(duelUid, duel.Parrier, args.Blocker, musicTrack);

        SpawnBarriers(duel);

        // Звук успешного парирования — он же сигнал начала катсцены.
        _audio.PlayPvs(ParrySound, args.Parrier);

        StartPromptStage(duel, QteStage.Directions);
    }

    private void SetupParticipant(EntityUid duelUid, QteDuelSide side, EntityUid opponent, int musicTrack)
    {
        var participant = EnsureComp<QteParticipantComponent>(side.Entity);
        participant.Duel = duelUid;
        participant.Opponent = opponent;
        participant.MusicTrack = musicTrack;
        participant.Stage = QteStage.Directions;
        participant.Hits = 0;
        participant.FinalAnswered = false;
        Dirty(side.Entity, participant);

        EnsureComp<QteInputLockComponent>(side.Entity);

        // Зум выставляется сервером прямо на ContentEyeComponent (поле сетевое) — клиент
        // подхватывает его сам и плавно доводит камеру. Через клиентский request-путь нельзя:
        // он требует админ-флага.
        _eye.SetZoom(side.Entity, CutsceneZoom, ignoreLimits: true);
    }

    private int PickMusicTrack()
    {
        if (!_proto.TryIndex(MusicCollection, out var collection) || collection.PickFiles.Count == 0)
            return 0;

        return _random.Next(collection.PickFiles.Count);
    }

    /// <summary>
    /// Кольцо барьера вокруг обоих участников. Барьер не пускает посторонних мобов вплотную,
    /// но прозрачен для снарядов — прервать сцену можно только дальнобойным попаданием.
    /// </summary>
    private void SpawnBarriers(QteDuelComponent duel)
    {
        var blockerXform = Transform(duel.Blocker.Entity);

        if (blockerXform.MapID == MapId.Nullspace)
            return;

        var center = _transform.GetMapCoordinates(duel.Blocker.Entity);
        var opponent = _transform.GetMapCoordinates(duel.Parrier.Entity);

        if (center.MapId != opponent.MapId)
            return;

        var mid = new MapCoordinates((center.Position + opponent.Position) / 2f, center.MapId);

        // Квадратное кольцо 5x5 вокруг середины: внутри помещаются оба бойца, снаружи никто не пройдёт.
        const int radius = 2;
        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
            {
                if (Math.Abs(x) != radius && Math.Abs(y) != radius)
                    continue; // только периметр

                var pos = new MapCoordinates(mid.Position + new System.Numerics.Vector2(x, y), mid.MapId);
                duel.Barriers.Add(Spawn(BarrierProto, pos));
            }
        }
    }

    // ── Этапы 1-2 ─────────────────────────────────────────────

    private void StartPromptStage(QteDuelComponent duel, QteStage stage)
    {
        duel.Stage = stage;

        foreach (var side in Sides(duel))
        {
            side.Sequence = BuildSequence(stage);
            side.Index = 0;
            ShowPrompt(duel, side);
        }
    }

    private List<QtePromptKey> BuildSequence(QteStage stage)
    {
        var pool = stage == QteStage.Directions ? DirectionKeys : LetterKeys;
        var count = stage == QteStage.Directions
            ? DirectionPromptCount
            : _random.Next(LetterPromptMin, LetterPromptMax + 1);

        var sequence = new List<QtePromptKey>(count);
        for (var i = 0; i < count; i++)
        {
            sequence.Add(_random.Pick(pool));
        }

        return sequence;
    }

    private void ShowPrompt(QteDuelComponent duel, QteDuelSide side)
    {
        if (!TryComp<QteParticipantComponent>(side.Entity, out var participant))
            return;

        var now = _timing.CurTime;
        var window = TimeSpan.FromSeconds(_random.NextFloat(
            (float) PromptWindowMin.TotalSeconds,
            (float) PromptWindowMax.TotalSeconds));

        participant.Stage = duel.Stage;
        participant.CurrentPrompt = side.Sequence[side.Index];
        participant.PromptStart = now;
        participant.PromptEnd = now + window;
        participant.PromptIndex = side.Index;
        participant.PromptTotal = side.Sequence.Count;
        participant.Hits = side.Hits;
        Dirty(side.Entity, participant);
    }

    private void UpdatePromptStage(EntityUid duelUid, QteDuelComponent duel, TimeSpan now)
    {
        foreach (var side in Sides(duel))
        {
            if (side.Index >= side.Sequence.Count)
                continue; // эта сторона свою последовательность уже прошла

            if (!TryComp<QteParticipantComponent>(side.Entity, out var participant))
                continue;

            if (now < participant.PromptEnd)
                continue;

            // Не успел — подсказка просто не засчитывается, этап не проваливается.
            AdvancePrompt(duel, side);
        }

        if (Sides(duel).All(s => s.Index >= s.Sequence.Count))
        {
            if (duel.Stage == QteStage.Directions)
                StartPromptStage(duel, QteStage.Letters);
            else
                StartFinalStage(duel);
        }
    }

    private void AdvancePrompt(QteDuelComponent duel, QteDuelSide side)
    {
        side.Index++;

        if (side.Index < side.Sequence.Count)
        {
            ShowPrompt(duel, side);
            return;
        }

        // Последовательность пройдена — ждём соперника, подсказку прячем.
        if (!TryComp<QteParticipantComponent>(side.Entity, out var participant))
            return;

        participant.CurrentPrompt = QtePromptKey.None;
        participant.Hits = side.Hits;
        Dirty(side.Entity, participant);
    }

    private void OnPromptInput(QtePromptInputEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } uid)
            return;

        if (!TryGetSide(uid, out var duel, out var side))
            return;

        if (duel.Stage is not (QteStage.Directions or QteStage.Letters))
            return;

        if (side.Index >= side.Sequence.Count)
            return;

        if (!TryComp<QteParticipantComponent>(uid, out var participant))
            return;

        var now = _timing.CurTime;

        // Окно уже истекло — засчитывать нечего, тик Update сам перейдёт к следующей подсказке.
        if (now > participant.PromptEnd)
            return;

        // Не та клавиша — нажатие просто не засчитывается, но и не проваливает подсказку:
        // у игрока остаётся время в текущем окне попробовать ещё раз.
        if (msg.Key != side.Sequence[side.Index])
            return;

        side.Hits++;
        AdvancePrompt(duel, side);
    }

    // ── Этап 3 ────────────────────────────────────────────────

    private void StartFinalStage(QteDuelComponent duel)
    {
        duel.Stage = QteStage.Final;

        var now = _timing.CurTime;
        var perfect = now + FinalWindup;
        var deadline = perfect + FinalGrace;

        foreach (var side in Sides(duel))
        {
            side.FinalAnswered = false;
            side.FinalHit = false;
            side.FinalError = float.MaxValue;

            if (!TryComp<QteParticipantComponent>(side.Entity, out var participant))
                continue;

            participant.Stage = QteStage.Final;
            participant.CurrentPrompt = QtePromptKey.None;
            participant.FinalStart = now;
            participant.FinalPerfect = perfect;
            participant.FinalDeadline = deadline;
            participant.FinalAnswered = false;
            participant.Hits = side.Hits;
            Dirty(side.Entity, participant);
        }
    }

    private void OnFinalInput(QteFinalInputEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } uid)
            return;

        if (!TryGetSide(uid, out var duel, out var side))
            return;

        if (duel.Stage != QteStage.Final || side.FinalAnswered)
            return;

        if (!TryComp<QteParticipantComponent>(uid, out var participant))
            return;

        // Компенсация пинга: сервер судит по своему времени получения, но вычитает половину
        // собственного замера задержки игрока. Клиент тайминг не присылает и подделать его не может.
        var latency = TimeSpan.FromMilliseconds(args.SenderSession.Ping / 2.0);
        var clickTime = _timing.CurTime - latency;

        side.FinalAnswered = true;
        side.FinalError = (float) Math.Abs((clickTime - participant.FinalPerfect).TotalSeconds);
        side.FinalHit = side.FinalError <= PerfectWindowSeconds;

        participant.FinalAnswered = true;
        Dirty(uid, participant);
    }

    private void UpdateFinalStage(EntityUid duelUid, QteDuelComponent duel, TimeSpan now)
    {
        var bothAnswered = Sides(duel).All(s => s.FinalAnswered);

        if (!bothAnswered)
        {
            if (!TryComp<QteParticipantComponent>(duel.Blocker.Entity, out var anyParticipant))
            {
                TeardownDuel(duelUid, duel);
                return;
            }

            if (now < anyParticipant.FinalDeadline)
                return; // ещё можно кликнуть
        }

        ResolveDuel(duelUid, duel);
    }

    private void ResolveDuel(EntityUid duelUid, QteDuelComponent duel)
    {
        var blocker = duel.Blocker;
        var parrier = duel.Parrier;

        QteDuelSide? winner = null;

        if (blocker.FinalHit && parrier.FinalHit)
        {
            // Оба попали — решает точность, при точном равенстве тай-брейкер этапов 1-2.
            if (Math.Abs(blocker.FinalError - parrier.FinalError) > float.Epsilon)
                winner = blocker.FinalError < parrier.FinalError ? blocker : parrier;
            else if (blocker.Hits != parrier.Hits)
                winner = blocker.Hits > parrier.Hits ? blocker : parrier;
            // Иначе абсолютная ничья — считаем, что никто не был лучше (обоюдный размен ниже).
        }
        else if (blocker.FinalHit)
        {
            winner = blocker;
        }
        else if (parrier.FinalHit)
        {
            winner = parrier;
        }

        if (winner == null)
        {
            // Оба промазали мимо идеальной зоны (или абсолютная ничья) — обоюдный размен.
            foreach (var side in Sides(duel))
            {
                ApplyOutcome(side.Entity, MutualDamage, MutualStun);
            }
        }
        else
        {
            var loser = winner == blocker ? parrier : blocker;
            ApplyOutcome(loser.Entity, WinnerDamage, WinnerStun);
        }

        TeardownDuel(duelUid, duel);
    }

    private void ApplyOutcome(EntityUid uid, float bluntDamage, TimeSpan stun)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var damage = new DamageSpecifier();
        damage.DamageDict.Add("Blunt", bluntDamage);
        _damageable.TryChangeDamage(uid, damage);

        _stun.TryUpdateStunDuration(uid, stun);
    }

    // ── Прерывание и демонтаж ─────────────────────────────────

    /// <summary>
    /// Ближний бой всегда предваряется AttackedEvent на жертве — помечаем следующий
    /// DamageModifyEvent как «этот урон от ближнего», чтобы отличить его от выстрела.
    /// </summary>
    private void OnParticipantAttacked(EntityUid uid, QteParticipantComponent component, AttackedEvent args)
    {
        component.PendingMeleeAttacker = args.User;
    }

    private void OnParticipantDamageModify(EntityUid uid, QteParticipantComponent component, DamageModifyEvent args)
    {
        if (component.PendingMeleeAttacker is not { } attacker || args.Origin != attacker)
            return;

        component.PendingMeleeAttacker = null;

        // Иммунитет к ближнему урону на время сцены — второй рубеж поверх физического барьера
        // (на случай оружия с большим радиусом, дотягивающегося через стенку).
        // Пустой DamageSpecifier обрывает ChangeDamage до DamageDealtEvent, поэтому такой
        // удар заодно и не прервёт сцену.
        args.Damage = new DamageSpecifier();
    }

    /// <summary>
    /// Сюда доходит только урон, переживший иммунитет — то есть дальнобойное попадание.
    /// Промах или выстрел в барьер урона не наносят и сцену не трогают.
    /// </summary>
    private void OnParticipantDamaged(EntityUid uid, QteParticipantComponent component, ref DamageDealtEvent args)
    {
        if (args.Damage.GetTotal() <= 0)
            return;

        if (!TryGetDuel(uid, out var duelUid, out var duel))
            return;

        foreach (var side in Sides(duel))
        {
            _stun.TryUpdateStunDuration(side.Entity, InterruptStun);
        }

        TeardownDuel(duelUid, duel);
    }

    private void OnParticipantTerminating(EntityUid uid, QteParticipantComponent component, ref EntityTerminatingEvent args)
    {
        if (TryGetDuel(uid, out var duelUid, out var duel))
            TeardownDuel(duelUid, duel);
    }

    private void TeardownDuel(EntityUid duelUid, QteDuelComponent duel)
    {
        foreach (var side in Sides(duel))
        {
            if (TerminatingOrDeleted(side.Entity))
                continue;

            // Клиент видит уход QteParticipantComponent и сам гасит виньетку/HUD/музыку.
            _eye.ResetZoom(side.Entity);
            RemComp<QteParticipantComponent>(side.Entity);
            RemComp<QteInputLockComponent>(side.Entity);
        }

        foreach (var barrier in duel.Barriers)
        {
            QueueDel(barrier);
        }

        duel.Barriers.Clear();
        QueueDel(duelUid);
    }

    // ── Вспомогательное ───────────────────────────────────────

    private static QteDuelSide[] Sides(QteDuelComponent duel) => [duel.Blocker, duel.Parrier];

    private bool ParticipantsAlive(QteDuelComponent duel)
    {
        return Sides(duel).All(s => !TerminatingOrDeleted(s.Entity) && HasComp<QteParticipantComponent>(s.Entity));
    }

    private bool TryGetDuel(EntityUid uid, out EntityUid duelUid, out QteDuelComponent duel)
    {
        duelUid = default;
        duel = default!;

        if (!TryComp<QteParticipantComponent>(uid, out var participant))
            return false;

        if (!TryComp<QteDuelComponent>(participant.Duel, out var duelComp))
            return false;

        duelUid = participant.Duel;
        duel = duelComp;
        return true;
    }

    private bool TryGetSide(EntityUid uid, out QteDuelComponent duel, out QteDuelSide side)
    {
        side = default!;

        if (!TryGetDuel(uid, out _, out duel!))
            return false;

        side = duel.Blocker.Entity == uid ? duel.Blocker : duel.Parrier;
        return side.Entity == uid;
    }
}
