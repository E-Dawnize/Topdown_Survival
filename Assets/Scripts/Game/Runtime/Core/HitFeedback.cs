namespace Game.Core
{
    /// <summary>
    /// 命中反馈的门槛：定格与震屏只在"值得强调"的命中上触发。
    /// 每一次普通命中都卡一下的话，连射时画面会一直抽，反馈反而读不出来。
    /// </summary>
    public static class HitFeedback
    {
        /// <summary>命中定格时长。短到只够"卡一下"，再长会被当成掉帧。</summary>
        public const float HitStopSeconds = 0.05f;

        /// <summary>
        /// 算"大伤害"的血量阈值。伤害跳字（WeaponDamage 的 ShowNumber 位）也用这一条线：
        /// 两处各写一个 20 的话，调完一处另一处就会说出另一种口径。
        /// </summary>
        public const float BigHitThreshold = 20f;

        /// <summary>震屏力度，Cinemachine 约定 1 是常规强度。小幅度就够读出来，大了会糊。</summary>
        public const float ShakeForce = 0.2f;

        public static bool IsEmphasized(float amount, bool isCrit) => isCrit || amount >= BigHitThreshold;
    }
}
