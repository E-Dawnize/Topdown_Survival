using Game.Core.Events;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// 玩家侧范围伤害的可见反馈：碰撞伤害、爆炸、链电、环绕刀、反伤在结算后各画一个颜色不同的环。
    ///
    /// 单独做成一个视图而不是塞进 EnemyViewPool：这些伤害的来源/位置都可能是玩家自己（碰撞、反伤、环绕刀），
    /// 与“敌人视图”没有关系；事件里只带“区域中心 + 半径 + 类型”，
    /// 表现层不需要认识 BuffSystem 或 RunSimulation。
    /// </summary>
    public sealed class CombatEffectView : MonoBehaviour
    {
        static readonly Color ContactColor = new Color(1f, 0.9f, 0.45f, 0.85f);
        static readonly Color ExplosionColor = new Color(1f, 0.55f, 0.22f, 0.9f);
        static readonly Color ChainColor = new Color(0.55f, 0.85f, 1f, 0.85f);
        static readonly Color OrbitColor = new Color(0.72f, 0.88f, 1f, 0.75f);
        static readonly Color ThornsColor = new Color(0.55f, 1f, 0.62f, 0.8f);
        static readonly Color CollectColor = new Color(0.45f, 0.92f, 1f, 0.9f);

        IEventBus _bus;
        bool _subscribed;

        public void Bind(IEventBus bus)
        {
            if (ReferenceEquals(_bus, bus)) return;

            Unsubscribe();
            _bus = bus;
            Subscribe();
        }

        void OnEnable()
        {
            Subscribe();
        }

        void OnDisable()
        {
            Unsubscribe();
        }

        void Subscribe()
        {
            if (_subscribed || _bus == null) return;

            _bus.Subscribe<CombatAreaEffect>(OnAreaEffect);
            _bus.Subscribe<ExperienceCollectPulse>(OnExperienceCollectPulse);
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed) return;

            _bus?.Unsubscribe<CombatAreaEffect>(OnAreaEffect);
            _bus?.Unsubscribe<ExperienceCollectPulse>(OnExperienceCollectPulse);
            _subscribed = false;
        }

        void OnAreaEffect(CombatAreaEffect e)
        {
            Color color;
            float startRadius;
            float duration;

            switch (e.Kind)
            {
                case CombatAreaEffectKind.Contact:
                    color = ContactColor;
                    startRadius = e.Radius * 0.45f;
                    duration = 0.22f;
                    break;
                case CombatAreaEffectKind.Chain:
                    color = ChainColor;
                    startRadius = 0.15f;
                    duration = 0.24f;
                    break;
                case CombatAreaEffectKind.Orbit:
                    color = OrbitColor;
                    startRadius = e.Radius * 0.72f;
                    duration = 0.30f;
                    break;
                case CombatAreaEffectKind.Thorns:
                    color = ThornsColor;
                    startRadius = e.Radius * 0.35f;
                    duration = 0.26f;
                    break;
                default:
                    color = ExplosionColor;
                    startRadius = 0.25f;
                    duration = 0.32f;
                    break;
            }

            CombatEffects.Ring(new Vector3(e.X, 0.05f, e.Z), startRadius, Mathf.Max(0.2f, e.Radius),
                color, duration);
        }

        /// <summary>全屏吸经验的扩散环。半径直接铺到屏幕外，给玩家“整屏都扫过一遍”的直觉。</summary>
        void OnExperienceCollectPulse(ExperienceCollectPulse e)
        {
            var origin = new Vector3(e.X, 0.06f, e.Z);
            CombatEffects.Ring(origin, 0.8f, 14f, CollectColor, 0.45f);
            CombatEffects.Ring(origin, 0.3f, 7f, new Color(0.85f, 0.98f, 1f, 0.8f), 0.28f);
        }
    }
}
