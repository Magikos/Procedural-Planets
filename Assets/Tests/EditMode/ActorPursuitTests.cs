using System;
using NUnit.Framework;
using UnityEngine;

namespace ProceduralPlanets.Tests
{
    public sealed class ActorPursuitTests
    {
        static ActorPursuit.Member Member(ulong id, float x, float z, float speed = 4) =>
            new(id, new Vector3(x, 0, z), speed, 1.4f, .55f);

        [Test]
        public void SlotsUseTravelTimeRemainStableAndReplaceMissingPursuer()
        {
            var plan = new ActorPursuit();
            var members = new[] { Member(5, 0, -2, 1), Member(20, -3, -3), Member(21, 3, -3) };
            plan.Update(1, Vector3.zero, Vector3.forward, Vector3.up, members);
            Assert.IsTrue(plan.TryGet(20, out int pursuer)); Assert.AreEqual(0, pursuer);
            plan.TryGet(5, out int first); plan.TryGet(21, out int second);
            Assert.AreNotEqual(first, second); Assert.AreNotEqual(0, first);
            plan.Update(1, Vector3.zero, Vector3.forward, Vector3.up,
                new[] { Member(21, 3, -1), Member(5, 0, -.2f), members[1] });
            plan.TryGet(5, out int stable); Assert.AreEqual(first, stable);
            plan.Update(1, Vector3.zero, Vector3.forward, Vector3.up, new[] { members[0], members[2] });
            Assert.IsFalse(plan.TryGet(20, out _));
            plan.TryGet(21, out int replacement); Assert.AreEqual(0, replacement);
            plan.Update(2, Vector3.zero, Vector3.forward, Vector3.up, new[] { members[0] });
            plan.TryGet(5, out int solo); Assert.AreEqual(0, solo);
            Assert.IsFalse(plan.TryGet(21, out _));
        }

        [Test]
        public void ApproachesLeadMovingTargetsAndUseTheSuppliedGroundPlane()
        {
            var plan = new ActorPursuit(); var member = Member(5, 0, -10);
            plan.Update(1, Vector3.zero, Vector3.forward * 6, Vector3.up, new[] { member });
            var left = plan.Goal(member, 1, Vector3.zero, Vector3.forward * 6, Vector3.up);
            var right = plan.Goal(member, 2, Vector3.zero, Vector3.forward * 6, Vector3.up);
            Assert.Less(left.x, 0); Assert.Greater(right.x, 0); Assert.Greater(left.z, 0);
            Assert.LessOrEqual(left.z, member.Speed * 2);
            Assert.AreEqual(Vector3.zero, plan.Goal(member, 0, Vector3.zero, Vector3.forward * 6, Vector3.up));
            plan.Clear();
            var sideways = new ActorPursuit.Member(5, new Vector3(0, -10, 0), 4, 1.4f, .55f);
            plan.Update(1, Vector3.zero, Vector3.up * 6, Vector3.right, new[] { sideways });
            var goal = plan.Goal(sideways, 1, Vector3.zero, Vector3.up * 6, Vector3.right);
            Assert.AreEqual(0, goal.x, .001f); Assert.Greater(goal.y, 0); Assert.AreNotEqual(0, goal.z);
        }

        [Test]
        public void AllyInStrikeLaneBlocksHuntButDoesNotDisableSelfDefense()
        {
            Assert.IsTrue(ActorPursuit.BlocksStrike(Vector3.zero, Vector3.forward * 2, Vector3.forward, Vector3.up, .6f));
            Assert.IsFalse(ActorPursuit.BlocksStrike(Vector3.zero, Vector3.forward * 2, Vector3.back, Vector3.up, .6f));
            Assert.IsFalse(ActorPursuit.BlocksStrike(Vector3.zero, Vector3.forward * 2, Vector3.forward + Vector3.right, Vector3.up, .6f));
            var c = new CreatureContext { Objective = CreatureObjective.Hunt, Senses = new CreatureSenses {
                Forward = Vector3.forward, Up = Vector3.up, AttackLaneBlocked = true } };
            Assert.IsFalse(CreatureHunt.CanStrike(c, Vector3.forward));
            c.Objective = CreatureObjective.Defend;
            Assert.IsTrue(CreatureHunt.CanStrike(c, Vector3.forward));
        }

        [Test]
        public void InvalidOrDuplicateMembersAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Member(0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Member(1, float.NaN, 0));
            var plan = new ActorPursuit(); var member = Member(1, 0, 0);
            Assert.Throws<ArgumentException>(() => plan.Update(1, Vector3.zero, Vector3.zero, Vector3.up, new[] { member, member }));
            Assert.Throws<ArgumentException>(() => plan.Update(1, Vector3.zero, Vector3.zero, Vector3.up, new ActorPursuit.Member[1]));
        }
    }
}
