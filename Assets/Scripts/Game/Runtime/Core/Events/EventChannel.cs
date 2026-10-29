using System;
using System.Collections.Generic;

namespace Game.Core.Events
{
    internal interface IEventChannel
    {
        bool HasPending { get; set; }
        void DispatchPending();
        void Clear();
    }

    /// <summary>
    /// 一种事件类型的订阅者与待分发的队列。
    /// 分发时对订阅者做一次快照拷贝，handler 内部退订不会改变本次分发的目标集合。
    /// </summary>
    internal sealed class EventChannel<T> : IEventChannel where T : struct
    {
        Action<T>[] _handlers = new Action<T>[4];
        Action<T>[] _dispatchBuffer = new Action<T>[4];
        int _count;
        readonly Queue<T> _pending = new Queue<T>(16);

        public bool HasPending { get; set; }
        public int HandlerCount => _count;

        public bool Subscribe(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            for (int i = 0; i < _count; i++)
            {
                if (_handlers[i] == handler) return false;
            }

            if (_count == _handlers.Length)
            {
                Array.Resize(ref _handlers, _handlers.Length * 2);
            }

            _handlers[_count++] = handler;
            return true;
        }

        public bool Unsubscribe(Action<T> handler)
        {
            for (int i = 0; i < _count; i++)
            {
                if (_handlers[i] != handler) continue;
                for (int j = i; j < _count - 1; j++)
                {
                    _handlers[j] = _handlers[j + 1];
                }

                _handlers[--_count] = null;
                return true;
            }

            return false;
        }

        public void Enqueue(in T evt)
        {
            _pending.Enqueue(evt);
        }

        public void DispatchPending()
        {
            while (_pending.Count > 0)
            {
                T evt = _pending.Dequeue();
                int n = _count;
                if (n == 0) continue;

                if (_dispatchBuffer.Length < n)
                {
                    Array.Resize(ref _dispatchBuffer, n);
                }

                Array.Copy(_handlers, _dispatchBuffer, n);
                for (int i = 0; i < n; i++)
                {
                    _dispatchBuffer[i]?.Invoke(evt);
                }
            }
        }

        public void Clear()
        {
            Array.Clear(_handlers, 0, _handlers.Length);
            _count = 0;
            _pending.Clear();
            HasPending = false;
        }
    }
}
