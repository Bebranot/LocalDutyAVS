using Content.Client.Audio;
using Content.Shared._Duty.Parry;
using Content.Shared._Duty.Parry.Components;
using Content.Shared.Input;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Duty.Parry;

/// <summary>
/// Клиентская часть QTE-катсцены: виньетка, скрытие HUD, музыка и приём нажатий.
/// Всё это локально и только у самих участников — посторонние видят лишь двух замерших
/// бойцов за барьером. Зум камеры выставляет сервер через ContentEyeComponent.
/// </summary>
public sealed class QteCutsceneSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IUserInterfaceManager _ui = default!;
    [Dependency] private readonly IOverlayManager _overlay = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IResourceCache _cache = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ContentAudioSystem _contentAudio = default!;

    private static readonly ProtoId<SoundCollectionPrototype> MusicCollection = "DutyQteSong";

    private const string QteContext = "qte";
    private const float MusicFadeIn = 1f;
    private const float MusicFadeOut = 0.5f;

    private QteCutsceneOverlay? _overlayInstance;
    private EntityUid? _musicStream;

    /// <summary>Виджеты HUD, спрятанные на время катсцены — восстанавливаем ровно их.</summary>
    private readonly List<Control> _hiddenWidgets = new();

    private bool _active;
    private string? _previousContext;

    public override void Initialize()
    {
        base.Initialize();

        var context = _input.Contexts.New(QteContext, "common");
        context.AddFunction(ContentKeyFunctions.QteUp);
        context.AddFunction(ContentKeyFunctions.QteLeft);
        context.AddFunction(ContentKeyFunctions.QteDown);
        context.AddFunction(ContentKeyFunctions.QteRight);
        context.AddFunction(ContentKeyFunctions.QteQ);
        context.AddFunction(ContentKeyFunctions.QteT);
        context.AddFunction(ContentKeyFunctions.QteE);
        context.AddFunction(ContentKeyFunctions.QteR);
        context.AddFunction(ContentKeyFunctions.QteG);
        context.AddFunction(ContentKeyFunctions.QteF);
        context.AddFunction(ContentKeyFunctions.QteH);
        context.AddFunction(ContentKeyFunctions.QteConfirm);

        CommandBinds.Builder
            .Bind(ContentKeyFunctions.QteUp, PromptHandler(QtePromptKey.W))
            .Bind(ContentKeyFunctions.QteLeft, PromptHandler(QtePromptKey.A))
            .Bind(ContentKeyFunctions.QteDown, PromptHandler(QtePromptKey.S))
            .Bind(ContentKeyFunctions.QteRight, PromptHandler(QtePromptKey.D))
            .Bind(ContentKeyFunctions.QteQ, PromptHandler(QtePromptKey.Q))
            .Bind(ContentKeyFunctions.QteT, PromptHandler(QtePromptKey.T))
            .Bind(ContentKeyFunctions.QteE, PromptHandler(QtePromptKey.E))
            .Bind(ContentKeyFunctions.QteR, PromptHandler(QtePromptKey.R))
            .Bind(ContentKeyFunctions.QteG, PromptHandler(QtePromptKey.G))
            .Bind(ContentKeyFunctions.QteF, PromptHandler(QtePromptKey.F))
            .Bind(ContentKeyFunctions.QteH, PromptHandler(QtePromptKey.H))
            .Bind(ContentKeyFunctions.QteConfirm, InputCmdHandler.FromDelegate(_ => SendFinal(), handle: true))
            .Register<QteCutsceneSystem>();

        SubscribeLocalEvent<QteParticipantComponent, ComponentStartup>(OnParticipantStartup);
        SubscribeLocalEvent<QteParticipantComponent, ComponentShutdown>(OnParticipantShutdown);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        StopCutscene();
        CommandBinds.Unregister<QteCutsceneSystem>();
        _input.Contexts.Remove(QteContext);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        if (!_active || _overlayInstance == null)
            return;

        // Страховка от залипания: если игрок сменил тело (смерть, гост, реконнект), ComponentShutdown
        // на нашей прошлой сущности до нас уже не дойдёт — без этой проверки HUD остался бы скрытым,
        // а ввод навсегда застрял бы в контексте "qte".
        if (_player.LocalEntity is not { } local || !TryComp<QteParticipantComponent>(local, out var participant))
        {
            StopCutscene();
            return;
        }

        // Состояние приходит с сервера и меняется часто — перечитываем каждый кадр,
        // чтобы оверлей рисовал актуальную подсказку/шкалу.
        _overlayInstance.Participant = participant;
    }

    private InputCmdHandler PromptHandler(QtePromptKey key)
    {
        return InputCmdHandler.FromDelegate(_ => SendPrompt(key), handle: true);
    }

    private void SendPrompt(QtePromptKey key)
    {
        if (!_active)
            return;

        RaiseNetworkEvent(new QtePromptInputEvent(key));
    }

    private void SendFinal()
    {
        if (!_active)
            return;

        RaiseNetworkEvent(new QteFinalInputEvent());
    }

    // ── Старт/стоп катсцены ───────────────────────────────────

    private void OnParticipantStartup(Entity<QteParticipantComponent> ent, ref ComponentStartup args)
    {
        if (_player.LocalEntity != ent.Owner)
            return;

        StartCutscene(ent.Comp);
    }

    private void OnParticipantShutdown(Entity<QteParticipantComponent> ent, ref ComponentShutdown args)
    {
        if (_player.LocalEntity != ent.Owner)
            return;

        StopCutscene();
    }

    private void StartCutscene(QteParticipantComponent participant)
    {
        if (_active)
            return;

        _active = true;

        _overlayInstance = new QteCutsceneOverlay(_clyde, _timing, _cache) { Participant = participant };
        _overlay.AddOverlay(_overlayInstance);

        HideHud();

        _previousContext = _input.Contexts.ActiveContext.Name;
        _input.Contexts.SetActiveContext(QteContext);

        PlayMusic(participant.MusicTrack);
    }

    private void StopCutscene()
    {
        if (!_active)
            return;

        _active = false;

        if (_overlayInstance != null)
        {
            _overlayInstance.Participant = null;
            _overlay.RemoveOverlay(_overlayInstance);
            _overlayInstance = null;
        }

        RestoreHud();

        if (_previousContext != null)
        {
            _input.Contexts.SetActiveContext(_previousContext);
            _previousContext = null;
        }

        // FadeOut сам останавливает поток, когда громкость дойдёт до минимума.
        if (_musicStream != null)
        {
            _contentAudio.FadeOut(_musicStream, duration: MusicFadeOut);
            _musicStream = null;
        }
    }

    private void PlayMusic(int trackIndex)
    {
        if (!_proto.TryIndex(MusicCollection, out var collection) || collection.PickFiles.Count == 0)
            return;

        var index = Math.Clamp(trackIndex, 0, collection.PickFiles.Count - 1);
        var path = collection.PickFiles[index];

        var stream = _audio.PlayGlobal(path.ToString(), _player.LocalEntity ?? EntityUid.Invalid, AudioParams.Default);

        if (stream == null)
            return;

        _musicStream = stream.Value.Entity;
        _contentAudio.FadeIn(_musicStream, duration: MusicFadeIn);
    }

    // ── Скрытие HUD ───────────────────────────────────────────

    /// <summary>
    /// Прячем всё на игровом экране, кроме контейнера вьюпорта — так работает для обоих
    /// вариантов экрана (DefaultGameScreen и SeparatedChatGameScreen) без знания их вёрстки.
    /// </summary>
    private void HideHud()
    {
        var screen = _ui.ActiveScreen;

        if (screen == null)
            return;

        foreach (var child in screen.Children)
        {
            if (!child.Visible || child.Name == "ViewportContainer")
                continue;

            child.Visible = false;
            _hiddenWidgets.Add(child);
        }
    }

    private void RestoreHud()
    {
        foreach (var widget in _hiddenWidgets)
        {
            widget.Visible = true;
        }

        _hiddenWidgets.Clear();
    }
}
