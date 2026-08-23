// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.AmbientMusic;

/// <summary>Трек для крит. состояния с явно заданной длительностью.</summary>
[DataDefinition]
public sealed partial class DutyCritTrack
{
    /// <summary>Путь к аудиофайлу.</summary>
    [DataField(required: true)]
    public SoundSpecifier Sound = default!;

    /// <summary>Длительность трека в секундах.</summary>
    [DataField(required: true)]
    public float Duration;
}

[Prototype("dynamicAmbientMusic")]
public sealed partial class DynamicAmbientMusicPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Очень хорошее состояние: 90–100% HP.</summary>
    [DataField(required: true)]
    public List<SoundSpecifier> TracksVeryGood = new();

    /// <summary>Хорошее состояние: 70–90% HP.</summary>
    [DataField(required: true)]
    public List<SoundSpecifier> TracksGood = new();

    /// <summary>Среднее состояние: 40–70% HP.</summary>
    [DataField(required: true)]
    public List<SoundSpecifier> TracksMedium = new();

    /// <summary>Ниже среднего: 25–40% HP.</summary>
    [DataField(required: true)]
    public List<SoundSpecifier> TracksBelowMedium = new();

    /// <summary>Ужасное состояние: 5–25% HP.</summary>
    [DataField(required: true)]
    public List<SoundSpecifier> TracksAwful = new();

    /// <summary>Критическое состояние по HP: менее 5% HP.</summary>
    [DataField(required: true)]
    public List<SoundSpecifier> TracksCritical = new();

    /// <summary>MobState.Critical — персонаж лежит без сознания. Треки с длительностью для плавных переходов.</summary>
    [DataField(required: true)]
    public List<DutyCritTrack> TracksMobCritical = new();

    /// <summary>Усиление громкости для TracksMobCritical (в dB, положительное = громче). Не зависит от <see cref="VolumeBoostDb"/>.</summary>
    [DataField]
    public float MobCritVolumeBoost = 8f;

    /// <summary>Длительность fade-out и fade-in при переходе между крит. треками (сек).</summary>
    [DataField]
    public float MobCritCrossfadeDuration = 10f;

    /// <summary>Боевая музыка — играет в петле при боевом режиме.</summary>
    [DataField(required: true)]
    public List<SoundSpecifier> CombatTracks = new();

    /// <summary>Боевая музыка при низком HP — играет в петле при боевом режиме + HP меньше порога.</summary>
    [DataField(required: true)]
    public List<SoundSpecifier> CombatLowTracks = new();

    /// <summary>Порог HP (%) для переключения на CombatLowTracks в боевом режиме.</summary>
    [DataField]
    public float CombatLowHpThreshold = 10f;

    /// <summary>Список звуков при смерти персонажа. Рандомный выбор.</summary>
    [DataField]
    public List<SoundSpecifier> DeathSounds = new();

    /// <summary>Усиление громкости звука смерти (в dB). Не зависит от <see cref="VolumeBoostDb"/> — смерть намеренно не участвует в общем бусте музыки.</summary>
    [DataField]
    public float DeathVolumeBoost = 2f;

    /// <summary>Список звуков при входе в критическое состояние (MobState.Critical). Рандомный выбор. КД 2 минуты, прерывается fadeout при резком выходе из крита.</summary>
    [DataField]
    public List<SoundSpecifier> CritEnterSounds = new();

    /// <summary>Усиление громкости звука входа в крит (в dB). Не зависит от <see cref="VolumeBoostDb"/> — крит-стингер намеренно не участвует в общем бусте музыки.</summary>
    [DataField]
    public float CritEnterVolumeBoost = 4.9f;

    [DataField] public float CalmMinInterval = 5f;
    [DataField] public float CalmMaxInterval = 50f;
    [DataField] public float CalmFadeInDuration = 2.5f;
    [DataField] public float CalmFadeOutDuration = 3.5f;
    [DataField] public float StateTransitionPause = 1.5f;
    [DataField] public float CombatFadeOutDuration = 1.5f;
    [DataField] public float CombatFadeInDuration = 0.5f;

    /// <summary>
    /// Общий буст громкости (в dB) для «музыкальных» категорий динамической музыки/эмбиента
    /// (HP-уровни, бой). Положительное = громче. Применяется поверх индивидуальных громкостей уровней.
    /// НЕ затрагивает критмод (<see cref="MobCritVolumeBoost"/>), смерть (<see cref="DeathVolumeBoost"/>)
    /// и крит-стингер (<see cref="CritEnterVolumeBoost"/>) — у них свои независимые бусты.
    /// </summary>
    [DataField] public float VolumeBoostDb = 6f;
}
