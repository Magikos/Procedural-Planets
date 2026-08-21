using System;
using NUnit.Framework;

namespace ProceduralPlanets.Tests
{
    // EntityId names explicit objects - a dropped log, a placed workbench - in a space kept separate from
    // ScatterId. Every save record and every replicated object will depend on the allocation scheme, so the
    // parts worth pinning are the owner/counter split (two owners must never collide), the reserved zero, and
    // the reload path where the allocator has to clear ids already in the save.
    public sealed class EntityIdTests
    {
        [Test]
        public void OwnerAndCounter_SurviveThePacking()
        {
            var id = new EntityId(owner: 3, counter: 123456789UL);
            Assert.AreEqual(3, id.Owner);
            Assert.AreEqual(123456789UL, id.Counter);
            Assert.IsFalse(id.IsAuthoritative, "only the host tag is authoritative");

            var host = new EntityId(EntityId.HostOwner, 1);
            Assert.IsTrue(host.IsAuthoritative);

            // The counter must reach its full width, not silently lose the top bits to the owner tag.
            var max = new EntityId(ushort.MaxValue, EntityId.MaxCounter);
            Assert.AreEqual(ushort.MaxValue, max.Owner);
            Assert.AreEqual(EntityId.MaxCounter, max.Counter);
        }

        [Test]
        public void Zero_IsNone_AndCanNeverBeMinted()
        {
            Assert.IsTrue(EntityId.None.IsNone);
            Assert.AreEqual(0UL, EntityId.None.Value);
            Assert.Throws<ArgumentOutOfRangeException>(() => new EntityId(EntityId.HostOwner, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EntityId(0, EntityId.MaxCounter + 1));
            Assert.IsFalse(new EntityIdAllocator().Next().IsNone);
        }

        [Test]
        public void DifferentOwners_NeverCollide_EvenAtTheSameCounter()
        {
            var host = new EntityIdAllocator(EntityId.HostOwner);
            var client = new EntityIdAllocator(owner: 7);

            for (int i = 0; i < 100; i++)
            {
                EntityId h = host.Next();
                EntityId c = client.Next();
                Assert.AreEqual(h.Counter, c.Counter, "both allocate independently, so counters match");
                Assert.AreNotEqual(h.Value, c.Value, "the owner tag keeps the ids apart");
                Assert.IsTrue(h.IsAuthoritative);
                Assert.IsFalse(c.IsAuthoritative);
            }
        }

        [Test]
        public void Observe_ClearsIdsAlreadyInTheSave()
        {
            var loaded = new EntityIdAllocator();
            loaded.Observe(new EntityId(EntityId.HostOwner, 500));
            loaded.Observe(new EntityId(EntityId.HostOwner, 12));      // out of order: the max must win
            Assert.AreEqual(new EntityId(EntityId.HostOwner, 501), loaded.Next());
        }

        [Test]
        public void Observe_IgnoresOtherOwners_SoAClientCannotAdvanceTheHost()
        {
            var host = new EntityIdAllocator(EntityId.HostOwner);
            host.Observe(new EntityId(9, 1_000_000UL));
            Assert.AreEqual(new EntityId(EntityId.HostOwner, 1), host.Next());
        }

        [Test]
        public void Allocator_IsMonotonic_AndRefusesToWrap()
        {
            var a = new EntityIdAllocator();
            Assert.AreEqual(1UL, a.Next().Counter);
            Assert.AreEqual(2UL, a.Next().Counter);
            Assert.AreEqual(3UL, a.NextCounter);

            a.Observe(new EntityId(EntityId.HostOwner, EntityId.MaxCounter));
            Assert.Throws<InvalidOperationException>(() => a.Next(), "exhaustion is a bug, not a silent reuse");
        }

        [Test]
        public void EqualityIsByValue_SoIdsWorkAsDictionaryKeys()
        {
            var a = new EntityId(2, 42);
            var b = new EntityId(2, 42);
            var c = new EntityId(3, 42);

            Assert.IsTrue(a == b);
            Assert.IsTrue(a != c);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());

            var map = new System.Collections.Generic.Dictionary<EntityId, string> { [a] = "log" };
            Assert.AreEqual("log", map[b]);
            Assert.IsFalse(map.ContainsKey(c));
        }
    }
}
