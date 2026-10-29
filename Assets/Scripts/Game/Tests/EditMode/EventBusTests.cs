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
            int total = 0;
            for (int i = 0; i < n; i++)
            {
                bus.Subscribe<Ping>(_ => total++);
            }

            bus.Publish(new Ping(0));
            bus.Flush();

            Assert.AreEqual(n, total);
            Assert.AreEqual(n, bus.SubscriptionCount);
        }
    }
}
