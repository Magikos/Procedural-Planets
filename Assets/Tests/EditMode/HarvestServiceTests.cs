using System;
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
            EventBus<CreatureStruckEvent>.ClearAll();
            _struckEvents = 0;
            EventBus<ScatterHarvestedEvent>.Listen(OnHarvested);
        }

        [TearDown]
        public void Teardown()
        {
            EventBus<ScatterHarvestedEvent>.ClearAll();
            EventBus<HarvestHitEvent>.ClearAll();
            EventBus<CreatureStruckEvent>.ClearAll();
            _struckEvents = 0;
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

        // --- creature strikes and carcass loot -------------------------------

        int _struckEvents;
        CreatureStruckEvent _lastStruck;

        void OnStruck(CreatureStruckEvent e) { _struckEvents++; _lastStruck = e; }

        // An animal with health, standing in for the residency service. It owns its own health exactly as the
        // real one does, so what is under test is the choke point's credit-and-announce, not the arithmetic.
        // A killing blow yields NOTHING: the hide stays on the body.
        static Func<EntityId, int, Vector3, CreatureStrike> Animal(int health, string name)
        {
            int remaining = health;
            return (_, damage, _) =>
            {
                if (remaining <= 0) return default;
                remaining -= Mathf.Max(1, damage);
                return remaining > 0
                    ? CreatureStrike.Wounded(name, Vector3.one, remaining)
                    : CreatureStrike.Fatal(name, Vector3.one);
            };
        }

        // A body that gives its yield up once and then has nothing left.
        static Func<EntityId, int, Vector3, CreatureStrike> Carcass(string name, HarvestYield yield)
        {
            bool taken = false;
            return (_, _, _) =>
            {
                if (taken) return CreatureStrike.NothingLeft(name, Vector3.one);
                taken = true;
                return CreatureStrike.Looted(name, Vector3.one, yield);
            };
        }

        static HarvestService MakeHunter(Dictionary<string, int> inv,
            Func<EntityId, int, Vector3, CreatureStrike> target = null)
            => new HarvestService(
                _ => true,
                (_, _) => { },
                (item, n) => { inv.TryGetValue(item, out int c); inv[item] = c + n; },
                _ => default,
                _ => false,
                target);

        static EntityId Anything => new(EntityId.DerivedOwner, 7);

        [Test]
        public void Strike_WoundsAndKillsWithoutGrantingAnything()
        {
            var inv = new Dictionary<string, int>();
            HarvestService svc = MakeHunter(inv, Animal(3, "Placeholder Deer"));
            EventBus<CreatureStruckEvent>.Listen(OnStruck);

            Assert.AreEqual(HarvestOutcome.Hit, svc.TryStrike(Anything, Vector3.zero, ToolTier.Club).Outcome);
            Assert.AreEqual(HarvestOutcome.Hit, svc.TryStrike(Anything, Vector3.zero, ToolTier.Club).Outcome);

            HarvestResult fatal = svc.TryStrike(Anything, Vector3.zero, ToolTier.Club);
            Assert.AreEqual(HarvestOutcome.Felled, fatal.Outcome);
            Assert.AreEqual(0, fatal.Yield.Count, "a killing blow leaves the hide on the body");
            Assert.AreEqual(0, inv.Count, "nothing is credited until the carcass is looted");

            Assert.AreEqual(3, _struckEvents, "every blow is announced, wound or kill");
            Assert.IsTrue(_lastStruck.Killed);
        }

        [Test]
        public void Loot_GrantsExactlyOnce_ThenTheCarcassHasNothingLeft()
        {
            var inv = new Dictionary<string, int>();
            HarvestService svc = MakeHunter(inv, Carcass("Placeholder Deer", new HarvestYield("Hide", 2)));
            EventBus<CreatureStruckEvent>.Listen(OnStruck);

            HarvestResult took = svc.TryStrike(Anything, Vector3.zero, ToolTier.Club);
            Assert.AreEqual(HarvestOutcome.Felled, took.Outcome);
            Assert.AreEqual(2, inv["Hide"]);
            Assert.AreEqual(1, _struckEvents);

            // Without this a held interact key farms one carcass forever.
            Assert.AreEqual(HarvestOutcome.AlreadyHarvested,
                svc.TryStrike(Anything, Vector3.zero, ToolTier.Club).Outcome);
            Assert.AreEqual(2, inv["Hide"], "a looted carcass does not yield twice");
            Assert.AreEqual(1, _struckEvents, "and it is not announced twice either");
        }

        [Test]
        public void Strike_WithNoCreatureSystemWired_IsNotHarvestable()
        {
            var inv = new Dictionary<string, int>();
            HarvestService svc = MakeHunter(inv);
            Assert.AreEqual(HarvestOutcome.NotHarvestable,
                svc.TryStrike(Anything, Vector3.zero, ToolTier.Club).Outcome);
        }
    }
}
