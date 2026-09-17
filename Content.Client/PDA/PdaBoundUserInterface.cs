using System.Numerics;
using Content.Client.ADT.CartridgeLoader.Cartridges;
using Content.Client.CartridgeLoader;
using Content.Shared.CartridgeLoader;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;
using JetBrains.Annotations;
using Robust.Client.Animations;
using Robust.Client.UserInterface;
using Robust.Shared.Animations;

namespace Content.Client.PDA
{
    [UsedImplicitly]
    public sealed class PdaBoundUserInterface : CartridgeLoaderBoundUserInterface
    {
        // Matches PdaMenu.xaml's own SetSize - the PDA window's normal size we return to
        // once NanoChat (the one program dense enough to want more room) closes.
        private static readonly Vector2 DefaultPdaSize = new(576, 450);
        private static readonly Vector2 NanoChatPdaSize = new(760, 560);
        private const string ResizeAnimationKey = "nano-chat-pda-resize";

        private static readonly Animation ResizeToNanoChat = BuildResizeAnimation(DefaultPdaSize, NanoChatPdaSize);
        private static readonly Animation ResizeToDefault = BuildResizeAnimation(NanoChatPdaSize, DefaultPdaSize);

        private static Animation BuildResizeAnimation(Vector2 from, Vector2 to)
        {
            return new Animation
            {
                Length = TimeSpan.FromSeconds(0.15),
                AnimationTracks =
                {
                    new AnimationTrackControlProperty
                    {
                        Property = "SetSize",
                        InterpolationMode = AnimationInterpolationMode.Linear,
                        KeyFrames =
                        {
                            new AnimationTrackProperty.KeyFrame(from, 0f),
                            new AnimationTrackProperty.KeyFrame(to, 0.15f),
                        },
                    },
                },
            };
        }

        private readonly PdaSystem _pdaSystem;

        [ViewVariables]
        private PdaMenu? _menu;

        public PdaBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
            _pdaSystem = EntMan.System<PdaSystem>();
        }

        protected override void Open()
        {
            base.Open();

            if (_menu == null)
                CreateMenu();
        }

        private void CreateMenu()
        {
            _menu = this.CreateWindowCenteredLeft<PdaMenu>();

            _menu.FlashLightToggleButton.OnToggled += _ =>
            {
                SendMessage(new PdaToggleFlashlightMessage());
            };

            _menu.EjectIdButton.OnPressed += _ =>
            {
                SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaIdSlotId));
            };

            _menu.EjectPenButton.OnPressed += _ =>
            {
                SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaPenSlotId));
            };

            _menu.EjectPaiButton.OnPressed += _ =>
            {
                SendPredictedMessage(new ItemSlotButtonPressedEvent(PdaComponent.PdaPaiSlotId));
            };

            _menu.ActivateMusicButton.OnPressed += _ =>
            {
                SendMessage(new PdaShowMusicMessage());
            };

            _menu.AccessRingtoneButton.OnPressed += _ =>
            {
                SendMessage(new PdaShowRingtoneMessage());
            };

            _menu.ShowUplinkButton.OnPressed += _ =>
            {
                SendMessage(new PdaShowUplinkMessage());
            };

            _menu.LockUplinkButton.OnPressed += _ =>
            {
                SendMessage(new PdaLockUplinkMessage());
            };

            _menu.OnProgramItemPressed += ActivateCartridge;
            _menu.OnInstallButtonPressed += InstallCartridge;
            _menu.OnUninstallButtonPressed += UninstallCartridge;
            _menu.ProgramCloseButton.OnPressed += _ => DeactivateActiveCartridge();

            var borderColorComponent = GetBorderColorComponent();
            if (borderColorComponent == null)
                return;

            _menu.BorderColor = borderColorComponent.BorderColor;
            _menu.AccentHColor = borderColorComponent.AccentHColor;
            _menu.AccentVColor = borderColorComponent.AccentVColor;
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not PdaUpdateState updateState)
                return;

            if (_menu == null)
            {
                _pdaSystem.Log.Error("PDA state received before menu was created.");
                return;
            }

            _menu.UpdateState(updateState);
        }

        protected override void AttachCartridgeUI(Control cartridgeUIFragment, string? title)
        {
            _menu?.ProgramView.AddChild(cartridgeUIFragment);
            _menu?.ToProgramView(title ?? Loc.GetString("comp-pda-io-program-fallback-title"));

            // NanoChat is dense enough (contact list + message pane) to want more room -
            // grow the window while it's open, then shrink back on close.
            if (cartridgeUIFragment is NanoChatUiFragment && _menu != null)
                _menu.PlayAnimation(ResizeToNanoChat, ResizeAnimationKey);
        }

        protected override void DetachCartridgeUI(Control cartridgeUIFragment)
        {
            if (_menu is null)
                return;

            _menu.ToHomeScreen();
            _menu.HideProgramHeader();
            _menu.ProgramView.RemoveChild(cartridgeUIFragment);

            if (cartridgeUIFragment is NanoChatUiFragment)
                _menu.PlayAnimation(ResizeToDefault, ResizeAnimationKey);
        }

        protected override void UpdateAvailablePrograms(List<(EntityUid, CartridgeComponent)> programs)
        {
            _menu?.UpdateAvailablePrograms(programs);
        }

        private PdaBorderColorComponent? GetBorderColorComponent()
        {
            return EntMan.GetComponentOrNull<PdaBorderColorComponent>(Owner);
        }
    }
}
