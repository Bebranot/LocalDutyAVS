// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Shared.Administration;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// Разбор прав из строк БД: узлы раскрываются до вложенных, личные запреты вычитаются, Host получает все узлы,
/// неизвестные имена пропускаются, старые флаги работают как раньше.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.SingleInstance)]
public sealed class AdminPermissionResolveTests
{
    private static Admin MakeAdmin(string[] pos, string[] neg, string[]? rank = null)
    {
        var admin = new Admin { UserId = Guid.NewGuid(), Flags = new List<AdminFlag>() };
        foreach (var p in pos)
            admin.Flags.Add(new AdminFlag { Flag = p, Negative = false });
        foreach (var n in neg)
            admin.Flags.Add(new AdminFlag { Flag = n, Negative = true });
        if (rank != null)
        {
            admin.AdminRank = new AdminRank { Name = "test", Flags = new List<AdminRankFlag>() };
            foreach (var r in rank)
                admin.AdminRank.Flags.Add(new AdminRankFlag { Flag = r });
        }

        return admin;
    }

    [Test]
    public async Task ResolveRules()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitIdleAsync();
        var admins = server.ResolveDependency<IAdminManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                // область включает вложенное
                var (_, nodes) = admins.ResolveDatabaseAdmin(MakeAdmin(new[] { "players" }, Array.Empty<string>()));
                Assert.That(nodes, Does.Contain("players_ban"));
                Assert.That(nodes, Does.Contain("players"));
                Assert.That(nodes, Does.Not.Contain("chat_asay"));

                // личный запрет вычитает узел из области
                (_, nodes) = admins.ResolveDatabaseAdmin(MakeAdmin(new[] { "players" }, new[] { "players_ban" }));
                Assert.That(nodes, Does.Not.Contain("players_ban"));
                Assert.That(nodes, Does.Contain("players_kick"));

                // права ранга складываются с личными
                (_, nodes) = admins.ResolveDatabaseAdmin(MakeAdmin(new[] { "chat_asay" }, Array.Empty<string>(), new[] { "round_control" }));
                Assert.That(nodes, Is.SupersetOf(new[] { "chat_asay", "round_control" }));

                // старый флаг даёт Direct, узел его не даёт, но включает для внутренних проверок
                var (direct, _) = admins.ResolveDatabaseAdmin(MakeAdmin(new[] { "BAN" }, Array.Empty<string>()));
                Assert.That(direct, Is.EqualTo(AdminFlags.Ban));
                (direct, nodes) = admins.ResolveDatabaseAdmin(MakeAdmin(new[] { "players_ban" }, Array.Empty<string>()));
                Assert.That(direct, Is.EqualTo(AdminFlags.None));
                Assert.That(nodes, Does.Contain("players_ban"));

                // запрет старого флага
                (direct, _) = admins.ResolveDatabaseAdmin(MakeAdmin(new[] { "BAN", "FUN" }, new[] { "FUN" }));
                Assert.That(direct, Is.EqualTo(AdminFlags.Ban));

                // неизвестные имена не роняют разбор
                Assert.DoesNotThrow(() => admins.ResolveDatabaseAdmin(MakeAdmin(new[] { "no_such_node", "NOSUCHFLAG" }, Array.Empty<string>())));

                // Host получает все узлы
                (direct, nodes) = admins.ResolveDatabaseAdmin(MakeAdmin(new[] { "HOST" }, Array.Empty<string>()));
                Assert.That(direct & AdminFlags.Host, Is.EqualTo(AdminFlags.Host));
                Assert.That(nodes, Is.SupersetOf(admins.PermissionTree.AllIds()));

                // защита от бана: права на выдачу прав, Host, но не обычный модератор
                Assert.That(admins.IsProtectedFromBan(MakeAdmin(new[] { "perms_edit" }, Array.Empty<string>())), Is.True);
                Assert.That(admins.IsProtectedFromBan(MakeAdmin(new[] { "PERMISSIONS" }, Array.Empty<string>())), Is.True);
                Assert.That(admins.IsProtectedFromBan(MakeAdmin(new[] { "HOST" }, Array.Empty<string>())), Is.True);
                Assert.That(admins.IsProtectedFromBan(MakeAdmin(new[] { "players" }, Array.Empty<string>())), Is.False);
                Assert.That(admins.IsProtectedFromBan(null), Is.False);

                // Host из БД: резервные слоты
                Assert.That(admins.HasHostFlag(MakeAdmin(new[] { "HOST" }, Array.Empty<string>())), Is.True);
                Assert.That(admins.HasHostFlag(MakeAdmin(new[] { "perms_edit" }, Array.Empty<string>())), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }
}
