using System;
using System.Collections.Generic;
using Game.Core.Events;
using UnityEngine;

namespace Game.Views
{
    /// <summary>
    /// UI 基类。只做一件事：把订阅登记下来，隐藏时统一取消。
    ///
    /// 约定：预制体根节点保持 inactive，装配根实例化后先 Initialize 再激活。
    /// 否则首次 OnEnable 会早于注入，订阅静默失败——所以这里宁可报错也不放过。
    /// </summary>
    public abstract class UiView : MonoBehaviour
    {
        readonly List<Action> _unsubscribers = new List<Action>(4);

        protected IEventBus Bus { get; private set; }
        public bool IsInitialized => Bus != null;

        public void Initialize(IEventBus bus)
        {
            Bus = bus;
        }

        /// <summary>子类在这里登记订阅，用 <see cref="On{T}"/> 而不是直接调 Bus。</summary>
        protected abstract void SubscribeAll();

        protected void On<T>(Action<T> handler) where T : struct
        {
            if (Bus == null)
            {
                Debug.LogError($"[{GetType().Name}] Bus 未注入，订阅 {typeof(T).Name} 被忽略");
                return;
            }

            Bus.Subscribe(handler);
            _unsubscribers.Add(() => Bus.Unsubscribe(handler));
        }

        protected virtual void OnEnable()
        {
            if (Bus == null)
            {
                Debug.LogError($"[{GetType().Name}] 在 Initialize 之前被激活，事件不会订阅。" +
                               "检查预制体根节点是否 inactive、装配根是否先 Initialize 再激活");
                return;
            }

            SubscribeAll();
        }

        protected virtual void OnDisable()
        {
            for (int i = 0; i < _unsubscribers.Count; i++)
            {
                _unsubscribers[i]();
            }

            _unsubscribers.Clear();
        }
    }
}
