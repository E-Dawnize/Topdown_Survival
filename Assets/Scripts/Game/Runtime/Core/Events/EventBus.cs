using System;
using System.Collections.Generic;

namespace Game.Core.Events
{
    public sealed class EventBus : IEventBus
    {
        readonly int _maxFlushSteps;

        readonly Dictionary<Type, IEventChannel> _channels = new Dictionary<Type, IEventChannel>(32);
        readonly List<IEventChannel> _pending = new List<IEventChannel>(16);
        bool _isFlushing;

        public EventBus(int maxFlushSteps = 10000)
        {
            if (maxFlushSteps < 1) throw new ArgumentOutOfRangeException(nameof(maxFlushSteps));
            _maxFlushSteps = maxFlushSteps;
        }

        public int SubscriptionCount { get; private set; }

        EventChannel<T> Channel<T>() where T : struct
        {
            if (_channels.TryGetValue(typeof(T), out IEventChannel existing))
            {
                return (EventChannel<T>)existing;
            }

            var created = new EventChannel<T>();
            _channels.Add(typeof(T), created);
            return created;
        }

        public void Subscribe<T>(Action<T> handler) where T : struct
        {
            if (Channel<T>().Subscribe(handler))
            {
                SubscriptionCount++;
            }
        }

        public void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (Channel<T>().Unsubscribe(handler))
            {
                SubscriptionCount--;
            }
        }

        public void Publish<T>(in T evt) where T : struct
        {
            EventChannel<T> channel = Channel<T>();
            channel.Enqueue(evt);
            if (!channel.HasPending)
            {
                channel.HasPending = true;

                // 频道可能已经在 _pending 里但本轮已经分发完（HasPending=false）。
                // 这时只把它重新标脏，不能再加一条，否则同一频道会被重复调度。
                if (!_pending.Contains(channel)) _pending.Add(channel);
            }
        }

        public void Flush()
        {
            if (_isFlushing)
            {
                throw new InvalidOperationException("Flush 不可重入：不要在事件处理里再调用 Flush");
            }

            _isFlushing = true;
            try
            {
                int guard = 0;

                // 每一轮只处理各频道“本轮开始时”的事件。handler 新发布的事件会留在队列里，
                // 由下一轮处理；guard 因此能看见同类型自触发，并在预算耗尽时中止，而不是卡死。
                while (true)
                {
                    bool anyPending = false;

                    for (int i = 0; i < _pending.Count; i++)
                    {
                        IEventChannel channel = _pending[i];
                        if (!channel.HasPending) continue;

                        anyPending = true;
                        if (++guard > _maxFlushSteps)
                        {
                            throw new InvalidOperationException(
                                "事件分发超过上限，可能是事件互相触发形成了循环");
                        }

                        // 分发期间保持 HasPending=true：handler 里 Publish 同一个频道时，
                        // Publish 不会把这个频道重复加进 _pending。等分发完再按队列余量决定。
                        try
                        {
                            channel.HasPending = channel.DispatchPending();
                        }
                        catch
                        {
                            // 非 handler 异常（例如扩容失败）时保留队列，下一次 Flush 还能重试。
                            channel.HasPending = channel.HasQueued;
                            throw;
                        }
                    }

                    if (!anyPending) break;
                }

                _pending.Clear();
            }
            finally
            {
                _isFlushing = false;
            }
        }

        public void Clear()
        {
            foreach (IEventChannel channel in _channels.Values)
            {
                channel.Clear();
            }

            _pending.Clear();
            SubscriptionCount = 0;
        }
    }
}
