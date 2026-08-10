using System;
using System.Collections.Generic;
using Content.Client.UserInterface.Systems.Actions.Widgets;
using Content.Client.UserInterface.Systems.Alerts.Widgets;
using Content.Client.UserInterface.Systems.Hotbar.Widgets;
using Content.Client.UserInterface.Systems.Inventory.Widgets;
using Content.Client.UserInterface.Systems.MenuBar.Widgets;
using Content.Shared._Duty.Aiming;
using Content.Shared._Duty.FireAgony;
using Content.Shared.Mobs.Systems;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Client._Duty.Aiming;

/// <summary>
/// Чисто визуальный клиентский слой: пока игрок непрерывно целится (держит ПКМ с огнестрелом,
/// т.е. на нём висит <see cref="AimingComponent"/>) дольше <see cref="AimHoldDelay"/> — плавно
/// затемняет весь HUD, КРОМЕ рук и чата, до нуля. При отмене прицеливания — реверсный плавный
/// fade-in от текущей прозрачности. На «жёстких» событиях (смерть, потеря сознания,
/// дисконнект/детач, смена раунда) — мгновенно возвращает HUD, чтобы он не «залип» невидимым.
///
/// Не трогает серверную логику. Гаснут: верхнее меню (<see cref="GameTopMenuBar"/>), правый блок
/// статусов (<see cref="AlertsUI"/>), слоты инвентаря (<see cref="InventoryGui"/>), панель действий
/// (<see cref="ActionsBar"/>) и быстрые слоты хотбара (<c>MainHotbar</c>/<c>SecondHotbar</c> +
/// сторадж-контейнеры внутри <see cref="HotbarGui"/>). Остаются при alpha=1: мир, РУКИ
/// (<c>HandContainer</c> + статус-панели активного предмета внутри того же хотбара) и ЧАТ.
///
/// ВАЖНО: иконки алертов и предметов в слотах рисуются через <c>SpriteView</c>, который берёт
/// прозрачность из накопленного modulate экранного хэндла (см. правку <c>// _Duty</c> в движковом
/// <c>SpriteView.Draw</c>) — без неё спрайтовый HUD не тускнел бы вместе с родителем.
/// </summary>
public sealed class HudAimFadeSystem : EntitySystem
{
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    // --- Тюнинг (настройка через CCVar — вне скоупа ТЗ) ---

    /// <summary>Сколько нужно непрерывно целиться, прежде чем HUD начнёт гаснуть.</summary>
    private static readonly TimeSpan AimHoldDelay = TimeSpan.FromSeconds(3);

    /// <summary>Длительность плавного затемнения до alpha=0.</summary>
    private const float FadeOutDuration = 2.0f;

    /// <summary>Длительность плавного возврата к alpha=1 (реверс с текущей точки).</summary>
    private const float FadeInDuration = 1.5f;

    /// <summary>Быстрый фейд HUD при входе/выходе сцены «Агонии от огня» (совпадает с виньеткой).</summary>
    private const float AgonyFadeDuration = 0.5f;

    private TimeSpan? _aimStart;
    private bool _agonyPrev;
    private EntityUid? _lastPlayer;

    private readonly List<HudFadeGroup> _groups = new();

    public override void Initialize()
    {
        base.Initialize();

        // Группы, которые гаснут. Резолвятся с активного экрана каждый кадр — экран может быть
        // не загружен / сменён, поэтому храним не сам виджет, а способ его достать.
        // Целые виджеты:
        _groups.Add(new HudFadeGroup(static screen => screen.GetWidget<GameTopMenuBar>()));
        _groups.Add(new HudFadeGroup(static screen => screen.GetWidget<AlertsUI>()));
        _groups.Add(new HudFadeGroup(static screen => screen.GetWidget<InventoryGui>()));
        _groups.Add(new HudFadeGroup(static screen => screen.GetWidget<ActionsBar>()));

        // Хотбар особый: в нём живут и руки (оставляем), и быстрые слоты (гасим). Поэтому таргетим
        // именно слот-контейнеры по имени, не трогая HandContainer/StatusPanelLeft/StatusPanelRight.
        _groups.Add(new HudFadeGroup(static screen => ResolveHotbarChild(screen, "MainHotbar")));
        _groups.Add(new HudFadeGroup(static screen => ResolveHotbarChild(screen, "SecondHotbar")));
        _groups.Add(new HudFadeGroup(static screen => ResolveHotbarChild(screen, "SingleStorageContainer")));
        _groups.Add(new HudFadeGroup(static screen => ResolveHotbarChild(screen, "DoubleStorageContainer")));
    }

    /// <summary>
    /// Достаёт именованный контрол внутри <see cref="HotbarGui"/> без исключений (в отличие от
    /// <c>FindControl</c>, который кидает, если контрол не найден). Возвращает null, если хотбара
    /// нет на экране или имя не найдено.
    /// </summary>
    private static Control? ResolveHotbarChild(UIScreen screen, string name)
    {
        var hotbar = screen.GetWidget<HotbarGui>();
        return hotbar == null ? null : FindByName(hotbar, name);
    }

