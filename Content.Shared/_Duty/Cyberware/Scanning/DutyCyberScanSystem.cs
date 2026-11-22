// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.HealthExaminable;
using Content.Shared.Implants.Components;

namespace Content.Shared._Duty.Cyberware.Scanning;

/// <summary>
/// _Duty: видимость имплантов для сканеров и осмотра. Анализатор здоровья показывает все импланты
/// (кибер с <see cref="DutyCyberwareComponent.Concealed"/> — как «неопознанный модуль»), а осмотр
/// собственного тела добавляет строку «Под кожей: …» только про кибер-железо.
/// Лежит в Shared, потому что осмотр (<see cref="HealthExaminableSystem"/>) собирается на клиенте.
/// </summary>
public sealed partial class DutyCyberScanSystem : EntitySystem
{
    // Один и тот же список используется в обоих сценариях, поэтому держим буфер, а не аллоцируем на каждый вызов.
    private readonly List<(string Name, int Count)> _buffer = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ImplantedComponent, HealthBeingExaminedEvent>(OnHealthExamined);
    }

    private void OnHealthExamined(Entity<ImplantedComponent> ent, ref HealthBeingExaminedEvent args)
    {
        // Железо под кожей чувствует только сам носитель; чужой осмотр ничего не видит.
        if (args.Examiner != ent.Owner)
            return;

        var lines = GetImplantLines((ent.Owner, ent.Comp), cyberOnly: true);
        if (lines.Count == 0)
            return;

        args.Message.PushNewline();
        args.Message.AddText(Loc.GetString("duty-cyber-scan-self-examine",
            ("modules", string.Join(", ", lines))));
    }

    /// <summary>
    /// Список импланта носителя в виде «Имя» / «Имя ×N». Одинаковые по имени сворачиваются в одну строку.
    /// </summary>
    /// <param name="cyberOnly">Только импланты с <see cref="DutyCyberwareComponent"/> (для осмотра себя).</param>
    public List<string> GetImplantLines(Entity<ImplantedComponent?> target, bool cyberOnly = false)
    {
        var result = new List<string>();
        if (!Resolve(target, ref target.Comp, false) || target.Comp.ImplantContainer == null)
            return result;

        _buffer.Clear();
        foreach (var implant in target.Comp.ImplantContainer.ContainedEntities)
        {
            string name;
            if (TryComp<DutyCyberwareComponent>(implant, out var cyber))
            {
                name = cyber.Concealed
                    ? Loc.GetString("duty-cyber-scan-concealed")
                    : Name(implant);
            }
            else if (cyberOnly)
            {
                continue;
            }
            else
            {
                name = Name(implant);
            }

            var index = _buffer.FindIndex(e => e.Name == name);
            if (index >= 0)
                _buffer[index] = (name, _buffer[index].Count + 1);
            else
                _buffer.Add((name, 1));
        }

        foreach (var (name, count) in _buffer)
        {
            result.Add(count > 1
                ? Loc.GetString("duty-cyber-scan-stack", ("name", name), ("count", count))
                : name);
        }

        return result;
    }
}
