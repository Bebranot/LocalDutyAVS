using Content.Shared.Eui;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared.Administration
{
    // _Duty-start: файл целиком переписан под дерево админ-прав (права — строки: старый флаг или id узла)
    // _Duty: права передаются строками: либо имя старого флага (BAN), либо id узла дерева прав (players_ban).
    // Так панель и сервер не зависят от битовой маски, а новые узлы добавляются без правки протокола.
    [Serializable, NetSerializable]
    public sealed class PermissionsEuiState : EuiStateBase
    {
        public bool IsLoading;

        public AdminData[] Admins = Array.Empty<AdminData>();
        public Dictionary<int, AdminRankData> AdminRanks = new();

        /// <summary>Что может выдавать сам редактор: его старые флаги, выданные напрямую, и узлы вместе со вложенными.</summary>
        public string[] EditorGrants = Array.Empty<string>();

        [Serializable, NetSerializable]
        public struct AdminData
        {
            public NetUserId UserId;
            public string? UserName;
            public string? Title;
            public bool Suspended;
            public string[] Pos;
            public string[] Neg;
            public int? RankId;
        }

        [Serializable, NetSerializable]
        public struct AdminRankData
        {
            public string Name;
            public string[] Grants;
        }
    }

    public static class PermissionsEuiMsg
    {
        [Serializable, NetSerializable]
        public sealed class AddAdmin : EuiMessageBase
        {
            public string UserNameOrId = string.Empty;
            public string? Title;
            public string[] Pos = Array.Empty<string>();
            public string[] Neg = Array.Empty<string>();
            public int? RankId;
            public bool Suspended;
        }

        [Serializable, NetSerializable]
        public sealed class RemoveAdmin : EuiMessageBase
        {
            public NetUserId UserId;
        }

        [Serializable, NetSerializable]
        public sealed class UpdateAdmin : EuiMessageBase
        {
            public NetUserId UserId;
            public string? Title;
            public string[] Pos = Array.Empty<string>();
            public string[] Neg = Array.Empty<string>();
            public int? RankId;
            public bool Suspended;
        }


        [Serializable, NetSerializable]
        public sealed class AddAdminRank : EuiMessageBase
        {
            public string Name = string.Empty;
            public string[] Grants = Array.Empty<string>();
        }

        [Serializable, NetSerializable]
        public sealed class RemoveAdminRank : EuiMessageBase
        {
            public int Id;
        }

        [Serializable, NetSerializable]
        public sealed class UpdateAdminRank : EuiMessageBase
        {
            public int Id;

            public string Name = string.Empty;
            public string[] Grants = Array.Empty<string>();
        }

        /// <summary>Итог действия с сервера: показывается в панели, чтобы отказ не выглядел как «ничего не произошло».</summary>
        [Serializable, NetSerializable]
        public sealed class OperationResult : EuiMessageBase
        {
            public bool Success;
            public string Message = string.Empty;
        }
    }
// _Duty-end
}
