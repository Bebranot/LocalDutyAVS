// SPDX-FileCopyrightText: 2026 LocalDuty <https://github.com/Bebranot/LocalDuty_Reserve>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client._Duty.Performance;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared.CCVar;
using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._Duty;

/// <summary>
/// _Duty: режим для слабых устройств. Главное обещание игроку — выключение возвращает его настройки
/// как было, а то, что он поменял руками при включённом режиме, не затирается. Ошибка тут молча
/// портит конфиг, и в игре её заметят только когда «почему-то вся графика на минимуме».
/// </summary>
[TestFixture]
[TestOf(typeof(LowEndModeUIController))]
public sealed class LowEndModeTests : GameTest
{
    [SidedDependency(Side.Client)] private readonly IConfigurationManager _cfg = null!;

    /// <summary>Значения «до» отличаются от дефолтов, чтобы восстановление нельзя было спутать со сбросом.</summary>
    private void SetUserSettings()
    {
        _cfg.SetCVar(CVars.LightResolutionScale, 1f);
        _cfg.SetCVar(CVars.LightSoftShadows, true);
        _cfg.SetCVar(CVars.MaxShadowcastingLights, 100);
        _cfg.SetCVar(CVars.RenderTileEdges, true);
        _cfg.SetCVar(CCVars.ViewportScaleRender, true);
        _cfg.SetCVar(CCVars.ParticleQuality, 3);
        _cfg.SetCVar(CCVars.AmbientCooldown, 0.1f);
        _cfg.SetCVar(CVars.AudioTickRate, 30);
        // Уже ниже порога пресета — режим не должен его поднимать.
        _cfg.SetCVar(CVars.DisplayMaxFPS, 30);
    }

    [Test]
    public async Task EnableAppliesPresetAndDisableRestores()
    {
        await Client.WaitAssertion(() =>
        {
            SetUserSettings();

            _cfg.SetCVar(DutyCCVars.LowEndMode, true);

            Assert.Multiple(() =>
            {
                Assert.That(_cfg.GetCVar(CVars.LightResolutionScale), Is.EqualTo(0.125f));
                Assert.That(_cfg.GetCVar(CVars.LightSoftShadows), Is.False);
                Assert.That(_cfg.GetCVar(CVars.MaxShadowcastingLights), Is.EqualTo(48));
                Assert.That(_cfg.GetCVar(CVars.RenderTileEdges), Is.False);
                Assert.That(_cfg.GetCVar(CCVars.ViewportScaleRender), Is.False);
                Assert.That(_cfg.GetCVar(CCVars.ParticleQuality), Is.EqualTo(1));
                Assert.That(_cfg.GetCVar(CCVars.AmbientCooldown), Is.EqualTo(0.3f));
                Assert.That(_cfg.GetCVar(CVars.AudioTickRate), Is.EqualTo(15));
                Assert.That(_cfg.GetCVar(CVars.DisplayMaxFPS), Is.EqualTo(30),
                    "Лимит FPS игрока ниже пресетного — режим не должен его поднимать.");
                Assert.That(_cfg.GetCVar(DutyCCVars.LowEndModeSaved), Is.Not.Empty);
            });

            // Игрок сам поменял частицы, пока режим включён, — это его выбор, выключение его не трогает.
            _cfg.SetCVar(CCVars.ParticleQuality, 2);

            _cfg.SetCVar(DutyCCVars.LowEndMode, false);

            Assert.Multiple(() =>
            {
                Assert.That(_cfg.GetCVar(CVars.LightResolutionScale), Is.EqualTo(1f));
                Assert.That(_cfg.GetCVar(CVars.LightSoftShadows), Is.True);
                Assert.That(_cfg.GetCVar(CVars.MaxShadowcastingLights), Is.EqualTo(100));
                Assert.That(_cfg.GetCVar(CVars.RenderTileEdges), Is.True);
                Assert.That(_cfg.GetCVar(CCVars.ViewportScaleRender), Is.True);
                Assert.That(_cfg.GetCVar(CCVars.ParticleQuality), Is.EqualTo(2),
                    "Ручная правка при включённом режиме затёрта снапшотом.");
                Assert.That(_cfg.GetCVar(CCVars.AmbientCooldown), Is.EqualTo(0.1f));
                Assert.That(_cfg.GetCVar(CVars.AudioTickRate), Is.EqualTo(30));
                Assert.That(_cfg.GetCVar(CVars.DisplayMaxFPS), Is.EqualTo(30));
                Assert.That(_cfg.GetCVar(DutyCCVars.LowEndModeSaved), Is.Empty);
            });
        });
    }

    /// <summary>
    /// Повторные циклы не должны «запекать» пресет в снапшот: второй раз выключение обязано вернуть
    /// те же исходные значения, а не пониженные.
    /// </summary>
    [Test]
    public async Task RepeatedTogglingKeepsOriginalSettings()
    {
        await Client.WaitAssertion(() =>
        {
            SetUserSettings();

            for (var i = 0; i < 3; i++)
            {
                _cfg.SetCVar(DutyCCVars.LowEndMode, true);
                _cfg.SetCVar(DutyCCVars.LowEndMode, false);
            }

            Assert.Multiple(() =>
            {
                Assert.That(_cfg.GetCVar(CVars.LightResolutionScale), Is.EqualTo(1f));
                Assert.That(_cfg.GetCVar(CVars.LightSoftShadows), Is.True);
                Assert.That(_cfg.GetCVar(CCVars.ParticleQuality), Is.EqualTo(3));
                Assert.That(_cfg.GetCVar(CVars.DisplayMaxFPS), Is.EqualTo(30));
                Assert.That(_cfg.GetCVar(DutyCCVars.LowEndModeSaved), Is.Empty);
            });
        });
    }
}
