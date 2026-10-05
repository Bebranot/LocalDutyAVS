// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Server.Administration.Managers;
using Content.Shared._Duty.Administration;
using Content.Shared.Administration;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// Целостность дерева админ-прав: без циклов и битых родителей, у каждого узла есть название и описание,
/// старые флаги в <c>legacy</c> существуют, а у областей (узлов с детьми) нет своих команд.
/// </summary>
[TestFixture]
[FixtureLifeCycle(LifeCycle.SingleInstance)]
[TestOf(typeof(AdminPermissionPrototype))]
public sealed class AdminPermissionTreeTests
{
    [Test]
    public async Task TreeIsConsistent()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        await server.WaitIdleAsync();

        var proto = server.ResolveDependency<IPrototypeManager>();
        var loc = server.ResolveDependency<ILocalizationManager>();
        var admin = server.ResolveDependency<IAdminManager>();

        await server.WaitAssertion(() =>
        {
            var tree = new AdminPermissionTree(proto);

            Assert.Multiple(() =>
            {
                Assert.That(tree.Errors, Is.Empty, string.Join("; ", tree.Errors));
                Assert.That(admin.PermissionTreeProblems, Is.Empty, string.Join("; ", admin.PermissionTreeProblems));

                // узлы, на которые ссылается код, должны существовать: иначе проверка в коде молча закрывает доступ
                foreach (var field in typeof(AdminNodes).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    var id = (string) field.GetValue(null)!;
                    Assert.That(tree.Exists(id), $"AdminNodes.{field.Name} = '{id}': такого узла нет в дереве прав");
                }

                foreach (var node in tree.All)
                {
                    Assert.That(loc.TryGetString(node.NameLoc, out _), $"Нет названия узла прав '{node.ID}' ({node.NameLoc})");
                    Assert.That(loc.TryGetString(node.DescLoc, out _), $"Нет описания узла прав '{node.ID}' ({node.DescLoc})");

                    // id узла не должен совпадать с именем старого флага без учёта регистра: оба хранятся в БД строками
                    Assert.That(Enum.TryParse<AdminFlags>(node.ID, true, out _), Is.False,
                        $"Id узла прав '{node.ID}' совпадает с именем старого флага");

                    foreach (var l in node.Legacy)
                    {
                        Assert.That(Enum.TryParse<AdminFlags>(l, true, out _), $"Узел '{node.ID}': неизвестный старый флаг '{l}'");
                    }

                    if (tree.ChildrenOf(node.ID).Count > 0)
                    {
                        Assert.That(node.Commands.Concat(node.Toolshed), Is.Empty,
                            $"Узел '{node.ID}' — область с вложенными узлами, команды должны быть у вложенных");
                        Assert.That(node.Legacy, Is.Empty,
                            $"Узел '{node.ID}' — область с вложенными узлами, старые флаги должны быть у вложенных");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }
}
