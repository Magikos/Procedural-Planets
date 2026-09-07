using System;
using NUnit.Framework;

namespace ProceduralPlanets.Tests
{
    // The state machine itself, independent of creatures. The parts worth pinning are the ones that were
    // wrong in the machine this was harvested from, plus the id contract the persistence rule depends on.
    public sealed class AdaptiveStateMachineTests
    {
        struct Ctx
        {
            public int EnterCount;
            public int UpdateCount;
            public int ExitCount;
            public bool Alarm;
            public int WantExitTo;
        }

        sealed class Idle : IState<Ctx>
        {
            public const int StateId = 0;
            public int Id => StateId;
            public void Enter(ref Ctx c) => c.EnterCount++;
            public void Exit(ref Ctx c) => c.ExitCount++;
            public void Update(ref Ctx c) => c.UpdateCount++;
            public int EvaluateExit(in Ctx c) => c.WantExitTo;
        }

        sealed class Run : IState<Ctx>
        {
            public const int StateId = 1;
            public int Id => StateId;
            public void Enter(ref Ctx c) => c.EnterCount++;
            public void Exit(ref Ctx c) => c.ExitCount++;
            public void Update(ref Ctx c) => c.UpdateCount++;
            public int EvaluateExit(in Ctx c) => global::StateId.None;
        }

        static AdaptiveStateMachine<Ctx> Machine(params StateTransition<Ctx>[] transitions) =>
            new(new IState<Ctx>[] { new Idle(), new Run() }, transitions);

        static StateTransition<Ctx> AlarmToRun => new()
        {
            From = global::StateId.None,
            Condition = (in Ctx c) => c.Alarm,
            ResolveTo = (int _, in Ctx _) => Run.StateId,
        };

        [Test]
        public void StopReleasesOnceAndPreventsFurtherUpdates()
        {
            var machine = Machine();
            var context = new Ctx { WantExitTo = global::StateId.None };
            machine.Start(ref context, Idle.StateId);
            machine.Stop(ref context);
            machine.Stop(ref context);
            machine.Tick(ref context);
            Assert.AreEqual(1, context.ExitCount);
            Assert.AreEqual(0, context.UpdateCount);
            Assert.AreEqual(global::StateId.None, machine.CurrentId);
            machine.Start(ref context, Run.StateId);
            machine.Tick(ref context);
            Assert.AreEqual(2, context.EnterCount);
            Assert.AreEqual(1, context.UpdateCount);
        }

        [Test]
        public void RestartReleasesPreviousStateButInvalidRestartLeavesItRunning()
        {
            var machine = Machine();
            var context = new Ctx { WantExitTo = global::StateId.None };
            machine.Start(ref context, Idle.StateId);
            Assert.Throws<ArgumentOutOfRangeException>(() => machine.Start(ref context, 99));
            Assert.AreEqual(0, context.ExitCount);
            Assert.AreEqual(Idle.StateId, machine.CurrentId);
            machine.Start(ref context, Run.StateId);
            Assert.AreEqual(1, context.ExitCount);
            Assert.AreEqual(2, context.EnterCount);
        }

        // --- the fix that a creature freezing for a beat exposed ---------------

        [Test]
        public void AStateActsOnTheFrameItIsEntered()
        {
            // The harvested machine returned after switching, spending the whole tick on the transition and
            // producing no intent. For an animal that reads as freezing for a beat before it runs.
            AdaptiveStateMachine<Ctx> m = Machine(AlarmToRun);
            var c = new Ctx { WantExitTo = global::StateId.None };
            m.Start(ref c, Idle.StateId);

            c.UpdateCount = 0;
            c.Alarm = true;
            m.Tick(ref c);

            Assert.AreEqual(Run.StateId, m.CurrentId);
            Assert.AreEqual(1, c.UpdateCount, "the state it switched INTO must run this tick, not next tick");
        }

        [Test]
        public void EnterAndExitFireExactlyOncePerSwitch()
        {
            AdaptiveStateMachine<Ctx> m = Machine(AlarmToRun);
            var c = new Ctx { WantExitTo = global::StateId.None };
            m.Start(ref c, Idle.StateId);
            Assert.AreEqual(1, c.EnterCount, "Start enters");

            c.Alarm = true;
            m.Tick(ref c);
            Assert.AreEqual(1, c.ExitCount);
            Assert.AreEqual(2, c.EnterCount);

            // Still alarmed, already running: the condition resolves to the state we are in, so nothing churns.
            m.Tick(ref c);
            Assert.AreEqual(1, c.ExitCount, "re-resolving to the current state must not re-enter it");
            Assert.AreEqual(2, c.EnterCount);
        }

