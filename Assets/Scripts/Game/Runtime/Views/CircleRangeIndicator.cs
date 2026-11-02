using UnityEngine;

namespace Game.Views
{
    /// <summary>远程常驻圆形：半径 = 当前有效射程。自动索敌没有固定朝向，所以用整圈表示覆盖范围。</summary>
    public sealed class CircleRangeIndicator : WeaponRangeIndicator
    {
        protected override int Segments => 64;
        protected override bool ClosedLoop => true;
        protected override float Width => 0.055f;
        protected override float AttackAlpha => 0.65f;

        protected override void Draw(float intensity)
        {
            if (Line == null || Model == null || Owner == null) return;

            Vector3 origin = Origin(0.040f);
            float range = Range();

            Line.positionCount = Segments;
            for (int i = 0; i < Segments; i++)
            {
                float angle = i / (float)Segments * Mathf.PI * 2f;
                Line.SetPosition(i, origin + new Vector3(Mathf.Cos(angle) * range, 0f,
                    Mathf.Sin(angle) * range));
            }

            ApplyLineColor(intensity);
        }
    }
}
