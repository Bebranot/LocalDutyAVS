// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client._Duty.Movement;
using Content.Shared._Duty.Movement;
using Content.Shared._Duty.Slide;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Player;

namespace Content.Client._Duty.Slide;

/// <summary>
/// _Duty: клиентская часть подката — решение по R у себя и пыль из-под скользящего.
/// </summary>
public sealed partial class DutySlideSystem : SharedDutySlideSystem
{
    [Dependency] private AnimationPlayerSystem _animation = default!;

    /// <summary>Как часто сыпать мелкую пыль во время скольжения.</summary>
    private static readonly TimeSpan DustInterval = TimeSpan.FromSeconds(0.07);

    /// <summary>Ниже этой скорости (тайл/с) пыль из-под скользящего уже не летит.</summary>
    private const float DustMinSpeed = 2f;

    private readonly Dictionary<EntityUid, TimeSpan> _nextDust = new();
    private readonly List<EntityUid> _finished = new();

    /// <summary>
    /// Клиент не может поглотить R и одновременно отправить её на сервер: движок не шлёт то, что
    /// поглотил локальный хендлер. Поэтому, решив делать подкат, поглощаем R и отправляем
    /// предсказуемый запрос; иначе отдаём R дальше — ванильному падению и серверу (тот сам
    /// скажет про перезарядку).
    /// </summary>
    protected override bool HandleSlideInput(ICommonSession? session)
    {
        // Повтор уже отправленной R при ре-предсказании: тогда мы её не поглотили, не поглощаем и сейчас.
        if (!Timing.IsFirstTimePredicted)
            return false;

        if (session?.AttachedEntity is not { Valid: true } uid || !Exists(uid))
            return false;

        if (IsSliding(uid))
            return true;

        if (CheckSlide(uid, out _) != DutySlideCheck.Ready)
            return false;

        RaisePredictiveEvent(new DutySlideRequestEvent());
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Ре-предсказание гоняет Update по нескольку раз за тик — без отсечки пыль сыпалась бы пачками.
        if (!Timing.IsFirstTimePredicted)
            return;

        var now = Timing.CurTime;
        var query = EntityQueryEnumerator<DutySlidingComponent, DutyStaminaComponent>();
        while (query.MoveNext(out var uid, out var slide, out var stamina))
        {
            if (slide.Phase != DutySlidePhase.Sliding)
                continue;

            if (!_nextDust.TryGetValue(uid, out var next))
            {
                SpawnCloud(uid, stamina.SprintCloud, "sprint_cloud", 0.48f);
                _nextDust[uid] = now + DustInterval;
            }
            // Пыль только на рывке: на медленном доезде она бы висела облаком на месте и смазывала
            // главное — видно, как человек влетает в подкат и гасит скорость.
            else if (now >= next && slide.LastSpeed >= DustMinSpeed)
            {
                SpawnCloud(uid, stamina.SprintCloudSmall, "sprint_cloud_small", 0.34f);
                _nextDust[uid] = now + DustInterval;
            }
        }

        // Подкат кончился — забываем, чтобы следующий снова начался с большого облака.
        _finished.Clear();
        foreach (var uid in _nextDust.Keys)
        {
            if (!TryComp<DutySlidingComponent>(uid, out var slide) || slide.Phase != DutySlidePhase.Sliding)
                _finished.Add(uid);
        }

        foreach (var uid in _finished)
        {
            _nextDust.Remove(uid);
        }
    }

    /// <summary>
    /// Те же облака, что у спринта (<see cref="DutySprintVisualsSystem"/>), проигранные один раз с
    /// нулевого кадра.
    /// </summary>
    private void SpawnCloud(EntityUid uid, string proto, string state, float length)
    {
        if (TerminatingOrDeleted(uid))
            return;

        var effect = Spawn(proto, Transform(uid).Coordinates);

        _animation.Play(effect, new Animation
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = DutySprintVisualLayers.Base,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(state), 0f) },
                },
            },
        }, state);
    }
}
