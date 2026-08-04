using System.Linq;
using Robust.Shared.Serialization;

namespace Content.Shared._Duty.HealthPhrases;

[RegisterComponent]
public sealed partial class HealthPhrasesComponent : Component
{
    // Custom*-списки используются только сервером (HealthPhrasesSystem.PickPhrase) для выбора
    // текста попап/шёпота; сам текст уже приходит клиенту отдельным popup/chat-
    // сообщением. Репликация самих списков всем клиентам в PVS не нужна и раскрывает
    // приватный текст игрока всем вокруг, поэтому [AutoNetworkedField] здесь не используется.
    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> CustomPopup70 = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> CustomWhisper70 = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> CustomPopup55 = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> CustomWhisper55 = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> CustomPopup40 = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> CustomWhisper40 = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> CustomPopup25 = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> CustomWhisper25 = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> CustomPopup10 = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> CustomWhisper10 = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> CustomPopup5 = new();

    [ViewVariables(VVAccess.ReadWrite)]
    public List<string> CustomWhisper5 = new();

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextPopupTime = TimeSpan.Zero;

    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextSpeechTime = TimeSpan.Zero;
}

[Serializable, NetSerializable]
public sealed class HealthPhrasesData
{
    // Старые профили: phrases70 → только popup (whisper пустой → FTL).
    [DataField("phrases70")]
    public List<string> Popup70 = new();

    [DataField("whisper70")]
    public List<string> Whisper70 = new();

    [DataField("phrases55")]
    public List<string> Popup55 = new();

    [DataField("whisper55")]
    public List<string> Whisper55 = new();

    [DataField("phrases40")]
    public List<string> Popup40 = new();

    [DataField("whisper40")]
    public List<string> Whisper40 = new();

    [DataField("phrases25")]
    public List<string> Popup25 = new();

    [DataField("whisper25")]
    public List<string> Whisper25 = new();

    [DataField("phrases10")]
    public List<string> Popup10 = new();

    [DataField("whisper10")]
    public List<string> Whisper10 = new();

    [DataField("phrases5")]
    public List<string> Popup5 = new();

    [DataField("whisper5")]
    public List<string> Whisper5 = new();

    public HealthPhrasesData Clone() => new()
    {
        Popup70 = new List<string>(Popup70),
        Whisper70 = new List<string>(Whisper70),
        Popup55 = new List<string>(Popup55),
        Whisper55 = new List<string>(Whisper55),
        Popup40 = new List<string>(Popup40),
        Whisper40 = new List<string>(Whisper40),
        Popup25 = new List<string>(Popup25),
        Whisper25 = new List<string>(Whisper25),
        Popup10 = new List<string>(Popup10),
        Whisper10 = new List<string>(Whisper10),
        Popup5 = new List<string>(Popup5),
        Whisper5 = new List<string>(Whisper5),
    };

    public bool MemberwiseEquals(HealthPhrasesData? other)
    {
        if (other is null)
            return false;

        return Popup70.SequenceEqual(other.Popup70)
               && Whisper70.SequenceEqual(other.Whisper70)
               && Popup55.SequenceEqual(other.Popup55)
               && Whisper55.SequenceEqual(other.Whisper55)
               && Popup40.SequenceEqual(other.Popup40)
               && Whisper40.SequenceEqual(other.Whisper40)
               && Popup25.SequenceEqual(other.Popup25)
               && Whisper25.SequenceEqual(other.Whisper25)
               && Popup10.SequenceEqual(other.Popup10)
               && Whisper10.SequenceEqual(other.Whisper10)
               && Popup5.SequenceEqual(other.Popup5)
               && Whisper5.SequenceEqual(other.Whisper5);
    }
}
