// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._Duty.Ballistics;
using NUnit.Framework;

namespace Content.Tests.Shared._Duty;

/// <summary>
/// Формула бронепробития: на ней держится баланс всех калибров, поэтому крайние случаи
/// (иммунитет, уязвимость, отрицательное пробитие экспансивных) фиксируем тестом.
/// </summary>
[TestFixture]
public sealed class DutyBallisticsTest
{
    [Test]
    [TestCase(0.4f, 0f, 0.4f)]       // без пробития броня не меняется
    [TestCase(0.4f, 0.3f, 0.58f)]    // жилет 0.4 против пули с пробитием 30%
    [TestCase(0.4f, -0.35f, 0.19f)]  // экспансивная: броня держит её сильнее
    [TestCase(0.4f, 1f, 1f)]         // полное пробитие — брони нет
    [TestCase(0.2f, -1f, 0f)]        // ниже нуля коэффициент не уходит
    [TestCase(0.4f, 5f, 1f)]         // пробитие зажато в [-1; 1]
    [TestCase(0f, 0.9f, 0f)]         // полный иммунитет пулей не пробивается
    [TestCase(1f, 0.5f, 1f)]         // без брони — без изменений
    [TestCase(1.5f, -0.5f, 1.5f)]    // уязвимость (c > 1) — не броня, не трогаем
    public void EffectiveCoefficient(float coefficient, float penetration, float expected)
    {
        Assert.That(DutyBallisticsSystem.EffectiveCoefficient(coefficient, penetration),
            Is.EqualTo(expected).Within(0.0001f));
    }
}
