using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.Shared._Duty;

/// <summary>
/// CVar громкости по уровню динамической музыки.
/// </summary>
public static class DutyAmbientMusicCVar
{
    public static CVarDef<float> GetVolumeCVar(DutyAmbientMusicLevel level) => level switch
    {
        DutyAmbientMusicLevel.VeryGood => DutyCCVars.DynamicAmbientMusicVolumeVeryGood,
        DutyAmbientMusicLevel.Good => DutyCCVars.DynamicAmbientMusicVolumeGood,
        DutyAmbientMusicLevel.Medium => DutyCCVars.DynamicAmbientMusicVolumeMedium,
        DutyAmbientMusicLevel.BelowMedium => DutyCCVars.DynamicAmbientMusicVolumeBelowMedium,
        DutyAmbientMusicLevel.Awful => DutyCCVars.DynamicAmbientMusicVolumeAwful,
        DutyAmbientMusicLevel.HpCritical => DutyCCVars.DynamicAmbientMusicVolumeHpCritical,
        DutyAmbientMusicLevel.MobCritical => DutyCCVars.DynamicAmbientMusicVolumeMobCritical,
        DutyAmbientMusicLevel.Combat => DutyCCVars.DynamicAmbientMusicVolumeCombat,
        DutyAmbientMusicLevel.CombatLow => DutyCCVars.DynamicAmbientMusicVolumeCombatLow,
        DutyAmbientMusicLevel.Death => DutyCCVars.DynamicAmbientMusicVolumeDeath,
        DutyAmbientMusicLevel.CritEnter => DutyCCVars.DynamicAmbientMusicVolumeCritEnter,
        _ => DutyCCVars.DynamicAmbientMusicVolumeVeryGood,
    };

    public static string GetLocaleKey(DutyAmbientMusicLevel level) => level switch
    {
        DutyAmbientMusicLevel.VeryGood => "duty-ambient-level-very-good",
        DutyAmbientMusicLevel.Good => "duty-ambient-level-good",
        DutyAmbientMusicLevel.Medium => "duty-ambient-level-medium",
        DutyAmbientMusicLevel.BelowMedium => "duty-ambient-level-below-medium",
        DutyAmbientMusicLevel.Awful => "duty-ambient-level-awful",
        DutyAmbientMusicLevel.HpCritical => "duty-ambient-level-hp-critical",
        DutyAmbientMusicLevel.MobCritical => "duty-ambient-level-mob-critical",
        DutyAmbientMusicLevel.Combat => "duty-ambient-level-combat",
        DutyAmbientMusicLevel.CombatLow => "duty-ambient-level-combat-low",
        DutyAmbientMusicLevel.Death => "duty-ambient-level-death",
        DutyAmbientMusicLevel.CritEnter => "duty-ambient-level-crit-enter",
        _ => "duty-ambient-level-unknown",
    };

    public static DutyAmbientMusicLevel FromHealthState(HealthMusicState state) => state switch
    {
        HealthMusicState.VeryGood => DutyAmbientMusicLevel.VeryGood,
        HealthMusicState.Good => DutyAmbientMusicLevel.Good,
        HealthMusicState.Medium => DutyAmbientMusicLevel.Medium,
        HealthMusicState.BelowMedium => DutyAmbientMusicLevel.BelowMedium,
        HealthMusicState.Awful => DutyAmbientMusicLevel.Awful,
        HealthMusicState.Critical => DutyAmbientMusicLevel.HpCritical,
        _ => DutyAmbientMusicLevel.VeryGood,
    };
}

/// <summary>
/// Состояние HP для спокойной (не боевой) музыки — зеркало логики в <see cref="Content.Client.Duty.Audio.DynamicAmbientMusicSystem"/>.
/// </summary>
public enum HealthMusicState
{
    VeryGood,
    Good,
    Medium,
    BelowMedium,
    Awful,
    Critical,
}
