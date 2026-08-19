using System.Text.RegularExpressions;
using Content.Server._CorvaxNext.Speech.Components;
using Content.Shared.Speech;
using Robust.Shared.Random;

namespace Content.Server._CorvaxNext.Speech.EntitySystems;

public sealed class ResomiAccentSystem : EntitySystem
{

    [Dependency] private readonly IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ResomiAccentComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, ResomiAccentComponent component, AccentGetEvent args)
    {
        var message = args.Message;

        // _Duty: было `_random.Pick(...)` как строка-замена (одно значение на всё
        // сообщение для каждого вызова Replace) — заменено на MatchEvaluator, чтобы
        // каждое отдельное вхождение буквы рандомизировалось независимо.
        // ш => шшш
        message = Regex.Replace(
            message,
            "ш+",
            match => _random.Pick(new List<string>() { "шш", "шшш" })
        );
        // Ш => ШШШ
        message = Regex.Replace(
            message,
            "Ш+",
            match => _random.Pick(new List<string>() { "ШШ", "ШШШ" })
        );
        // ч => щщщ
        message = Regex.Replace(
            message,
            "ч+",
            match => _random.Pick(new List<string>() { "щщ", "щщщ" })
        );
        // Ч => ЩЩЩ
        message = Regex.Replace(
            message,
            "Ч+",
            match => _random.Pick(new List<string>() { "ЩЩ", "ЩЩЩ" })
        );
        // р => ррр
        message = Regex.Replace(
            message,
            "р+",
            match => _random.Pick(new List<string>() { "рр", "ррр" })
        );
        // Р => РРР
        message = Regex.Replace(
            message,
            "Р+",
            match => _random.Pick(new List<string>() { "РР", "РРР" })
        );
        args.Message = message;
    }
}
