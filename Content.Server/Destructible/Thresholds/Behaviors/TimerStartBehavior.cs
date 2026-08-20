//(Откат PR - https://github.com/space-wizards/space-station-14/pull/32429)
using Content.Shared.Trigger.Components;

namespace Content.Server.Destructible.Thresholds.Behaviors;

[DataDefinition]
public sealed partial class TimerStartBehavior : IThresholdBehavior
{
    public void Execute(EntityUid owner, DestructibleSystem system, EntityUid? cause = null)
    {
        // Некоторые взрывчатки (например AirGrenade через RemoveComponentsOnTrigger) снимают с себя
        // TimerTriggerComponent после первого срабатывания. Если уже сработавшую гранату задевает
        // соседним взрывом, порог всё равно пытается запустить таймер повторно — без этой проверки
        // ActivateTimerTrigger падает в "Can't resolve TimerTriggerComponent".
        if (!system.EntityManager.HasComponent<TimerTriggerComponent>(owner))
            return;

        system.TriggerSystem.ActivateTimerTrigger(owner, cause);
    }
}
//Создает новый режим ограничения урона, 
//который срабатывает для взрывчатых веществ и
//заставляет их начать обратный отсчет.
//В сочетании с высокой устойчивостью к взрыву это позволяет сохранить бомбу,
//которая не была взведена/сломана и отсчитывает время после срабатывания
//от другого взрывчатого вещества.
