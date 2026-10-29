using System;
using System.Collections.Generic;

namespace Game.Core.Events
{
    public sealed class EventBus : IEventBus
    {
        const int MaxFlushSteps = 10000;

        readonly Dictionary<Type, IEventChannel> _channels = new Dictionary<Type, IEventChannel>(32);
        readonly List<IEventChannel> _pending = new List<IEventChannel>(16);
        bool _isFlushing;

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
                _pending.Add(channel);
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
                // 处理过程中 handler 可能继续 Publish，Count 会被撑大，这里就是要跟着处理
                for (int i = 0; i < _pending.Count; i++)
                {
                    if (++guard > MaxFlushSteps)
                    {
                        throw new InvalidOperationException("事件分发超过上限，可能是事件互相触发形成了循环");
                    }

                    IEventChannel channel = _pending[i];
                    channel.HasPending = false;
                    channel.DispatchPending();
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
