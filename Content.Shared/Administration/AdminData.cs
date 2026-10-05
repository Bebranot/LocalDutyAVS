using Content.Shared._Duty.Administration;


namespace Content.Shared.Administration
{
    /// <summary>
    ///     Represents data for a single server admin.
    /// </summary>
    public sealed class AdminData
    {
        // Can be false if they're de-adminned with the ability to re-admin.
        /// <summary>
        ///     Whether the admin is currently active. This can be false if they have de-adminned mid-round.
        /// </summary>
        public bool Active;

        /// <summary>
        /// Whether the admin is in stealth mode and won't appear in adminwho to admins without the Stealth flag.
        /// </summary>
        public bool Stealth;

        /// <summary>
        ///     The admin's title.
        /// </summary>
        public string? Title;

        /// <summary>
        ///     The admin's permission flags.
        /// </summary>
        public AdminFlags Flags;

        // _Duty-start: дерево прав. Flags = старые флаги, выданные напрямую, плюс то, что включают узлы (для HasAdminFlag в системах).
        /// <summary>
        ///     Старые флаги, выданные напрямую. Только они (а не включённые узлами) открывают консольные команды по атрибутам.
        ///     Заполняется на сервере, клиенту не передаётся.
        /// </summary>
        public AdminFlags DirectFlags;

        /// <summary>
        ///     Выданные узлы прав вместе со всеми вложенными (см. AdminPermissionPrototype).
        /// </summary>
        public HashSet<string> Nodes = new();

        public bool HasNode(string node, bool includeDeAdmin = false)
        {
            return (includeDeAdmin || Active) && Nodes.Contains(node);
        }

        public bool HasDirectFlag(AdminFlags flag)
        {
            return Active && (DirectFlags & flag) == flag;
        }
        // _Duty-end

        /// <summary>
        ///     Checks whether this admin has an admin flag.
        /// </summary>
        /// <param name="flag">The flags to check. Multiple flags can be specified, they must all be held.</param>
        /// <param name="includeDeAdmin">If true then also count flags even if the admin has de-adminned.</param>
        /// <returns>False if this admin is not <see cref="Active"/> or does not have all the flags specified.</returns>
        public bool HasFlag(AdminFlags flag, bool includeDeAdmin = false)
        {
            return (includeDeAdmin || Active) && (Flags & flag) == flag;
        }

        /// <summary>
        ///     Check if this admin can spawn stuff in with the entity/tile spawn panel.
        /// </summary>
        public bool CanAdminPlace()
        {
            return HasFlag(AdminFlags.Spawn);
        }

        /// <summary>
        ///     Check if this admin can execute server-side C# scripts.
        /// </summary>
        public bool CanScript()
        {
            // _Duty: скрипты открывает и узел host_script, без выдачи Host целиком
            return HasFlag(AdminFlags.Host) || HasNode(AdminNodes.HostScript);
        }

        /// <summary>
        ///     Check if this admin can open the admin menu.
        /// </summary>
        public bool CanAdminMenu()
        {
            // _Duty: меню открывает и тот, у кого есть хотя бы один узел прав
            return HasFlag(AdminFlags.Admin) || (Active && Nodes.Count > 0);
        }

        /// <summary>
        /// Check if this admin can be hidden and see other hidden admins.
        /// </summary>
        public bool CanStealth()
        {
            return HasFlag(AdminFlags.Stealth);
        }

        public bool CanAdminReloadPrototypes()
        {
            // _Duty: перезагрузка прототипов идёт вместе с их загрузкой (узел host_upload)
            return HasFlag(AdminFlags.Host) || HasNode(AdminNodes.HostUpload);
        }
    }
}
