using System.Linq;
using Content.Client.ADT.Traits.UI;
using Content.Shared.ADT.Language;
using Content.Shared.Preferences;
using Content.Shared.Traits;
using Robust.Shared.Prototypes;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    /// <summary>
    /// Refreshes traits selector for ADT TraitsTab
    /// </summary>
    public void RefreshTraits()
    {
        if (Profile != null)
        {
            var selectedTraits = new HashSet<ProtoId<TraitPrototype>>(Profile.TraitPreferences.Count);
            foreach (var traitId in Profile.TraitPreferences)
            {
                if (_prototypeManager.HasIndex(traitId))
                {
                    selectedTraits.Add(new ProtoId<TraitPrototype>(traitId));
                }
            }

            Traits.SetSelectedTraits(selectedTraits, Profile);
            Traits.UpdateRequirements(Profile);
        }
        else
        {
            Traits.SetSelectedTraits(new HashSet<ProtoId<TraitPrototype>>(), Profile);
        }
    }

    private void OnTraitsSelectionChanged(HashSet<ProtoId<TraitPrototype>> traits)
    {
        if (Profile is null)
            return;

        // _Duty: применяем только разницу вместо снятия и повторного добавления всех черт (каждое — копия профиля).
        foreach (var existingTrait in Profile.TraitPreferences.ToList())
        {
            if (!traits.Contains(existingTrait))
                Profile = Profile.WithoutTraitPreference(existingTrait, _prototypeManager);
        }

        foreach (var trait in traits)
        {
            if (!Profile.TraitPreferences.Contains(trait))
                Profile = Profile.WithTraitPreference(trait.Id, _prototypeManager);
        }

        TrimLanguages();

        SetDirty();
        RefreshTraits();
        RefreshLanguages(); // ADT-Tweak: лимит языков зависит от выбранных трайтов (Полиглот)
    }

    // ADT-Tweak-Start
    private void TrimLanguages()
    {
        if (Profile is null)
            return;

        var species = _prototypeManager.Index(Profile.Species);
        var required = new HashSet<ProtoId<LanguagePrototype>>(species.DefaultLanguages);
        required.UnionWith(species.UniqueLanguages);
        var max = species.MaxLanguages + Profile.LanguageSlotsBonus;

        foreach (var lang in Profile.Languages.ToList())
        {
            if (Profile.Languages.Count <= max)
                break;
    
            if (required.Contains(lang))
                continue;
    
            Profile = Profile.WithoutLanguage(lang);
        }
    }
    // ADT-Tweak-End
}