        [Test]
        public void AStateCanEndItself()
        {
            AdaptiveStateMachine<Ctx> m = Machine();
            var c = new Ctx { WantExitTo = Run.StateId };
            m.Start(ref c, Idle.StateId);

            m.Tick(ref c);
            Assert.AreEqual(Run.StateId, m.CurrentId,
                "EvaluateExit is how a behaviour ends on a condition only it can see");
        }

        [Test]
        public void ATransitionBeatsAStatesOwnExit()
        {
            // Fear has to override whatever the animal was doing, including its own plans.
            AdaptiveStateMachine<Ctx> m = Machine(new StateTransition<Ctx>
            {
                From = global::StateId.None,
                Condition = (in Ctx c) => c.Alarm,
                ResolveTo = (int _, in Ctx _) => Idle.StateId,
            });
            var c = new Ctx { WantExitTo = Run.StateId, Alarm = true };
            m.Start(ref c, Idle.StateId);

            m.Tick(ref c);
            Assert.AreEqual(Idle.StateId, m.CurrentId, "the transition won, so the self-exit never ran");
        }

        [Test]
        public void ResolveToSeesWhereItCameFrom()
        {
            // This is what "flee exits back to whatever I was doing" needs, without states knowing each other.
            int observedFrom = -99;
            AdaptiveStateMachine<Ctx> m = Machine(new StateTransition<Ctx>
            {
                From = global::StateId.None,
                Condition = (in Ctx c) => c.Alarm,
                ResolveTo = (int from, in Ctx _) => { observedFrom = from; return Run.StateId; },
            });
            var c = new Ctx { WantExitTo = global::StateId.None, Alarm = true };
            m.Start(ref c, Idle.StateId);

            m.Tick(ref c);
            Assert.AreEqual(Idle.StateId, observedFrom);
        }

        // --- the id contract the persistence rule leans on ---------------------

        [Test]
        public void CurrentIdIsTheStatesOwnId_SoItCanBeWrittenDownAndRestored()
        {
            AdaptiveStateMachine<Ctx> m = Machine();
            var c = new Ctx { WantExitTo = global::StateId.None };

            m.Start(ref c, Run.StateId);
            Assert.AreEqual(Run.StateId, m.CurrentId);

            // Restoring is the same call with a saved number - there is no Type to look up and no mapping
            // table to drift, which is the whole reason the machine keys on an integer.
            AdaptiveStateMachine<Ctx> restored = Machine();
            var c2 = new Ctx { WantExitTo = global::StateId.None };
            restored.Start(ref c2, m.CurrentId);
            Assert.AreEqual(Run.StateId, restored.CurrentId);
        }

        // --- construction refuses what would fail silently ---------------------

        [Test]
        public void DuplicateStateIdsThrow_RatherThanQuietlyDroppingABehaviour()
        {
            Assert.Throws<ArgumentException>(() =>
                new AdaptiveStateMachine<Ctx>(new IState<Ctx>[] { new Idle(), new Idle() }));
        }

        [Test]
        public void AnEmptyMachineThrows()
        {
            Assert.Throws<ArgumentException>(() => new AdaptiveStateMachine<Ctx>(Array.Empty<IState<Ctx>>()));
        }

        [Test]
        public void ATransitionFromAnUnknownStateThrows()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => Machine(new StateTransition<Ctx>
            {
                From = 7,
                Condition = (in Ctx _) => true,
                ResolveTo = (int _, in Ctx _) => Run.StateId,
            }));
        }

        [Test]
        public void ATransitionMissingItsPartsThrows()
        {
            Assert.Throws<ArgumentException>(() => Machine(new StateTransition<Ctx> { From = global::StateId.None }));
        }

        [Test]
        public void StartingInAnUnknownStateThrows()
        {
            AdaptiveStateMachine<Ctx> m = Machine();
            var c = new Ctx();
            Assert.Throws<ArgumentOutOfRangeException>(() => m.Start(ref c, 42));
        }
    }
}
