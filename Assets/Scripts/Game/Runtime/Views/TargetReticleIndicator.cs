using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 远程武器的目标锁定圈：画在当前锁定的怪物脚下，随着冷却进入拉弓窗口，
    /// 从目标外圈逐渐收拢到身体边缘；开火时消失，箭从弓上飞出去。
    /// 它不画在射手周围——远程的范围感应该由“我正在瞄谁”表达，而不是一圈覆盖范围。
    /// </summary>
    public sealed class TargetReticleIndicator : WeaponRangeIndicator
    {
        protected override int Segments => 48;
        protected override bool ClosedLoop => true;
        protected override float Width => 0.055f;
        protected override float AttackAlpha => 0.92f;

        /// <summary>intensity = 拉弓进度 0..1；0 是外圈，1 是贴住目标身体的内圈。</summary>
        protected override void Draw(float intensity)
        {
            if (Line == null || Model == null || !Model.HasTarget) return;

            Vector3 center = new Vector3(Model.TargetPosition.x, 0.060f, Model.TargetPosition.y);
            float targetRadius = Mathf.Max(0.25f, Model.TargetRadius);
            float outer = targetRadius + 0.85f;
            float inner = targetRadius + 0.08f;
            float radius = Mathf.Lerp(outer, inner, Mathf.Clamp01(intensity));

            Line.positionCount = Segments;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i / (float)Segments * Mathf.PI * 2f;
                Line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0f,
                    Mathf.Sin(angle) * radius));
            }

            // 收拢过程中保持清晰，不跟着 intensity 一起淡掉。
            ApplyLineColor(0.95f);
        }
    }
}