    /// <summary>Рекурсивный поиск потомка по <see cref="Control.Name"/> (первое совпадение).</summary>
    private static Control? FindByName(Control root, string name)
    {
        foreach (var child in root.Children)
        {
            if (child.Name == name)
                return child;

            if (FindByName(child, name) is { } found)
                return found;
        }

        return null;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var screen = _ui.ActiveScreen;
        var player = _player.LocalEntity;

        var playerChanged = _lastPlayer != player;
        _lastPlayer = player;

        // «Жёсткие» условия мгновенного возврата HUD:
        //  - нет управляемой сущности (дисконнект / детач / смена раунда);
        //  - сущность сменилась (новый спавн / переселение);
        //  - персонаж не жив (смерть / крит-«потеря сознания»).
        if (player is not { } user || playerChanged || !_mobState.IsAlive(user))
        {
            _aimStart = null;
            _agonyPrev = false;
            foreach (var group in _groups)
                group.HardReset(screen);
            return;
        }

        var aiming = HasComp<AimingComponent>(user);

        if (aiming)
            _aimStart ??= _timing.CurTime;
        else
            _aimStart = null;

        // _Duty: сцена «Агонии от огня» тоже гасит HUD — быстро (0.5с) и приоритетнее прицеливания.
        var agony = TryComp<FireAgonyComponent>(user, out var fireAgony) && fireAgony.Active;
        var aimFaded = aiming && _aimStart is { } start && _timing.CurTime - start >= AimHoldDelay;
        var faded = agony || aimFaded;

        var target = faded ? 0f : 1f;
        // Агония и её обрыв фейдятся быстро; прицеливание — своими длительностями.
        var duration = agony || _agonyPrev
            ? AgonyFadeDuration
            : faded ? FadeOutDuration : FadeInDuration;
        _agonyPrev = agony;

        foreach (var group in _groups)
        {
            group.SetTarget(target, duration);
            group.Tick(frameTime, screen);
        }
    }

    /// <summary>
    /// Состояние затемнения одной HUD-группы: собственный отменяемый/переиспользуемый твин
    /// (быстрые перещёлкивания ПКМ не плодят параллельных анимаций — реверс идёт от текущего alpha),
    /// плюс снапшот <see cref="Control.MouseFilter"/> для перевода поддерева в Ignore на время
    /// затемнения, чтобы скрытый HUD не ловил клики «в никуда».
    /// </summary>
    private sealed class HudFadeGroup
    {
        /// <summary>Ниже этого alpha группа считается «скрываемой» и переводится в MouseFilter=Ignore.</summary>
        private const float MouseIgnoreThreshold = 0.99f;

        private readonly Func<UIScreen, Control?> _resolve;
        private readonly Dictionary<Control, Control.MouseFilterMode> _savedFilters = new();

        private float _from = 1f;
        private float _to = 1f;
        private float _elapsed;
        private float _duration;
        private float _current = 1f;
        private bool _suppressed;

        public HudFadeGroup(Func<UIScreen, Control?> resolve)
        {
            _resolve = resolve;
        }

        /// <summary>Задать целевой alpha. Если цель не изменилась — твин не перезапускается.</summary>
        public void SetTarget(float target, float duration)
        {
            // Цели всегда 0f/1f-литералы, так что точное сравнение корректно.
            if (_to.Equals(target))
                return;

            _from = _current;
            _to = target;
            _elapsed = 0f;
            _duration = duration;
        }

        public void Tick(float dt, UIScreen? screen)
        {
            if (_duration <= 0f)
            {
                _current = _to;
            }
            else
            {
                _elapsed = MathF.Min(_elapsed + dt, _duration);
                var p = _elapsed / _duration;
                var eased = p * p * (3f - 2f * p); // smoothstep — ease-in-out
                _current = _from + (_to - _from) * eased;
            }

            Apply(screen);
        }

        /// <summary>Мгновенно (без анимации) вернуть alpha=1 и восстановить клики.</summary>
        public void HardReset(UIScreen? screen)
        {
            _from = _to = _current = 1f;
            _elapsed = 0f;
            _duration = 0f;
            Apply(screen);
        }

        private void Apply(UIScreen? screen)
        {
            var widget = screen is null ? null : _resolve(screen);
            if (widget == null)
            {
                // Экран/виджет отсутствует — забываем снапшот, восстанавливать нечего.
                _savedFilters.Clear();
                _suppressed = false;
                return;
            }

            widget.Modulate = Color.White.WithAlpha(_current);

            var shouldIgnore = _current < MouseIgnoreThreshold;
            if (shouldIgnore)
                ApplyIgnore(widget);
            else if (_suppressed)
                RestoreFilters();

            _suppressed = shouldIgnore;
        }

        /// <summary>
        /// Рекурсивно переводит всё поддерево в MouseFilter=Ignore, запоминая исходные значения.
        /// Идемпотентно и вызывается каждый кадр, пока группа скрыта, — так корректно гасятся
        /// и контролы, добавленные уже во время затемнения (например, новые иконки алертов).
        /// </summary>
        private void ApplyIgnore(Control root)
        {
            if (root.MouseFilter != Control.MouseFilterMode.Ignore)
            {
                _savedFilters.TryAdd(root, root.MouseFilter);
                root.MouseFilter = Control.MouseFilterMode.Ignore;
            }

            foreach (var child in root.Children)
                ApplyIgnore(child);
        }

        private void RestoreFilters()
        {
            foreach (var (control, filter) in _savedFilters)
            {
                if (!control.Disposed)
                    control.MouseFilter = filter;
            }

            _savedFilters.Clear();
        }
    }
}
