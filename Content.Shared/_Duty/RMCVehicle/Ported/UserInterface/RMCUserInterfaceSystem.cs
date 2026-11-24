// SPDX-FileCopyrightText: 2025 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace Content.Shared._RMC14.UserInterface;

/// <summary>
/// BUI, который умеет перечитать состояние компонента после прихода стейта с сервера.
/// </summary>
public interface IRefreshableBui
{
    void Refresh();
}

/// <summary>
/// _Duty: урезанный порт RMCUserInterfaceSystem — только отложенное обновление открытых окон.
/// Обновление откладывается до Update, потому что AfterAutoHandleStateEvent приходит посреди
/// применения стейтов и окно иначе может прочитать полуобновлённые компоненты.
/// </summary>
public sealed class RMCUserInterfaceSystem : EntitySystem
{
    private readonly List<(Entity<UserInterfaceComponent?> Ent, Action<Entity<UserInterfaceComponent?>, RMCUserInterfaceSystem> Act)> _toRefresh = new();

    public void RefreshUIs<T>(Entity<UserInterfaceComponent?> uiEnt) where T : BoundUserInterface, IRefreshableBui
    {
        _toRefresh.Add((uiEnt, static (uiEnt, system) =>
        {
            if (system.TerminatingOrDeleted(uiEnt))
                return;

            if (!system.Resolve(uiEnt, ref uiEnt.Comp, false))
                return;

            foreach (var bui in uiEnt.Comp.ClientOpenInterfaces.Values)
            {
                if (bui is T ui)
                    ui.Refresh();
            }
        }));
    }

    public override void Update(float frameTime)
    {
        try
        {
            foreach (var refresh in _toRefresh)
            {
                try
                {
                    refresh.Act(refresh.Ent, this);
                }
                catch (Exception e)
                {
                    Log.Error($"Ошибка обновления окна техники\n{e}");
                }
            }
        }
        finally
        {
            _toRefresh.Clear();
        }
    }
}
