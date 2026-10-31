using System;
using System.Collections.Generic;
using Game.Core.Events;
using NUnit.Framework;

namespace Game.Tests
{
    public class EventBusTests
    {
        readonly struct Ping
        {
            public readonly int Value;
            public Ping(int value) { Value = value; }
        }

        readonly struct Pong
        {
            public readonly int Value;
            public Pong(int value) { Value = value; }
        }

        /// <summary>
        /// 每个订阅者一个实例。直接写 `_ => total++` 是错的：同一个作用域里的 lamba 捕获同一个闭包，
        /// 循环 50 次得到的是同一个委托实例，会被去重成一个订阅者。
        /// </summary>
        sealed class Counter
        {
            public int Total;
            public void Handle(Ping _) => Total++;
        }

        [Test]
        public void PublishIsDeferredUntilFlush()
        {
            var bus = new EventBus();
            int received = 0;
            bus.Subscribe<Ping>(_ => received++);

            bus.Publish(new Ping(1));
            Assert.AreEqual(0, received, "Publish 之后不应该立刻分发");

            bus.Flush();
            Assert.AreEqual(1, received);
        }

        [Test]
        public void SameTypeKeepsFifoOrder()
        {
            var bus = new EventBus();
            var seen = new List<int>();
            bus.Subscribe<Ping>(p => seen.Add(p.Value));

            bus.Publish(new Ping(1));
            bus.Publish(new Ping(2));
            bus.Publish(new Ping(3));
            bus.Flush();

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, seen);
        }

        [Test]
        public void DuplicateSubscribeOnlyFiresOnce()
        {
            var bus = new EventBus();
            int count = 0;
            Action<Ping> handler = _ => count++;

            bus.Subscribe(handler);
            bus.Subscribe(handler);
            Assert.AreEqual(1, bus.SubscriptionCount);

            bus.Publish(new Ping(0));
            bus.Flush();
            Assert.AreEqual(1, count);
        }

        [Test]
        public void UnsubscribeStopsDelivery()
        {
            var bus = new EventBus();
            int count = 0;
            Action<Ping> handler = _ => count++;

            bus.Subscribe(handler);
            bus.Publish(new Ping(0));
            bus.Unsubscribe(handler);
            bus.Flush();

            Assert.AreEqual(0, count, "退订发生在 Flush 之前，这次不该收到");
            Assert.AreEqual(0, bus.SubscriptionCount);
        }

        [Test]
        public void HandlerCanUnsubscribeDuringDispatchSafely()
        {
            var bus = new EventBus();
            int a = 0, b = 0;
            Action<Ping> ha = _ => { a++; bus.Unsubscribe<Ping>(_ => { }); };
            Action<Ping> hb = _ => b++;

            bus.Subscribe(ha);
            bus.Subscribe(hb);
            bus.Publish(new Ping(0));
            bus.Flush();

            Assert.AreEqual(1, a);
            Assert.AreEqual(1, b, "另一个 handler 仍应收到本次事件");
        }

        [Test]
        public void HandlerCanPublishDuringFlush()
        {
            var bus = new EventBus();
            var order = new List<string>();

            bus.Subscribe<Ping>(p =>
            {
                order.Add("ping" + p.Value);
                if (p.Value == 1) bus.Publish(new Pong(1));
            });
            bus.Subscribe<Pong>(_ => order.Add("pong"));

            bus.Publish(new Ping(1));
            bus.Flush();

            CollectionAssert.AreEqual(new[] { "ping1", "pong" }, order);
        }

        [Test]
        public void ClearDropsHandlersAndPending()
        {
            var bus = new EventBus();
            int count = 0;
            bus.Subscribe<Ping>(_ => count++);
            bus.Publish(new Ping(0));

            bus.Clear();
            bus.Flush();

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, bus.SubscriptionCount);
        }

        [Test]
        public void ClearLeavesZeroSubscriptionsAndLateUnsubscribeIsANoOp()
        {
            // 场景重载时的真实顺序：AppRoot.OnDestroy 先把总线 Clear，HUD / 商店 / 结算面板的
            // OnDisable 之后才逐个退订。退订落在 Clear 之后必须是空操作，
            // 否则订阅计数会被减成负数，下一个场景里一开就“看着还有订阅”。
            var bus = new EventBus();
            Action<Ping> handler = _ => { };

            bus.Subscribe(handler);
            bus.Subscribe<Pong>(_ => { });
            Assert.AreEqual(2, bus.SubscriptionCount);

            bus.Clear();
            Assert.AreEqual(0, bus.SubscriptionCount, "清理后订阅数归零");

            bus.Unsubscribe(handler);
            Assert.AreEqual(0, bus.SubscriptionCount, "清理后再退订不能把计数减成负数");

            // 清空后重新订阅要能正常工作：新场景的订阅不被旧状态挡住
            int pings = 0;
            bus.Subscribe<Ping>(_ => pings++);
            bus.Publish(new Ping(1));
            bus.Flush();

            Assert.AreEqual(1, pings);
            Assert.AreEqual(1, bus.SubscriptionCount);
        }

        [Test]
        public void TypesAreIndependent()
        {
            var bus = new EventBus();
            int pings = 0, pongs = 0;
            bus.Subscribe<Ping>(_ => pings++);
            bus.Subscribe<Pong>(_ => pongs++);

            bus.Publish(new Ping(0));
            bus.Flush();

            Assert.AreEqual(1, pings);
            Assert.AreEqual(0, pongs);
        }

        [Test]
        public void ManySubscribersAllReceive()
        {
            var bus = new EventBus();
            const int n = 50;
            var counters = new Counter[n];
            for (int i = 0; i < n; i++)
            {
                counters[i] = new Counter();
                bus.Subscribe<Ping>(counters[i].Handle);
            }

            bus.Publish(new Ping(0));
            bus.Flush();

            for (int i = 0; i < n; i++)
            {
                Assert.AreEqual(1, counters[i].Total, $"订阅者 {i} 的收到次数不对");
            }
            Assert.AreEqual(n, bus.SubscriptionCount);
        }
    }
}
