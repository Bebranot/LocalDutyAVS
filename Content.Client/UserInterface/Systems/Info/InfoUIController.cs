using Content.Client.Gameplay;
using Content.Client.Info;
using Content.Shared.Guidebook;
using Content.Shared.Info;
using Robust.Client.Audio;
using Robust.Client.Console;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.UserInterface.Systems.Info;

public sealed class InfoUIController : UIController, IOnStateExited<GameplayState>
{
    [Dependency] private readonly IClientConsoleHost _consoleHost = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    private static readonly SoundPathSpecifier FuckRulesSound = new("/Audio/_Duty/UI/rules_skip.ogg");

    private RulesPopup? _rulesPopup;
    private RulesAndInfoWindow? _infoWindow;

    [ValidatePrototypeId<GuideEntryPrototype>]
    private const string DefaultRuleset = "ADTRuleset"; //ADT Rule

    public ProtoId<GuideEntryPrototype> RulesEntryId = DefaultRuleset;

    protected override string SawmillName => "rules";

    public override void Initialize()
    {
        base.Initialize();

        _netManager.RegisterNetMessage<RulesAcceptedMessage>();
        _netManager.RegisterNetMessage<SendRulesInformationMessage>(OnRulesInformationMessage);

        _consoleHost.RegisterCommand("fuckrules",
            "",
            "",
            (_, _, _) =>
        {
            var audioParams = AudioParams.Default.WithVolume(-3f);
            _entMan.System<AudioSystem>().PlayGlobal(FuckRulesSound, Filter.Local(), false, audioParams);

            Timer.Spawn(1000, () => OnAcceptPressed(true));
        });
    }

    private void OnRulesInformationMessage(SendRulesInformationMessage message)
    {
        RulesEntryId = message.CoreRules;

        if (message.ShouldShowRules)
            ShowRules(message.PopupTime);
    }

    public void OnStateExited(GameplayState state)
    {
        if (_infoWindow == null)
            return;

        _infoWindow.Dispose();
        _infoWindow = null;
    }

    private void ShowRules(float time)
    {
        if (_rulesPopup != null)
            return;

        _rulesPopup = new RulesPopup
        {
            Timer = time
        };

        _rulesPopup.OnQuitPressed += OnQuitPressed;
        _rulesPopup.OnAcceptPressed += OnAcceptPressed;
        UIManager.WindowRoot.AddChild(_rulesPopup);
        LayoutContainer.SetAnchorPreset(_rulesPopup, LayoutContainer.LayoutPreset.Wide);
    }

    private void OnQuitPressed()
    {
        _consoleHost.ExecuteCommand("quit");
    }

    private void OnAcceptPressed(bool fuckRules)
    {
        var message = new RulesAcceptedMessage() { FuckRules = fuckRules };
        _netManager.ClientSendMessage(message);

        _rulesPopup?.Orphan();
        _rulesPopup = null;
    }

    public GuideEntryPrototype GetCoreRuleEntry()
    {
        if (!_prototype.TryIndex(RulesEntryId, out var guideEntryPrototype))
        {
            guideEntryPrototype = _prototype.Index(RulesEntryId); //ADT tweak
            Log.Error($"Couldn't find the following prototype: {RulesEntryId}. Falling back to {DefaultRuleset}, please check that the server has the rules set up correctly");
            return guideEntryPrototype;
        }

        return guideEntryPrototype;
    }

    public void OpenWindow()
    {
        if (_infoWindow == null || _infoWindow.Disposed)
            _infoWindow = UIManager.CreateWindow<RulesAndInfoWindow>();

        _infoWindow?.OpenCentered();
    }
}
