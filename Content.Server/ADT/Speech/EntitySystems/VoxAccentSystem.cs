using System.Text.RegularExpressions;
using Content.Server.Speech.Components;
using Robust.Shared.Random;
using Content.Shared.Speech;

namespace Content.Server.Speech.EntitySystems;

public sealed class VoxAccentSystem : EntitySystem
{
    [Dependency] private readonly IRobustRandom _random = default!; // Corvax-Localization

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VoxAccentComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, VoxAccentComponent component, AccentGetEvent args)
    {
        var message = args.Message;
        // ADT-Localization-Start
        // _Duty: было `_random.Pick(...)` как строка-замена — значение выбиралось
        // ОДИН раз на всё сообщение, и все вхождения "к+" в этом сообщении получали
        // одну и ту же замену. Используем MatchEvaluator, чтобы каждое вхождение
        // выбиралось независимо (как уже сделано в SickTeethAccentSystem).
        // к => ке
        message = Regex.Replace(
            message,
            "к+",
            match => _random.Pick(new List<string>() { "ки", "кик" })
        );
        // К => Ке
        message = Regex.Replace(
            message,
            "К+",
            match => _random.Pick(new List<string>() { "Ки", "Кик" })
        );
        // ADT-Localization-End
        message = Regex.Replace(message, "ч+", "ч");
        message = Regex.Replace(message, "Ч+", "Ч");

        args.Message = message;
    }
}
