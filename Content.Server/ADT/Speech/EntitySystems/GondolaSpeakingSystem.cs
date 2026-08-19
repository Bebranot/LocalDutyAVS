using System.Text.RegularExpressions;
using Content.Server.Speech.Components;
using Content.Shared.Speech;

namespace Content.Server.Speech.EntitySystems
{
    public sealed class GondolaSpeakingSystem : EntitySystem
    {
        // Gondolas only know one word - matches any run of letters so punctuation/spacing survives untouched.
        private static readonly Regex RegexWord = new(@"[а-яёa-z]+", RegexOptions.IgnoreCase);

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

        private void OnAccent(EntityUid uid, GondolaSpeakingComponent component, AccentGetEvent args)
        {
            args.Message = Accentuate(args.Message);
        }
    }
}
