using System;

namespace Game.Core.Events
{
    /// <summary>
    /// 事件总线。Publish 只入队，Flush 时统一分发。
    /// 同一 tick 内同类型事件保证 FIFO；跨类型的先后顺序不保证（按类型首次入队的顺序分发）。
    /// </summary>
    public interface IEventBus
    {
        void Subscribe<T>(Action<T> handler) where T : struct;
        void Unsubscribe<T>(Action<T> handler) where T : struct;
        void Publish<T>(in T evt) where T : struct;
        void Flush();
        void Clear();
        int SubscriptionCount { get; }
    }
}
