using Content.Server.ADT.Language;
using Content.Shared._Duty.Traits;
using Content.Shared.ADT.Language;
namespace Content.Server._Duty.Traits;

/// <summary>
/// Выдаёт язык грубой речи и удерживает его активным.
/// </summary>
public sealed partial class DutyRoughSpeechSystem : EntitySystem
{
    public const string LanguageId = "DutyRough";

    [Dependency] private LanguageSystem _language = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DutyRoughSpeechComponent, ComponentInit>(OnInit);
        SubscribeNetworkEvent<LanguageChosenMessage>(OnLanguageChosen, after: new[] { typeof(LanguageSystem) });
    }

    private void OnInit(EntityUid uid, DutyRoughSpeechComponent _, ComponentInit args)
    {
        ApplyRoughLanguage(uid);
    }

    private void OnLanguageChosen(LanguageChosenMessage args)
    {
        var uid = GetEntity(args.Uid);

        if (!HasComp<DutyRoughSpeechComponent>(uid) || args.SelectedLanguage == LanguageId)
            return;

        ApplyRoughLanguage(uid);
    }

    private void ApplyRoughLanguage(EntityUid uid)
    {
        if (!TryComp<LanguageSpeakerComponent>(uid, out var speaker))
            return;

        _language.AddSpokenLanguage(uid, LanguageId, LanguageKnowledge.Speak, speaker);
        speaker.CurrentLanguage = LanguageId;
        Dirty(uid, speaker);
        _language.UpdateUi(uid, speaker);
    }
}
