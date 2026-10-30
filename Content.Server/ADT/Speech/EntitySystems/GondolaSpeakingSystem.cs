using System.Text.RegularExpressions;
using Content.Server.Speech.Components;
using Content.Shared.Speech;
using Robust.Shared.Random;

namespace Content.Server.Speech.EntitySystems
{
    public sealed class GondolaSpeakingSystem : EntitySystem
    {
        private static readonly Regex RegexWord = new(@"[а-яёa-z]+", RegexOptions.IgnoreCase);
        private static readonly Regex RegexLoneI = new(@"(?<=\ )i(?=[\ \.\?]|$)");

        [Dependency] private IRobustRandom _random = default!;

        public override void Initialize()
        {
            SubscribeLocalEvent<GondolaSpeakingComponent, AccentGetEvent>(OnAccent);
        }

        public string Accentuate(string message)
        {
            return RegexWord.Replace(message, ReplaceWord);
        }

        private static string ReplaceWord(Match match)
        {
            var word = match.Value;
            var replacement = "гондола";

            if (char.IsUpper(word[0]))
                replacement = char.ToUpper(replacement[0]) + replacement[1..];

            return replacement;
        }

        private void OnAccent(EntityUid uid, GondolaSpeakingComponent component, ref AccentGetEvent args)
        {
            args.Message = Accentuate(args.Message);
        }
    }
}
