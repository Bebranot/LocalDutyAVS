using System.Numerics;
using Content.Shared.DamageOverlay;
using Content.Shared.Mobs;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client.DamageOverlay;

/// <summary>
/// The overlay used to display how damaged a player's attached character is.
/// </summary>
public sealed partial class DamageOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> CircleMaskShader = "GradientCircleMask";

    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    // _Duty: _critShader отключён — белый крит-оверлей заменён на CritVision/NaturalDeath
    private readonly ShaderInstance _oxygenShader;
    private readonly ShaderInstance _bruteShader;

    public MobState State = MobState.Alive;

    /// <summary>
    /// Handles the red pulsing overlay
    /// </summary>
    public float PainLevel = 0f;

    private float _oldPainLevel = 0f;

    /// <summary>
    /// Handles the darkening overlay.
    /// </summary>
    public float OxygenLevel = 0f;

    private float _oldOxygenLevel = 0f;

    /// <summary>
    /// Handles the white overlay when crit.
    /// </summary>
    public float CritLevel = 0f;

    private float _oldCritLevel = 0f;

    public float DeadLevel = 1f;

    // _Duty: плавный расчёт радиуса обзора в мировых тайлах для SoftCritical (5.5) и Critical (3.0)
    private const float CriticalVisionRadiusTiles = 3.0f;
    private const float SoftCriticalVisionRadiusTiles = 5.5f;
    private const float InactiveVisionRadiusTiles = 12.0f;
    private EntityUid? _lastEntity;
    private float _critDarknessAlpha = 0f;
    private float _critVisionRadiusTiles = InactiveVisionRadiusTiles;

    public DamageOverlay()
    {
        // TODO: Replace
        IoCManager.InjectDependencies(this);
        _oxygenShader = _prototypeManager.Index(CircleMaskShader).InstanceUnique();
        _bruteShader = _prototypeManager.Index(CircleMaskShader).InstanceUnique();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out EyeComponent? eyeComp))
            return;

        if (eyeComp.Eye == null || args.Viewport.Eye != eyeComp.Eye) // _Duty: защита от null Eye
            return;

        if (!_entityManager.TryGetComponent(_playerManager.LocalEntity, out DamageOverlayComponent? damageComp))
            return;

        // _Duty: сброс сглаженных уровней при смене привязанной сущности (респавн/призрак/вселение)
        if (_lastEntity != _playerManager.LocalEntity)
        {
            _lastEntity = _playerManager.LocalEntity;
            _oldPainLevel = 0f;
            _oldOxygenLevel = 0f;
            _oldCritLevel = 0f;
            DeadLevel = 1f;
            _critDarknessAlpha = 0f;
            _critVisionRadiusTiles = InactiveVisionRadiusTiles;
        }

        State = damageComp.CurrentState;
        CritLevel = damageComp.CritLevel;
        OxygenLevel = damageComp.OxygenLevel;
        PainLevel = damageComp.PainLevel;

        /*
         * Here's the rundown:
         * 1. There's lerping for each level so the transitions are smooth.
         * 2. There's 3 overlays, 1 for brute damage, 1 for oxygen damage (that also doubles as a crit overlay),
         * and a white one during crit that closes in as you progress towards death. When you die it slowly disappears.
         * The crit overlay also occasionally reduces its alpha as a "blink"
         */

        var viewport = args.WorldAABB;
        var handle = args.WorldHandle;
        var distance = args.ViewportBounds.Width;

        var time = (float) _timing.RealTime.TotalSeconds;
        var lastFrameTime = (float) _timing.FrameTime.TotalSeconds;

        // If they just died then lerp out the white overlay.
        if (State != MobState.Dead)
        {
            DeadLevel = 1f;
        }
        else if (!MathHelper.CloseTo(0f, DeadLevel, 0.001f))
        {
            var diff = -DeadLevel;
            DeadLevel += GetDiff(diff, lastFrameTime);
        }
        else
        {
            DeadLevel = 0f;
        }

        if (!MathHelper.CloseTo(_oldPainLevel, PainLevel, 0.001f))
        {
            var diff = PainLevel - _oldPainLevel;
            _oldPainLevel += GetDiff(diff, lastFrameTime);
        }
        else
        {
            _oldPainLevel = PainLevel;
        }

        if (!MathHelper.CloseTo(_oldOxygenLevel, OxygenLevel, 0.001f))
        {
            var diff = OxygenLevel - _oldOxygenLevel;
            _oldOxygenLevel += GetDiff(diff, lastFrameTime);
        }
        else
        {
            _oldOxygenLevel = OxygenLevel;
        }

        if (!MathHelper.CloseTo(_oldCritLevel, CritLevel, 0.001f))
        {
            var diff = CritLevel - _oldCritLevel;
            _oldCritLevel += GetDiff(diff, lastFrameTime);
        }
        else
        {
            _oldCritLevel = CritLevel;
        }

        /*
         * darknessAlphaOuter is the maximum alpha for anything outside of the larger circle
         * darknessAlphaInner (on the shader) is the alpha for anything inside the smallest circle
         *
         * outerCircleRadius is what we end at for max level for the outer circle
         * outerCircleMaxRadius is what we start at for 0 level for the outer circle
         *
         * innerCircleRadius is what we end at for max level for the inner circle
         * innerCircleMaxRadius is what we start at for 0 level for the inner circle
         */

        // Makes debugging easier don't @ me
        float level = 0f;
        level = _oldPainLevel;

        // TODO: Lerping
        if (level > 0f && _oldCritLevel <= 0f)
        {
            var pulseRate = 3f;
            var adjustedTime = time * pulseRate;
            float outerMaxLevel = 2.0f * distance;
            float outerMinLevel = 0.8f * distance;
            float innerMaxLevel = 0.6f * distance;
            float innerMinLevel = 0.2f * distance;

            var outerRadius = outerMaxLevel - level * (outerMaxLevel - outerMinLevel);
            var innerRadius = innerMaxLevel - level * (innerMaxLevel - innerMinLevel);

            var pulse = MathF.Max(0f, MathF.Sin(adjustedTime));

            _bruteShader.SetParameter("time", pulse);
            _bruteShader.SetParameter("color", new Vector3(1f, 0f, 0f));
            _bruteShader.SetParameter("darknessAlphaOuter", 0.8f);

            _bruteShader.SetParameter("outerCircleRadius", outerRadius);
            _bruteShader.SetParameter("outerCircleMaxRadius", outerRadius + 0.2f * distance);
            _bruteShader.SetParameter("innerCircleRadius", innerRadius);
            _bruteShader.SetParameter("innerCircleMaxRadius", innerRadius + 0.02f * distance);
            handle.UseShader(_bruteShader);
            handle.DrawRect(viewport, Color.White);
        }
        else
        {
            _oldPainLevel = PainLevel;
        }

        // _Duty: радиус обзора в мировых тайлах (учёт зума и RenderScale) для SoftCritical (5.5) и Critical (3.0)
        var inCritOrSoft = State is MobState.SoftCritical or MobState.Critical;
        var targetCritAlpha = inCritOrSoft ? 1f : 0f;
        var targetCritTiles = State switch
        {
            MobState.Critical => CriticalVisionRadiusTiles,
            MobState.SoftCritical => SoftCriticalVisionRadiusTiles,
            _ => InactiveVisionRadiusTiles,
        };

        if (!MathHelper.CloseTo(_critDarknessAlpha, targetCritAlpha, 0.001f))
            _critDarknessAlpha += GetDiff(targetCritAlpha - _critDarknessAlpha, lastFrameTime);
        else
            _critDarknessAlpha = targetCritAlpha;

        if (!MathHelper.CloseTo(_critVisionRadiusTiles, targetCritTiles, 0.01f))
            _critVisionRadiusTiles += GetDiff(targetCritTiles - _critVisionRadiusTiles, lastFrameTime);
        else
            _critVisionRadiusTiles = targetCritTiles;

        if (inCritOrSoft || _critDarknessAlpha > 0.001f)
        {
            var invMatrix = args.Viewport.GetWorldToLocalMatrix();
            var eyePos = eyeComp.Eye.Position.Position;
            var p0 = Vector2.Transform(eyePos, invMatrix);
            var p1 = Vector2.Transform(eyePos + Vector2.UnitX, invMatrix);
            var pixelsPerTile = MathF.Max(1f, (p1 - p0).Length());

            var outerRadius = _critVisionRadiusTiles * pixelsPerTile;
            var innerRadius = outerRadius * 0.45f;
            var oxyDarkness = _oldOxygenLevel > 0f ? MathF.Min(0.98f, 0.3f * MathF.Log(_oldOxygenLevel) + 1f) : 0f;
            var outerDarkness = MathF.Max(_critDarknessAlpha, oxyDarkness);

            _oxygenShader.SetParameter("time", 0.0f);
            _oxygenShader.SetParameter("color", new Vector3(0f, 0f, 0f));
            _oxygenShader.SetParameter("darknessAlphaOuter", outerDarkness);
            _oxygenShader.SetParameter("innerCircleRadius", innerRadius);
            _oxygenShader.SetParameter("innerCircleMaxRadius", innerRadius);
            _oxygenShader.SetParameter("outerCircleRadius", outerRadius);
            _oxygenShader.SetParameter("outerCircleMaxRadius", outerRadius);
            handle.UseShader(_oxygenShader);
            handle.DrawRect(viewport, Color.White);
        }
        else
        {
            level = _oldOxygenLevel;

            if (level > 0f)
            {
                float outerMaxLevel = 0.6f * distance;
                float outerMinLevel = 0.06f * distance;
                float innerMaxLevel = 0.02f * distance;
                float innerMinLevel = 0.02f * distance;

                var outerRadius = outerMaxLevel - level * (outerMaxLevel - outerMinLevel);
                var innerRadius = innerMaxLevel - level * (innerMaxLevel - innerMinLevel);
                var outerDarkness = MathF.Min(0.98f, 0.3f * MathF.Log(level) + 1f);

                _oxygenShader.SetParameter("time", 0.0f);
                _oxygenShader.SetParameter("color", new Vector3(0f, 0f, 0f));
                _oxygenShader.SetParameter("darknessAlphaOuter", outerDarkness);
                _oxygenShader.SetParameter("innerCircleRadius", innerRadius);
                _oxygenShader.SetParameter("innerCircleMaxRadius", innerRadius);
                _oxygenShader.SetParameter("outerCircleRadius", outerRadius);
                _oxygenShader.SetParameter("outerCircleMaxRadius", outerRadius + 0.2f * distance);
                handle.UseShader(_oxygenShader);
                handle.DrawRect(viewport, Color.White);
            }
        }

        // _Duty: белый оверлей _critShader отключён в SoftCritical/Critical/Dead, чтобы не засвечивать
        // тёмное чёрно-белое крит-зрение и не давать белую вспышку перед NaturalDeathOverlay;
        // сам расчёт DeadLevel выше сохранён для совместимости.

        handle.UseShader(null);
    }

    private float GetDiff(float value, float lastFrameTime)
    {
        var adjustment = value * 5f * lastFrameTime;

        if (value < 0f)
            adjustment = Math.Clamp(adjustment, value, -value);
        else
            adjustment = Math.Clamp(adjustment, -value, value);

        return adjustment;
    }
}
