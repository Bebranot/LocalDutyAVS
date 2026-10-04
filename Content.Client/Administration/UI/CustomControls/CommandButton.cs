using System.Diagnostics.CodeAnalysis;
using Content.Client._Duty.Administration;
using Content.Client.Administration.Managers;
using Content.Client.Guidebook.Richtext;
using Robust.Client.Console;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Administration.UI.CustomControls
{
    [Virtual]
    public class CommandButton : Button, IDocumentTag
    {
        public string? Command { get; set; }

        public CommandButton()
        {
            OnPressed += Execute;
        }

        protected virtual bool CanPress()
        {
            return string.IsNullOrEmpty(Command) ||
                   IoCManager.Resolve<IClientConGroupController>().CanCommand(Command.Split(' ')[0]);
        }

        // _Duty-start: кнопка перепроверяет права при их смене, а не только при первом показе; подсказка называет команду и право
        public bool IsAllowed => CanPress();

        /// <summary>Нажать кнопку программно (для результатов поиска в меню F7).</summary>
        public void Activate()
        {
            if (CanPress())
                Execute(null!);
        }

        // Скрыто именно из-за прав (а не по Visible="False" в XAML): только это возвращаем при смене прав
        private bool _hiddenByPermissions;

        protected override void EnteredTree()
        {
            ApplyPermissions();
            IoCManager.Resolve<IClientAdminManager>().AdminStatusUpdated += OnStatusUpdated;

            if (ToolTip == null && !string.IsNullOrEmpty(Command))
            {
                var name = Command.Split(' ')[0];
                var nodes = AdminNodeLookup.NodeNames(name);
                ToolTip = string.IsNullOrEmpty(nodes)
                    ? Loc.GetString("admin-menu-button-tooltip", ("command", name))
                    : Loc.GetString("admin-menu-button-tooltip-node", ("command", name), ("nodes", nodes));
            }
        }

        protected override void ExitedTree()
        {
            IoCManager.Resolve<IClientAdminManager>().AdminStatusUpdated -= OnStatusUpdated;
        }

        private void OnStatusUpdated()
        {
            ApplyPermissions();
        }

        private void ApplyPermissions()
        {
            if (!CanPress())
            {
                Visible = false;
                _hiddenByPermissions = true;
            }
            else if (_hiddenByPermissions)
            {
                Visible = true;
                _hiddenByPermissions = false;
            }
        }
        // _Duty-end

        protected virtual void Execute(ButtonEventArgs obj)
        {
            // Default is to execute command
            if (!string.IsNullOrEmpty(Command))
                IoCManager.Resolve<IClientConsoleHost>().ExecuteCommand(Command);
        }

        public bool TryParseTag(Dictionary<string, string> args, [NotNullWhen(true)] out Control? control)
        {
            if (args.Count != 2 || !args.TryGetValue("Text", out var text) || !args.TryGetValue("Command", out var command))
            {
                Logger.Error($"Invalid arguments passed to {nameof(CommandButton)}");
                control = null;
                return false;
            }

            Command = command;
            Text = Loc.GetString(text);
            control = this;
            return true;
        }
    }
}
