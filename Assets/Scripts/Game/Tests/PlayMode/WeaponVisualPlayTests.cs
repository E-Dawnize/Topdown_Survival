using System.Collections;
using System.Reflection;
using Game.Config;
using Game.Core;
using Game.Model;
using Game.Systems.Weapons;
using Game.Views;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests
{
    /// <summary>
    /// 武器表现层在真实 MonoBehaviour 生命周期下的最小验证：catalog prefab 能实例化，
    /// ShotsFired 变化驱动一次挥砍，姿态真的转起来，时长走完后回到待机。
    /// 模型长什么样只能在场景里看，这里只管"prefab 装上了、动画会播"。
    /// </summary>
    public class WeaponVisualPlayTests
    {
        sealed class NoopBehaviour : IWeaponBehaviour
        {
            public void Tick(WeaponModel weapon, Vector2 shooterPosition, Vector2 facing,
                in ShooterStats stats, float dt)
            {
            }
        }


        [Test]
        public void NockedArrowIsHandedToTheProjectilePool()
        {
            ProjectileNockedHandoff.Clear();
            ProjectileMuzzleRegistry.Clear();

            var parent = new GameObject("ProjectilePoolTest");
            var dummyPrefab = new GameObject("UnusedArrowPrefab");
            var nocked = new GameObject("NockedArrow");
            nocked.SetActive(false);
            nocked.AddComponent<ProjectileView>();

            var projectiles = new ProjectileSystem(new DeterministicRandom(1UL), 4);
            var pool = new ProjectileViewPool(projectiles, null, parent.transform, dummyPrefab);

            ProjectileNockedHandoff.Set(0, nocked);
            Assert.IsTrue(projectiles.Spawn(Vector2.zero, Vector2.up, 18f, 8f, default, 1));

            pool.Sync(0f);

            Assert.IsTrue(nocked.activeSelf, "交接后的搭箭应被投射物池激活");
            Assert.AreSame(parent.transform, nocked.transform.parent,
                "搭箭应移动进投射物视图池，而不是继续挂在弓上");
            Assert.IsNull(ProjectileNockedHandoff.Take(0, 1f),
                "交接只能被消费一次");

            Object.Destroy(parent);
            Object.Destroy(dummyPrefab);
            ProjectileNockedHandoff.Clear();
            ProjectileMuzzleRegistry.Clear();
        }


        [Test]
        public void ArrowVisualGoesStraightFromMuzzleToAimPoint()
        {
            var go = new GameObject("ArrowPathTest");
            var view = go.AddComponent<ProjectileView>();
            view.Initialize();

            Vector2 origin = Vector2.zero;
            Vector2 aimPoint = new Vector2(10f, 0f);
            Vector3 muzzle = new Vector3(1f, 1f, 0f);

            view.Sync(origin, origin, 1f, Vector2.up, origin, aimPoint, true, muzzle);
            Vector3 start = go.transform.position;

            view.Sync(origin, new Vector2(5f, 0f), 1f, Vector2.up, origin, aimPoint, true, muzzle);
            Vector3 middle = go.transform.position;

            view.Sync(origin, aimPoint, 1f, Vector2.up, origin, aimPoint, true, muzzle);
            Vector3 end = go.transform.position;

            Vector2 line = aimPoint - new Vector2(muzzle.x, muzzle.z);
            foreach (Vector3 p in new[] { start, middle, end })
            {
                Vector2 offset = new Vector2(p.x, p.z) - new Vector2(muzzle.x, muzzle.z);
                Assert.AreEqual(0f, line.x * offset.y - line.y * offset.x, 1e-4f,
                    "箭的视觉位置必须始终在枪口 → 瞄准点的直线上，不能横向平移");
                Assert.AreEqual(muzzle.y, p.y, 1e-4f, "箭保持枪口高度");
            }

            Assert.Less(Vector3.Distance(start, middle), Vector3.Distance(start, end),
                "箭必须沿着目标方向前进，不能原地横移");

            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator ShotsFiredStartsASwingAndSettlesBackToIdle()
        {
            WeaponVisualCatalog catalog = WeaponVisualCatalog.LoadDefault();
            Assert.IsNotNull(catalog, "缺少 WeaponVisualCatalog.asset");
            WeaponVisualCatalog.Entry entry = catalog.Find("Sword");
            Assert.IsNotNull(entry, "catalog 里没有 Sword");
            Assert.IsNotNull(entry.Prefab, "Sword 条目没有 prefab");

            var def = new WeaponDef
            {
                Id = "Sword", Name = "Sword", Kind = WeaponKind.Melee,
                Params = new[] { 0.08f, 0.22f }
            };
            var model = new WeaponModel(def, new NoopBehaviour());

            var root = new GameObject("WeaponVisualPlayTest");
            WeaponVisual visual = WeaponVisual.Attach(root.transform, model, entry, Vector3.zero, 1f, 0);
            Assert.IsNotNull(visual);
            Assert.Greater(visual.transform.childCount, 0, "prefab 里应当有 Model/Flash 子物体");
            Assert.AreEqual(model.ShotsFired, 0);

            // 先让一帧待机姿态落下来，再记基准
            yield return null;
            Quaternion idle = visual.transform.localRotation;

            // 表现层要跟着真实瞄准方向转，而不是只跟父节点（玩家移动方向）转
            model.AimDirection = new Vector2(1f, 0f);
            yield return null;
            Assert.AreEqual(90f, Mathf.DeltaAngle(0f, visual.transform.localEulerAngles.y), 1f,
                "AimDirection 指向 +X 时，武器应绕 Y 转到 90°");
            model.AimDirection = Vector2.zero;
            yield return null;

            model.Fire(default);
            // 动作从 0° 起手（避免和待机衔接时跳变），需要等一小段真实时间才到明显角度；
            // 测试 runner 的前几帧 dt 可能很小，用固定等待比数帧可靠。
            yield return new WaitForSeconds(0.08f);

            float swingAngle = Quaternion.Angle(idle, visual.transform.localRotation);
            Assert.Greater(swingAngle, 20f, "ShotsFired +1 之后刀身应当已经离开待机角度");

            yield return new WaitForSeconds(entry.AttackDuration + 0.12f);

            var attacking = typeof(WeaponVisual).GetField("_attacking",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(attacking);
            Assert.IsFalse((bool)attacking.GetValue(visual), "动作时长走完后必须退出攻击状态");

            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator BowShowsNockedArrowDuringDrawWindow()
        {
            WeaponVisualCatalog catalog = WeaponVisualCatalog.LoadDefault();
            Assert.IsNotNull(catalog, "缺少 WeaponVisualCatalog.asset");
            WeaponVisualCatalog.Entry entry = catalog.Find("Bow");
            Assert.IsNotNull(entry, "catalog 里没有 Bow");

            var def = new WeaponDef
            {
                Id = "Bow", Name = "Bow", Kind = WeaponKind.Ranged,
                BaseDamage = 8f, Cooldown = 0.85f, Range = 8f,
                Projectiles = 1, ProjectileSpeed = 18f, Pierce = 1,
                Params = new[] { 0f }
            };
            var model = new WeaponModel(def, new NoopBehaviour());

            var root = new GameObject("BowVisualPlayTest");
            WeaponVisual visual = WeaponVisual.Attach(root.transform, model, entry, Vector3.zero, 1f, 0, 99);
            Assert.IsNotNull(visual);
            Assert.IsTrue(visual.HasNockedArrow, "弓箭 prefab 上必须挂 NockedArrow");

            Transform nocked = visual.transform.Find("NockedArrow");
            Assert.IsNotNull(nocked);
            Assert.IsFalse(nocked.gameObject.activeSelf, "待机时箭不应一直搭在弓上");

            model.AimDirection = Vector2.up;
            model.Fire(default);
            yield return null;

            Assert.IsTrue(ProjectileMuzzleRegistry.TryGet(99, 0.5f, out Vector3 muzzle),
                "开火后应把弓的枪口位置写给投射物表现层");
            Assert.Less(Vector3.Distance(muzzle, visual.MuzzleWorld), 0.02f,
                "注册的枪口应当是这把弓的 MuzzleWorld");

            yield return new WaitForSeconds(entry.AttackDuration + 0.08f);

            Assert.IsFalse(nocked.gameObject.activeSelf, "刚射完不应搭箭");
            Assert.IsNull(nocked.transform.parent, "开火后这支搭箭应脱离弓，交给投射物池");

            Transform nockedAfterShot = visual.transform.Find("NockedArrow");
            Assert.IsNotNull(nockedAfterShot, "开火后应在弓上复制一支新的搭箭");
            Assert.IsFalse(nockedAfterShot.gameObject.activeSelf, "新搭箭在冷却期间不应立即可见");

            // 手动把冷却推到预拉窗口内；视觉层只读 CooldownRemaining。
            model.Tick(0.65f);
            yield return null;

            Assert.IsTrue(nockedAfterShot.gameObject.activeSelf, "冷却最后 0.28 秒应把新搭箭拉上弓");

            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator MeleeWeaponShowsRangeFanBeforeFirstSwing()
        {
            WeaponVisualCatalog catalog = WeaponVisualCatalog.LoadDefault();
            Assert.IsNotNull(catalog, "缺少 WeaponVisualCatalog.asset");
            WeaponVisualCatalog.Entry entry = catalog.Find("Sword");
            Assert.IsNotNull(entry, "catalog 里没有 Sword");

            var def = new WeaponDef
            {
                Id = "Sword", Name = "Sword", Kind = WeaponKind.Melee,
                BaseDamage = 15f, Cooldown = 0.6f, Range = 2.2f,
                SpreadDeg = 45f, Params = new[] { 0.08f, 0.22f }
            };
            var model = new WeaponModel(def, new NoopBehaviour());

            var root = new GameObject("MeleeRangePlayTest");
            WeaponVisual visual = WeaponVisual.Attach(root.transform, model, entry, Vector3.zero, 1f, 0);
            Assert.IsNotNull(visual);
            Assert.IsTrue(visual.HasMeleeRange, "近战武器装上的第一帧就应该有常驻范围扇形");
            Assert.AreEqual(0, model.ShotsFired, "测试前提：还没有出过手");

            yield return null;

            Transform range = root.transform.Find("MeleeRange_Sword");
            Assert.IsNotNull(range, "范围扇形应作为武器架的兄弟节点存在");
            LineRenderer line = range.GetComponent<LineRenderer>();
            Assert.IsNotNull(line);
            Assert.IsTrue(line.enabled, "还没攻击时范围扇形也要可见");
            Assert.Greater(line.positionCount, 2, "范围扇形至少要有一条弧线，不能只有中心点");

            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator EnemyHealthBarAppearsAfterDamage()
        {
            var def = new EnemyDef
            {
                Id = "bar_test", Name = "血条靶", Behaviour = EnemyBehaviourType.Chase, Tier = 1,
                HP = 10f, Damage = 0f, Speed = 0f, AttackRange = 1f, AttackInterval = 1f,
                XpValue = 1, BodyRadius = 0.3f, Scale = 1f
            };
            var enemy = new EnemyModel(def, Vector2.zero, new UnitId(9001), EnemySpawnOptions.Default);

            var go = new GameObject("HealthBarPlayTest");
            UnitView view = go.AddComponent<UnitView>();
            view.Bind(enemy);

            enemy.HP = 5f;
            view.Sync(1f);
            yield return null;

            LineRenderer[] lines = go.GetComponentsInChildren<LineRenderer>(true);
            Assert.AreEqual(2, lines.Length, "血条由底条 + 填充条两条线组成");
            int enabled = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].enabled) enabled++;
            }
            Assert.AreEqual(2, enabled, "掉血后血条应当可见");

            enemy.HP = enemy.MaxHP;
            view.Sync(1f);
            yield return null;
            enabled = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].enabled) enabled++;
            }
            Assert.AreEqual(0, enabled, "满血普通怪不显示头顶血条");

            Object.Destroy(go);
        }

        [Test]
        public void DamageNumbersCreateWorldTextAndCritSuffix()
        {
            DamageNumbers.Clear();
            DamageNumbers.Show(new Vector3(1f, 1f, 2f), 123f, false);
            DamageNumbers.Show(new Vector3(2f, 1f, 2f), 456f, true);

            TextMesh[] texts = Object.FindObjectsOfType<TextMesh>();
            bool normal = false;
            bool crit = false;
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].text == "123") normal = true;
                if (texts[i].text == "暴击 456") crit = true;
            }

            Assert.IsTrue(normal, "普通伤害应生成数字飘字");
            Assert.IsTrue(crit, "暴击飘字应带独立前缀，和普通伤害区分");

            DamageNumbers.Clear();
        }
    }
}
