using System.Linq;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared.Administration;
using Content.Shared.Database;
using Content.Shared.Eui;
using Robust.Server.Player;
using Robust.Shared.Network;
using DbAdminRank = Content.Server.Database.AdminRank;
using static Content.Shared.Administration.PermissionsEuiMsg;


namespace Content.Server.Administration.UI
{
    // _Duty-start: файл целиком переписан под дерево админ-прав (права — строки: старый флаг или id узла)
    // _Duty: права — строки: старый флаг (BAN) либо id узла дерева прав (players_ban).
    // Каждую правку проверяем ДО записи в БД: выдать можно только то, что есть у самого редактора.
    public sealed class PermissionsEui : BaseEui
    {
        /// <summary>Узел, дающий право открывать панель выдачи прав (вместе со старым флагом Permissions).</summary>
        public const string EditNode = "perms_edit";

        private const int MaxTitleLength = 64;
        private const int MaxRankNameLength = 64;

        [Dependency] private readonly IPlayerManager _playerManager = default!;
        [Dependency] private readonly IServerDbManager _db = default!;
        [Dependency] private readonly IAdminManager _adminManager = default!;
        [Dependency] private readonly IAdminLogManager _adminLog = default!;
        [Dependency] private readonly ILogManager _logManager = default!;

        private readonly ISawmill _sawmill;
        private bool _isLoading;

        private readonly List<(Admin a, string? lastUserName)> _admins = new List<(Admin, string? lastUserName)>();
        private readonly List<DbAdminRank> _adminRanks = new();

        public PermissionsEui()
        {
            IoCManager.InjectDependencies(this);
            _sawmill = _logManager.GetSawmill("admin.perms");
        }

        public override void Opened()
        {
            base.Opened();

            StateDirty();
            LoadFromDb();
            _adminManager.OnPermsChanged += AdminManagerOnPermsChanged;
        }

        public override void Closed()
        {
            base.Closed();

            _adminManager.OnPermsChanged -= AdminManagerOnPermsChanged;
        }

        private void AdminManagerOnPermsChanged(AdminPermsChangedEventArgs obj)
        {
            // Close UI if user loses the right to edit permissions.
            if (obj.Player == Player)
            {
                if (!CanEdit())
                    Close();
                else
                    StateDirty();
            }
        }

        private bool CanEdit()
        {
            var data = _adminManager.GetAdminData(Player);
            return data != null && (data.HasNode(EditNode) || data.HasDirectFlag(AdminFlags.Permissions));
        }

        private static string[] ToStrings(IEnumerable<string> names) => names.ToArray();

        /// <summary>Сообщает клиенту итог действия: без этого отказ выглядел как «ничего не произошло».</summary>
        private void Report(bool success, string locKey, params (string, object)[] args)
        {
            if (IsShutDown)
                return;

            SendMessage(new OperationResult { Success = success, Message = Loc.GetString(locKey, args) });
        }

        private void Fail(string locKey, params (string, object)[] args) => Report(false, locKey, args);

        public override EuiStateBase GetNewState()
        {
            if (_isLoading)
            {
                return new PermissionsEuiState
                {
                    IsLoading = true
                };
            }

            var editor = _adminManager.GetAdminData(Player);
            var editorGrants = new List<string>();
            if (editor != null)
            {
                // Host выдаёт любые старые флаги, даже если напрямую у него только HOST
                var flags = editor.HasDirectFlag(AdminFlags.Host) ? AdminFlagsHelper.Everything : editor.DirectFlags;
                editorGrants.AddRange(AdminFlagsHelper.FlagsToNames(flags));
                editorGrants.AddRange(editor.Nodes);
            }

            return new PermissionsEuiState
            {
                EditorGrants = editorGrants.ToArray(),
                Admins = _admins.Select(p => new PermissionsEuiState.AdminData
                {
                    Pos = ToStrings(p.a.Flags.Where(f => !f.Negative).Select(f => f.Flag)),
                    Neg = ToStrings(p.a.Flags.Where(f => f.Negative).Select(f => f.Flag)),
                    Title = p.a.Title,
                    RankId = p.a.AdminRankId,
                    UserId = new NetUserId(p.a.UserId),
                    UserName = p.lastUserName,
                    Suspended = p.a.Suspended,
                }).ToArray(),

                AdminRanks = _adminRanks.ToDictionary(a => a.Id, a => new PermissionsEuiState.AdminRankData
                {
                    Grants = ToStrings(a.Flags.Select(p => p.Flag)),
                    Name = a.Name
                })
            };
        }

