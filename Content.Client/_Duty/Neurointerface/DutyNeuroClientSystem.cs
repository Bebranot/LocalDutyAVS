// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.GameTicking.Managers;
using Content.Shared._Duty.Neurointerface;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Neurointerface;

/// <summary>
/// _Duty: клиентская система нейроинтерфейса NT-SEC.
/// Регистрирует world-маркеры, обводку соотрядцев и экран flatline, отслеживает состояние локального носителя маски
/// и управляет фазами последовательности смерти (HUD-глитч -> flatline).
/// </summary>
public sealed partial class DutyNeuroClientSystem : EntitySystem
{
    public const float HudDeathGlitchDuration = 0.85f;
    public const float FlatlineTotalDuration = 4.6f;

    /// <summary>Длительность плавного затухания flatline при закрытии по клику.</summary>
    public const float FlatlineDismissFadeDuration = 0.3f;

    [Dependency] private IOverlayManager _overlayManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ClientGameTicker _gameTicker = default!;

    private DutyNeuroWorldMarkersOverlay? _markersOverlay;
    private DutyNeuroFlatlineOverlay? _flatlineOverlay;
    private DutyNeuroSquadOutlineOverlay? _squadOutlineOverlay;

    private EntityUid? _trackedWearer;
    private bool _wasDead;
    private float _deadElapsed;
    private bool _flatlineDismissed;

    /// <summary>Значение <see cref="_deadElapsed"/> в момент закрытия flatline кликом.</summary>
    private float _dismissedAtElapsed;

    /// <summary>Должен ли сейчас отображаться полноэкранный оверлей flatline.</summary>
    public bool ShouldShowFlatline
    {
        get
        {
            if (!TryGetActiveWearer(out _, out var wearer))
                return false;

            if (wearer.OwnerStatus != DutyNeuroMemberStatus.Dead)
                return false;

            if (_flatlineDismissed && DismissFade <= 0f)
                return false;

            return _deadElapsed >= HudDeathGlitchDuration &&
                   _deadElapsed < HudDeathGlitchDuration + FlatlineTotalDuration;
        }
    }

    /// <summary>Время внутри показа flatline (начиная с 0 после завершения HUD-глитча).</summary>
    public float FlatlineElapsed => MathF.Max(0f, _deadElapsed - HudDeathGlitchDuration);

    /// <summary>Множитель прозрачности от закрытия по клику: 1 — не закрыт, затем плавно до 0.</summary>
    public float DismissFade => !_flatlineDismissed
        ? 1f
        : Math.Clamp(1f - (_deadElapsed - _dismissedAtElapsed) / FlatlineDismissFadeDuration, 0f, 1f);

    public override void Initialize()
    {
        base.Initialize();

        _markersOverlay = new DutyNeuroWorldMarkersOverlay();
        _flatlineOverlay = new DutyNeuroFlatlineOverlay();
        _squadOutlineOverlay = new DutyNeuroSquadOutlineOverlay();
        _overlayManager.AddOverlay(_markersOverlay);
        _overlayManager.AddOverlay(_flatlineOverlay);
        _overlayManager.AddOverlay(_squadOutlineOverlay);

        SubscribeLocalEvent<LocalPlayerAttachedEvent>(OnLocalPlayerAttached);
        SubscribeLocalEvent<LocalPlayerDetachedEvent>(OnLocalPlayerDetached);
        SubscribeLocalEvent<DutyNeurointerfaceWearerComponent, ComponentStartup>(OnWearerStartup);
        SubscribeLocalEvent<DutyNeurointerfaceWearerComponent, ComponentRemove>(OnWearerRemove);

        _inputManager.KeyBindStateChanged += OnKeyBindStateChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _inputManager.KeyBindStateChanged -= OnKeyBindStateChanged;

        if (_markersOverlay != null)
            _overlayManager.RemoveOverlay(_markersOverlay);
        if (_flatlineOverlay != null)
            _overlayManager.RemoveOverlay(_flatlineOverlay);
        if (_squadOutlineOverlay != null)
            _overlayManager.RemoveOverlay(_squadOutlineOverlay);

        _markersOverlay = null;
        _flatlineOverlay = null;
        _squadOutlineOverlay = null;
        ResetDeathSequence();
    }

