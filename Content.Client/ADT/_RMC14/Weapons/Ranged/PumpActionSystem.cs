using Content.Shared._RMC14.Input;
using Content.Shared._RMC14.Weapons.Ranged;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Client.Input;

namespace Content.Client._RMC14.Weapons.Ranged;

public sealed class PumpActionSystem : SharedPumpActionSystem
{
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    protected override void OnExamined(Entity<PumpActionComponent> ent, ref ExaminedEvent args)
    {
        // _Duty: было `if (!_input.TryGetKeyBinding(...)) return;` — Examine ("cm-gun-pump-examine")
        // статичный текст без параметра {$key} (в отличие от Popup/PopupKey ниже), результат
        // TryGetKeyBinding нигде не используется. Из-за этого у игрока БЕЗ назначенной клавиши
        // CMUniqueAction (как раз тому, кому эта подсказка нужнее всего) examine вообще не
        // показывал текст про взвод помпового оружия — базовая (серверная) версия его показывает
        // всегда. Убрали бессмысленный ранний return.
        args.PushMarkup(Loc.GetString(ent.Comp.Examine), 1);
    }

    protected override void OnAttemptShoot(Entity<PumpActionComponent> ent, ref AttemptShootEvent args)
    {
        base.OnAttemptShoot(ent, ref args);

        if (!ent.Comp.Pumped)
        {
            var message = _input.TryGetKeyBinding(CMKeyFunctions.CMUniqueAction, out var bind)
                ? Loc.GetString(ent.Comp.PopupKey, ("key", bind.GetKeyString()))
                : Loc.GetString(ent.Comp.Popup);
            _popup.PopupClient(message, args.User, args.User);
        }
    }
}