        public override async void HandleMessage(EuiMessageBase msg)
        {
            base.HandleMessage(msg);

            // _Duty: base мог закрыть окно по CloseEuiMessage, повторно закрывать нельзя
            if (IsShutDown)
                return;

            // _Duty: право правки могли отозвать, пока окно было открыто
            if (!CanEdit())
            {
                _sawmill.Warning($"{Player} отправил правку прав без права на неё");
                Close();
                return;
            }

            try
            {
                switch (msg)
                {
                    case AddAdmin ca:
                    {
                        await HandleCreateAdmin(ca);
                        break;
                    }

                    case UpdateAdmin ua:
                    {
                        await HandleUpdateAdmin(ua);
                        break;
                    }

                    case RemoveAdmin ra:
                    {
                        await HandleRemoveAdmin(ra);
                        break;
                    }

                    case AddAdminRank ar:
                    {
                        await HandleAddAdminRank(ar);
                        break;
                    }

                    case UpdateAdminRank ur:
                    {
                        await HandleUpdateAdminRank(ur);
                        break;
                    }

                    case RemoveAdminRank ra:
                    {
                        await HandleRemoveAdminRank(ra);
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                // async void: без перехвата ошибка БД уронила бы процесс
                _sawmill.Error($"Ошибка при правке прав ({msg.GetType().Name}) от {Player}: {e}");
                Fail("duty-perm-result-error");
            }

            if (!IsShutDown)
            {
                LoadFromDb();
            }
        }

        private async Task HandleRemoveAdminRank(RemoveAdminRank rr)
        {
            var rank = await _db.GetAdminRankAsync(rr.Id);
            if (rank == null)
            {
                Fail("duty-perm-result-not-found");
                return;
            }

            if (!CanTouchRank(rank))
            {
                _sawmill.Warning($"{Player} tried to remove higher-ranked admin rank {rank.Name}");
                Fail("duty-perm-result-no-access");
                return;
            }

            await _db.RemoveAdminRankAsync(rr.Id);
            Report(true, "duty-perm-result-rank-removed", ("name", rank.Name));

            _adminLog.Add(LogType.AdminCommands, LogImpact.Extreme, $"{Player} удалил админ-ранг {rank.Name}");
            _adminManager.ReloadAdminsWithRank(rr.Id);
        }

        private async Task HandleUpdateAdminRank(UpdateAdminRank ur)
        {
            var rank = await _db.GetAdminRankAsync(ur.Id);
            if (rank == null)
            {
                Fail("duty-perm-result-not-found");
                return;
            }

            if (!CanTouchRank(rank))
            {
                _sawmill.Warning($"{Player} tried to update higher-ranked admin rank {rank.Name}");
                Fail("duty-perm-result-no-access");
                return;
            }

            if (!ValidateRank(ur.Name, ur.Grants, out var name, out var grants))
                return;

            rank.Flags = GenRankFlagList(grants);
            rank.Name = name;

            await _db.UpdateAdminRankAsync(rank);

            var flagText = string.Join(' ', grants.Select(f => $"+{f}"));
            _sawmill.Info($"{Player} updated admin rank {rank.Name}/{flagText}.");
            _adminLog.Add(LogType.AdminCommands, LogImpact.Extreme, $"{Player} изменил админ-ранг {rank.Name}: {flagText}");
            Report(true, "duty-perm-result-rank-saved", ("name", rank.Name));

            _adminManager.ReloadAdminsWithRank(ur.Id);
        }

        private async Task HandleAddAdminRank(AddAdminRank ar)
        {
            if (!ValidateRank(ar.Name, ar.Grants, out var name, out var grants))
                return;

            var rank = new DbAdminRank
            {
                Name = name,
                Flags = GenRankFlagList(grants)
            };

            await _db.AddAdminRankAsync(rank);

            var flagText = string.Join(' ', grants.Select(f => $"+{f}"));
            _sawmill.Info($"{Player} added admin rank {rank.Name}/{flagText}.");
            _adminLog.Add(LogType.AdminCommands, LogImpact.Extreme, $"{Player} создал админ-ранг {rank.Name}: {flagText}");
            Report(true, "duty-perm-result-rank-saved", ("name", rank.Name));
        }

        private async Task HandleRemoveAdmin(RemoveAdmin ra)
        {
            var admin = await _db.GetAdminDataForAsync(ra.UserId);
            if (admin == null)
            {
                // Doesn't exist.
                Fail("duty-perm-result-not-found");
                return;
            }

            if (!CanTouchAdmin(admin))
            {
                _sawmill.Warning($"{Player} tried to remove higher-ranked admin {ra.UserId.ToString()}");
                Fail("duty-perm-result-no-access");
                return;
            }

            await _db.RemoveAdminAsync(ra.UserId);

            var record = await _db.GetPlayerRecordByUserId(ra.UserId);
            var removedName = record?.LastSeenUserName ?? ra.UserId.ToString();
            _sawmill.Info($"{Player} removed admin {removedName}");
            _adminLog.Add(LogType.AdminCommands, LogImpact.Extreme, $"{Player} снял админа {removedName}");
            Report(true, "duty-perm-result-admin-removed", ("name", removedName));

            if (_playerManager.TryGetSessionById(ra.UserId, out var player))
            {
                _adminManager.ReloadAdmin(player);
            }
        }

        private async Task HandleUpdateAdmin(UpdateAdmin ua)
        {
            if (!ValidateAdmin(ua.Title, ua.Pos, ua.Neg, out var pos, out var neg))
                return;

            var admin = await _db.GetAdminDataForAsync(ua.UserId);
            if (admin == null)
            {
                // Was removed in the mean time I guess?
                Fail("duty-perm-result-not-found");
                return;
            }

            if (!CanTouchAdmin(admin))
            {
                _sawmill.Warning($"{Player} tried to modify higher-ranked admin {ua.UserId.ToString()}");
                Fail("duty-perm-result-no-access");
                return;
            }

            // _Duty: ранг проверяем ДО записи в БД. Раньше запись шла первой, и проверка уже ничего не откатывала:
            // можно было записать себе ранг с правами выше своих.
            var (bad, rankName) = await FetchAndCheckRank(ua.RankId);
            if (bad)
            {
                return;
            }

            admin.Title = ua.Title;
            admin.AdminRankId = ua.RankId;
            admin.Flags = GenAdminFlagList(pos, neg);
            admin.Suspended = ua.Suspended;

            await _db.UpdateAdminAsync(admin);

            var playerRecord = await _db.GetPlayerRecordByUserId(ua.UserId);

            var name = playerRecord?.LastSeenUserName ?? ua.UserId.ToString();
            var title = ua.Title ?? "<no title>";
            var flags = PosNegText(pos, neg);

            _sawmill.Info($"{Player} updated admin {name} to {title}/{rankName}/{flags}");
            _adminLog.Add(LogType.AdminCommands, LogImpact.Extreme, $"{Player} изменил права админа {name}: {title}/{rankName}/{flags}");
            Report(true, "duty-perm-result-admin-saved", ("name", name));

            if (_playerManager.TryGetSessionById(ua.UserId, out var player))
            {
                _adminManager.ReloadAdmin(player);
            }
        }

        private async Task HandleCreateAdmin(AddAdmin ca)
        {
            if (!ValidateAdmin(ca.Title, ca.Pos, ca.Neg, out var pos, out var neg))
                return;

            string name;
            NetUserId userId;
            if (Guid.TryParse(ca.UserNameOrId, out var guid))
            {
                userId = new NetUserId(guid);
                var playerRecord = await _db.GetPlayerRecordByUserId(userId);
                if (playerRecord == null)
                {
                    // _Duty: выдавать права несуществующему Guid нельзя (раньше запись создавалась)
                    _sawmill.Warning($"{Player} tried to add admin with unknown id {ca.UserNameOrId}.");
                    Fail("duty-perm-result-unknown-player", ("name", ca.UserNameOrId));
                    return;
                }

                name = playerRecord.LastSeenUserName;
            }
            else
            {
                // Username entered, resolve user ID from DB.
                var dbPlayer = await _db.GetPlayerRecordByUserName(ca.UserNameOrId);
                if (dbPlayer == null)
                {
                    // username not in DB.
                    _sawmill.Warning($"{Player} tried to add admin with unknown username {ca.UserNameOrId}.");
                    Fail("duty-perm-result-unknown-player", ("name", ca.UserNameOrId));
                    return;
                }

                userId = dbPlayer.UserId;
                name = ca.UserNameOrId;
            }

            var existing = await _db.GetAdminDataForAsync(userId);
            if (existing != null)
            {
                // Already exists.
                Fail("duty-perm-result-already-admin", ("name", name));
                return;
            }

            var (bad, rankName) = await FetchAndCheckRank(ca.RankId);
            if (bad)
            {
                return;
            }

            rankName ??= "<no rank>";

            var admin = new Admin
            {
                Flags = GenAdminFlagList(pos, neg),
                AdminRankId = ca.RankId,
                UserId = userId.UserId,
                Title = ca.Title,
                Suspended = ca.Suspended,
            };

            await _db.AddAdminAsync(admin);

            var title = ca.Title ?? "<no title>";
            var flags = PosNegText(pos, neg);

            _sawmill.Info($"{Player} added admin {name} as {title}/{rankName}/{flags}");
            _adminLog.Add(LogType.AdminCommands, LogImpact.Extreme, $"{Player} добавил админа {name}: {title}/{rankName}/{flags}");
            Report(true, "duty-perm-result-admin-saved", ("name", name));

            if (_playerManager.TryGetSessionById(userId, out var player))
            {
                _adminManager.ReloadAdmin(player);
            }
        }

        private bool ValidateAdmin(string? title, string[] rawPos, string[] rawNeg, out string[] pos, out string[] neg)
        {
            neg = Array.Empty<string>();
            if (!TryNormalize(rawPos, out pos) || !TryNormalize(rawNeg, out neg))
            {
                Fail("duty-perm-result-unknown-grant");
                return false;
            }

            if (title is { Length: > MaxTitleLength })
            {
                Fail("duty-perm-result-bad-title", ("max", MaxTitleLength));
                return false;
            }

            if (pos.Intersect(neg).Any())
            {
                // Can't have overlapping pos and neg flags.
                Fail("duty-perm-result-conflict");
                return false;
            }

            if (!HoldsAll(pos))
            {
                // Can't create an admin with higher perms than yourself, obviously.
                _sawmill.Warning($"{Player} tried to grant admin powers above their authorization.");
                Fail("duty-perm-result-no-access");
                return false;
            }

            return true;
        }

        private bool ValidateRank(string rawName, string[] rawGrants, out string name, out string[] grants)
        {
            grants = Array.Empty<string>();
            if (!ValidateName(rawName, MaxRankNameLength, out name))
            {
                Fail("duty-perm-result-bad-name", ("max", MaxRankNameLength));
                return false;
            }

            if (!TryNormalize(rawGrants, out grants))
            {
                Fail("duty-perm-result-unknown-grant");
                return false;
            }

            if (!HoldsAll(grants))
            {
                _sawmill.Warning($"{Player} tried to give a rank permissions above their authorization.");
                Fail("duty-perm-result-no-access");
                return false;
            }

            return true;
        }

        private async Task<(bool bad, string?)> FetchAndCheckRank(int? rankId)
        {
            string? ret = null;
            if (rankId is { } r)
            {
                var rank = await _db.GetAdminRankAsync(r);
                if (rank == null)
                {
                    // Tried to set to nonexistent rank.
                    _sawmill.Warning($"{Player} tried to assign nonexistent admin rank.");
                    Fail("duty-perm-result-not-found");
                    return (true, null);
                }

                ret = rank.Name;

                if (!HoldsAll(rank.Flags.Select(p => p.Flag)))
                {
                    // Can't assign a rank with flags you don't have yourself.
                    _sawmill.Warning($"{Player} tried to assign admin rank above their authorization.");
                    Fail("duty-perm-result-rank-too-high", ("name", rank.Name));
                    return (true, null);
                }
            }

            return (false, ret);
        }

        private async void LoadFromDb()
        {
            try
            {
                StateDirty();
                _isLoading = true;
                var (admins, ranks) = await _db.GetAllAdminAndRanksAsync();

                _admins.Clear();
                _admins.AddRange(admins);
                _adminRanks.Clear();
                _adminRanks.AddRange(ranks);
            }
            catch (Exception e)
            {
                _sawmill.Error($"Не удалось загрузить админов из БД: {e}");
            }
            finally
            {
                _isLoading = false;
                StateDirty();
            }
        }

        private List<AdminFlag> GenAdminFlagList(string[] pos, string[] neg)
        {
            return pos.Select(f => new AdminFlag {Negative = false, Flag = f})
                .Concat(neg.Select(f => new AdminFlag {Negative = true, Flag = f}))
                .ToList();
        }

        private static List<AdminRankFlag> GenRankFlagList(IEnumerable<string> grants)
        {
            return grants.Select(f => new AdminRankFlag {Flag = f}).ToList();
        }

        private static string PosNegText(string[] pos, string[] neg)
        {
            return string.Join(' ', pos.Select(f => $"+{f}").Concat(neg.Select(f => $"-{f}")));
        }

        /// <summary>
        /// Разбирает присланные права: каждое имя должно быть известным старым флагом или узлом дерева.
        /// Старые флаги приводятся к верхнему регистру. Дубликаты убираются.
        /// </summary>
        private bool TryNormalize(string[] raw, out string[] result)
        {
            var tree = _adminManager.PermissionTree;
            var set = new List<string>();
            foreach (var item in raw)
            {
                var name = item.Trim();
                // Сначала узел как есть: id областей (debug, server, host, fun, logs, round) совпадают с именами
                // старых флагов без учёта регистра, и без этого область превращалась бы в старый флаг.
                if (tree.Exists(name))
                {
                    // узел
                }
                else if (AdminFlagsHelper.TryNameToFlag(name.ToUpperInvariant(), out _))
                    name = name.ToUpperInvariant();
                else
                {
                    _sawmill.Warning($"{Player} прислал неизвестное право '{item}'");
                    result = Array.Empty<string>();
                    return false;
                }

                if (!set.Contains(name))
                    set.Add(name);
            }

            result = set.ToArray();
            return true;
        }

        private static bool ValidateName(string raw, int maxLength, out string name)
        {
            name = raw.Trim();
            return name.Length > 0 && name.Length <= maxLength;
        }

        /// <summary>Есть ли у редактора это право: старый флаг, выданный напрямую, либо узел (родитель включает вложенные).</summary>
        private bool UserHolds(string grant)
        {
            var data = _adminManager.GetAdminData(Player);
            if (data == null)
                return false;

            if (AdminFlagsHelper.TryNameToFlag(grant, out var flag))
                return data.HasDirectFlag(flag) || data.HasDirectFlag(AdminFlags.Host);

            return data.HasNode(grant);
        }

        private bool HoldsAll(IEnumerable<string> grants)
        {
            // Неизвестное имя в БД (устарело) права не даёт и редактору мешать не должно.
            var tree = _adminManager.PermissionTree;
            foreach (var g in grants)
            {
                if (!AdminFlagsHelper.TryNameToFlag(g, out _) && !tree.Exists(g))
                    continue;

                if (!UserHolds(g))
                    return false;
            }

            return true;
        }

        private bool CanTouchAdmin(Admin admin)
        {
            var grants = admin.Flags.Where(f => !f.Negative).Select(f => f.Flag)
                .Concat(admin.AdminRank?.Flags.Select(f => f.Flag) ?? Enumerable.Empty<string>());

            return HoldsAll(grants);
        }

        private bool CanTouchRank(DbAdminRank rank)
        {
            return HoldsAll(rank.Flags.Select(f => f.Flag));
        }
    }
// _Duty-end
}
