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

        static ScatterPick Tree(ulong id, int proto, Vector3 pos) =>
            new ScatterPick(id, proto, pos, Quaternion.Euler(0f, 37f, 0f), 1.4f);

        static HarvestService Make(HashSet<ulong> store, List<(int proto, ulong id)> removed,
            Dictionary<string, int> inv, ScatterInteraction interaction, string displayName = "Pine",
            HashSet<ulong> dug = null, List<ScatterPick> persisted = null)
            => new HarvestService(
                pick => { persisted?.Add(pick); return store.Add(pick.Id); },
                (p, id) => removed.Add((p, id)),
                (item, n) => { inv.TryGetValue(item, out int c); inv[item] = c + n; },
                _ => new ProtoHarvestInfo(interaction, displayName),
                id => dug != null && dug.Add(id));

        [Test]
        public void OneShot_FellsPersistsRemovesGrantsRaisesOnce()
        {
            var store = new HashSet<ulong>();
            var removed = new List<(int, ulong)>();
            var inv = new Dictionary<string, int>();
            var persisted = new List<ScatterPick>();
            var svc = Make(store, removed, inv, ScatterInteraction.Chop, persisted: persisted);

            ulong id = 0xABCDEF12345UL;
            ScatterPick pick = Tree(id, 4, new Vector3(1, 2, 3));
            HarvestResult r = svc.TryHarvest(pick, ToolTier.BasicAxe);

            Assert.AreEqual(HarvestOutcome.Felled, r.Outcome);
            Assert.Greater(r.Yield.Count, 0);
            Assert.IsTrue(store.Contains(id), "harvested id persisted");
            CollectionAssert.Contains(removed, (4, id));
            Assert.Greater(inv[r.Yield.ItemId], 0, "inventory credited");
            Assert.AreEqual(1, _harvestedEvents, "ScatterHarvestedEvent raised exactly once");
            Assert.AreEqual(id, _lastEvent.Id);

            // The stump and the fall are placed from what was persisted, so the felled instance's yaw and
            // size have to reach the record rather than being dropped at the verb.
            Assert.AreEqual(1, persisted.Count);
            Assert.AreEqual(pick.Rotation, persisted[0].Rotation);
            Assert.AreEqual(pick.Scale, persisted[0].Scale);
        }

        [Test]
        public void NoneInteraction_NotHarvestable_NoSideEffects()
        {
            var store = new HashSet<ulong>();
            var removed = new List<(int, ulong)>();
            var inv = new Dictionary<string, int>();
            var svc = Make(store, removed, inv, ScatterInteraction.None);

            HarvestResult r = svc.TryHarvest(Tree(1UL, 0, Vector3.zero), ToolTier.BasicAxe);
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

            HarvestResult r = svc.TryHarvest(Tree(7UL, 4, Vector3.zero), ToolTier.BasicAxe);
            Assert.AreEqual(HarvestOutcome.AlreadyHarvested, r.Outcome);
            Assert.AreEqual(0, removed.Count, "no remove on already-harvested");
            Assert.AreEqual(0, inv.Count, "no grant on already-harvested");
            Assert.AreEqual(0, _harvestedEvents);
        }

        [Test]
        public void Dig_RemovesStump_GrantsWood_RaisesOnce_NoDoubleDig()
        {
            var store = new HashSet<ulong>();
            var removed = new List<(int, ulong)>();
            var inv = new Dictionary<string, int>();
            var dug = new HashSet<ulong>();
            var svc = Make(store, removed, inv, ScatterInteraction.Chop, dug: dug);

            ulong id = 42UL;
            HarvestResult r = svc.TryDig(id, new Vector3(1, 2, 3), ToolTier.Shovel);
            Assert.AreEqual(HarvestOutcome.Felled, r.Outcome);
            Assert.IsTrue(dug.Contains(id), "stump recorded dug");
            Assert.Greater(inv["Wood"], 0, "dig grants wood");
            Assert.AreEqual(1, _harvestedEvents);

            _harvestedEvents = 0;
            HarvestResult again = svc.TryDig(id, Vector3.zero, ToolTier.Shovel);
            Assert.AreEqual(HarvestOutcome.AlreadyHarvested, again.Outcome);
            Assert.AreEqual(0, _harvestedEvents, "no event on an already-dug stump");
        }
    }
}
