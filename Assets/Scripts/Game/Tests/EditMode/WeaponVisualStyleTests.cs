using System.Collections.Generic;
using Game.Views;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary>
    /// 武器表现层里与资源无关的纯数据：槽位、拖尾、缩放。
    /// 模型/动作/时长由 WeaponVisualCatalog 资产负责（见 WeaponVisualCatalogTests），这里不测美术资源。
    /// </summary>
    public class WeaponVisualStyleTests
    {
        [Test]
        public void PlayerSlotsAreUniqueAndOutsideTheCapsule()
        {
            for (int count = 1; count <= 8; count++)
            {
                var seen = new HashSet<Vector3>();
                for (int index = 0; index < count; index++)
                {
                    Vector3 slot = WeaponVisualStyle.PlayerSlot(index, count);

                    Assert.IsTrue(seen.Add(slot), $"count={count} 时槽位 {index} 和另一个槽位重合");
                    Assert.IsTrue(Mathf.Abs(slot.x) > 0.5f || Mathf.Abs(slot.z) > 0.5f,
                        $"count={count} 时槽位 {index} 在胶囊身体里面");
                    Assert.GreaterOrEqual(slot.y, 0.9f);
                }
            }
        }

        [Test]
        public void WeaponsTrailBehindMovingPlayerAndClampDash()
        {
            Vector3 slot = WeaponVisualStyle.PlayerSlot(0, 1);

            // 静止时目标就是槽位本身
            Assert.AreEqual(slot, WeaponVisualStyle.FollowLocalTarget(slot, Vector2.zero, Quaternion.identity));

            // 向右移动时，本地目标点往左偏（玩家身后），偏移量 = 速度 × 拖尾时间
            Vector3 moving = WeaponVisualStyle.FollowLocalTarget(slot, new Vector2(5f, 0f), Quaternion.identity);
            Assert.AreEqual(slot.x - 5f * WeaponVisualStyle.FollowTrailTime, moving.x, 1e-4f);
            Assert.AreEqual(slot.y, moving.y, 1e-4f);
            Assert.AreEqual(slot.z, moving.z, 1e-4f);

            // 冲刺速度再高，拖尾也不能把武器甩出屏幕
            Vector3 dash = WeaponVisualStyle.FollowLocalTarget(slot, new Vector2(100f, 0f), Quaternion.identity);
            Assert.LessOrEqual((dash - slot).magnitude, WeaponVisualStyle.FollowMaxTrail + 1e-4f);
            Assert.Greater(WeaponVisualStyle.FollowSmoothTime, 0f, "跟随不能是瞬移，否则又变成硬焊在槽位上");
            Assert.Greater(WeaponVisualStyle.FollowTurnSmoothTime, 0f, "转向不能是瞬移，否则武器会跟着角色瞬转");
        }

        [Test]
        public void CompanionWeaponIsSmallerAndToTheSide()
        {
            Vector3 slot = WeaponVisualStyle.CompanionSlot;

            Assert.Greater(slot.x, 0.4f, "旅伴武器应当放在身体侧面，不然会被胶囊挡住");
            Assert.Less(WeaponVisualStyle.CompanionScale, WeaponVisualStyle.PlayerScale,
                "旅伴武器要比玩家武器小一档");
        }
    }
}
