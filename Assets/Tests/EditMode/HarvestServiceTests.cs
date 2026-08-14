using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    // HarvestService is the one verb choke point (plans/003 §3b seam 1). The POC one-shots a node: persist +
    // remove-from-draw + grant yield + raise ScatterHarvestedEvent exactly once. Proving the seam wiring here
    // means multi-hit / axe-damage can later slot in behind TryHarvest without breaking the one-shot path.
    public sealed class HarvestServiceTests
    {
        int _harvestedEvents;
        ScatterHarvestedEvent _lastEvent;

        void OnHarvested(ScatterHarvestedEvent e) { _harvestedEvents++; _lastEvent = e; }

        [SetUp]
        public void Setup()
        {
            _harvestedEvents = 0;
            EventBus<ScatterHarvestedEvent>.ClearAll();
            EventBus<HarvestHitEvent>.ClearAll();
            EventBus<ScatterHarvestedEvent>.Listen(OnHarvested);
        }

        [TearDown]
        public void Teardown()
        {
            EventBus<ScatterHarvestedEvent>.ClearAll();
            EventBus<HarvestHitEvent>.ClearAll();
        }

        static HarvestService Make(HashSet<ulong> store, List<(int proto, ulong id)> removed,
            Dictionary<string, int> inv, ScatterInteraction interaction, string displayName = "Pine")
            => new HarvestService(
                id => store.Add(id),
                (p, id) => removed.Add((p, id)),
                (item, n) => { inv.TryGetValue(item, out int c); inv[item] = c + n; },
                _ => new ProtoHarvestInfo(interaction, displayName));

        [Test]
        public void OneShot_FellsPersistsRemovesGrantsRaisesOnce()
        {
            var store = new HashSet<ulong>();
            var removed = new List<(int, ulong)>();
            var inv = new Dictionary<string, int>();
            var svc = Make(store, removed, inv, ScatterInteraction.Chop);

            ulong id = 0xABCDEF12345UL;
            HarvestResult r = svc.TryHarvest(id, 4, ToolTier.BasicAxe, new Vector3(1, 2, 3));

            Assert.AreEqual(HarvestOutcome.Felled, r.Outcome);
            Assert.Greater(r.Yield.Count, 0);
            Assert.IsTrue(store.Contains(id), "harvested id persisted");
            CollectionAssert.Contains(removed, (4, id));
            Assert.Greater(inv[r.Yield.ItemId], 0, "inventory credited");
            Assert.AreEqual(1, _harvestedEvents, "ScatterHarvestedEvent raised exactly once");
            Assert.AreEqual(id, _lastEvent.Id);
        }

        [Test]
        public void NoneInteraction_NotHarvestable_NoSideEffects()
        {
            var store = new HashSet<ulong>();
            var removed = new List<(int, ulong)>();
            var inv = new Dictionary<string, int>();
            var svc = Make(store, removed, inv, ScatterInteraction.None);

            HarvestResult r = svc.TryHarvest(1UL, 0, ToolTier.BasicAxe, Vector3.zero);
            Assert.AreEqual(HarvestOutcome.NotHarvestable, r.Outcome);
            Assert.AreEqual(0, store.Count);
            Assert.AreEqual(0, removed.Count);
            Assert.AreEqual(0, _harvestedEvents);
        }

        [Test]
        public void AlreadyHarvested_NoRemoveNoGrantNoEvent()
        {
            var store = new HashSet<ulong> { 7UL }; // already harvested
            var removed = new List<(int, ulong)>();
            var inv = new Dictionary<string, int>();
            var svc = Make(store, removed, inv, ScatterInteraction.Chop);

            HarvestResult r = svc.TryHarvest(7UL, 4, ToolTier.BasicAxe, Vector3.zero);
            Assert.AreEqual(HarvestOutcome.AlreadyHarvested, r.Outcome);
            Assert.AreEqual(0, removed.Count, "no remove on already-harvested");
            Assert.AreEqual(0, inv.Count, "no grant on already-harvested");
            Assert.AreEqual(0, _harvestedEvents);
        }
    }
}
