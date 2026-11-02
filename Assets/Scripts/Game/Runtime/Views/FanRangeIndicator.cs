using UnityEngine;

namespace Game.Views
{
    /// <summary>近战常驻扇形：半径 = 当前有效射程，半角 = SpreadDeg。</summary>
    public sealed class FanRangeIndicator : WeaponRangeIndicator
    {
        protected override int Segments => 20;
        protected override float Width => 0.040f;

        public override void Refresh(bool attacking)
        {
            if (Line == null || Model == null || Owner == null) return;

            Vector3 origin = Origin();
            Vector2 aim = Aim();
            float range = Range();
            float half = HalfAngle() * Mathf.Deg2Rad;
            float aimAngle = Mathf.Atan2(aim.y, aim.x);

            Line.positionCount = Segments + 2;
            Line.SetPosition(0, origin);
            for (int i = 0; i <= Segments; i++)
            {
                float angle = Mathf.Lerp(-half, half, i / (float)Segments);
                float world = aimAngle + angle;
                Line.SetPosition(i + 1, origin + new Vector3(Mathf.Cos(world), 0f,
                    Mathf.Sin(world)) * range);
            }
            Line.SetPosition(Segments + 1, origin);

            ApplyLineColor(attacking);
        }
    }
}
