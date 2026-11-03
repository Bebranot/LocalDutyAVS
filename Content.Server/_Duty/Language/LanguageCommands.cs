// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later
//
// _Duty: портировано из Space Onyx (LanguageListCommand/LanguageSelectCommand), адаптировано под
// нашу схему LanguageSpeakerComponent (ADT: CurrentLanguage + Dictionary<string, LanguageKnowledge>
// вместо их SpokenLanguages/UnderstoodLanguages HashSet). Сделано админ-командами — у Onyx команды
// не отмечены как игрокские, а свободно менять себе язык через консоль не хотим.

using System.Linq;
using Content.Server.Administration;
using Content.Shared.ADT.Language;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._Duty.Language;

[AdminCommand(AdminFlags.Admin)]
public sealed class LanguageListCommand : IConsoleCommand
{
    public string Command => "languagelist";
    public string Description => "Показывает известные языки текущего персонажа.";
    public string Help => "languagelist";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { } entity ||
            !IoCManager.Resolve<IEntityManager>().TryGetComponent(entity, out LanguageSpeakerComponent? speaker))
            return;

        shell.WriteLine($"Current: {speaker.CurrentLanguage}");
        foreach (var (lang, knowledge) in speaker.Languages.OrderBy(kv => kv.Key))
            shell.WriteLine($"{lang}: {knowledge}");
    }
}

[AdminCommand(AdminFlags.Admin)]
public sealed class LanguageSelectCommand : IConsoleCommand
{
    public string Command => "languageselect";
    public string Description => "Выставляет текущий разговорный язык персонажа.";
    public string Help => "languageselect <language>";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player?.AttachedEntity is not { } entity || args.Length != 1)
            return;

        var entities = IoCManager.Resolve<IEntityManager>();
        if (!entities.TryGetComponent(entity, out LanguageSpeakerComponent? speaker) ||
            !speaker.Languages.ContainsKey(args[0]))
        {
            shell.WriteError("Unknown or unavailable language.");
            return;
        }

        speaker.CurrentLanguage = args[0];
        entities.Dirty(entity, speaker);
    }
}