    public bool TryGetActiveWearer(out EntityUid uid, out DutyNeurointerfaceWearerComponent wearer)
    {
        if (_playerManager.LocalEntity is { } local &&
            TryComp(local, out DutyNeurointerfaceWearerComponent? comp) &&
            comp.Active)
        {
            uid = local;
            wearer = comp;
            return true;
        }

        uid = EntityUid.Invalid;
        wearer = null!;
        return false;
    }

    public TimeSpan GetCurrentRoundTime()
    {
        var elapsed = _timing.CurTime - _gameTicker.RoundStartTimeSpan;
        return elapsed < TimeSpan.Zero ? TimeSpan.Zero : elapsed;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!TryGetActiveWearer(out var uid, out var wearer))
        {
            _trackedWearer = null;
            ResetDeathSequence();
            return;
        }

        if (_trackedWearer != uid)
        {
            _trackedWearer = uid;
            ResetDeathSequence();
        }

        if (wearer.OwnerStatus == DutyNeuroMemberStatus.Dead)
        {
            if (!_wasDead)
            {
                _wasDead = true;
                _deadElapsed = 0f;
                _flatlineDismissed = false;
            }
            else
            {
                _deadElapsed += MathF.Max(0f, frameTime);
            }
        }
        else if (_wasDead)
        {
            // Носителя реанимировали или состояние откатилось — полностью сбрасываем сцену смерти.
            ResetDeathSequence();
        }
    }

    private void OnLocalPlayerAttached(LocalPlayerAttachedEvent args)
    {
        _trackedWearer = args.Entity;
        ResetDeathSequence();
        SyncInitialDeadState(args.Entity);
    }

    private void OnLocalPlayerDetached(LocalPlayerDetachedEvent args)
    {
        _trackedWearer = null;
        ResetDeathSequence();
    }

    private void OnWearerStartup(EntityUid uid, DutyNeurointerfaceWearerComponent component, ComponentStartup args)
    {
        if (_playerManager.LocalEntity != uid)
            return;

        _trackedWearer = uid;
        ResetDeathSequence();
        SyncInitialDeadState(uid);
    }

    private void OnWearerRemove(EntityUid uid, DutyNeurointerfaceWearerComponent component, ComponentRemove args)
    {
        if (_playerManager.LocalEntity != uid && _trackedWearer != uid)
            return;

        _trackedWearer = null;
        ResetDeathSequence();
    }

    private void SyncInitialDeadState(EntityUid uid)
    {
        if (!TryComp(uid, out DutyNeurointerfaceWearerComponent? wearer) || !wearer.Active)
            return;

        if (wearer.OwnerStatus == DutyNeuroMemberStatus.Dead)
        {
            // Если игрок уже подключился к мёртвому телу с маской, не крутим flatline заново,
            // чтобы не перекрывать интерфейс призрака/реконнекта (время уже за пределом показа).
            _wasDead = true;
            _deadElapsed = HudDeathGlitchDuration + FlatlineTotalDuration;
            _flatlineDismissed = true;
            _dismissedAtElapsed = 0f;
        }
    }

    private void OnKeyBindStateChanged(ViewportBoundKeyEventArgs args)
    {
        if (_flatlineDismissed || !ShouldShowFlatline)
            return;

        var keyArgs = args.KeyEventArgs;
        if (keyArgs.State != BoundKeyState.Down)
            return;

        if (keyArgs.Function == EngineKeyFunctions.UIClick ||
            keyArgs.Function == EngineKeyFunctions.UIRightClick)
        {
            // Запоминаем момент закрытия: оверлей плавно гаснет за FlatlineDismissFadeDuration.
            _flatlineDismissed = true;
            _dismissedAtElapsed = _deadElapsed;
        }
    }

    private void ResetDeathSequence()
    {
        _wasDead = false;
        _deadElapsed = 0f;
        _flatlineDismissed = false;
        _dismissedAtElapsed = 0f;
    }
}
