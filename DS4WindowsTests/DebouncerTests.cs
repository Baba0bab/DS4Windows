using System;
using DS4Windows;
using DS4WinWPF.DS4Control;

namespace DS4WindowsTests
{
    [TestClass]
    public class DebouncerTests
    {
        private static readonly DateTime Start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        private static DS4State State(bool cross, double ms)
        {
            return new DS4State() { Cross = cross, ReportTimeStamp = Start.AddMilliseconds(ms) };
        }

        private static Debouncer CreateDebouncer(double durationMs)
        {
            Debouncer debouncer = new Debouncer(() => durationMs);
            debouncer.AddDebouncer(nameof(DS4State.Cross));
            return debouncer;
        }

        [TestMethod]
        public void DisabledDebouncerReturnsInputUnchanged()
        {
            Debouncer debouncer = CreateDebouncer(0);
            DS4State input = State(true, 0);
            Assert.AreSame(input, debouncer.ProcessInput(input));
        }

        [TestMethod]
        public void FirstPressIsNotDelayed()
        {
            Debouncer debouncer = CreateDebouncer(20);
            Assert.IsFalse(debouncer.ProcessInput(State(false, 0)).Cross);
            Assert.IsTrue(debouncer.ProcessInput(State(true, 1)).Cross);
        }

        [TestMethod]
        public void ChatterIsFilteredOnPressAndRelease()
        {
            Debouncer debouncer = CreateDebouncer(20);
            debouncer.ProcessInput(State(false, 0));

            // Press with contact bounce
            Assert.IsTrue(debouncer.ProcessInput(State(true, 1)).Cross);
            Assert.IsTrue(debouncer.ProcessInput(State(false, 3)).Cross);
            Assert.IsTrue(debouncer.ProcessInput(State(true, 5)).Cross);
            Assert.IsTrue(debouncer.ProcessInput(State(true, 30)).Cross);

            // Release with contact bounce
            Assert.IsFalse(debouncer.ProcessInput(State(false, 40)).Cross);
            Assert.IsFalse(debouncer.ProcessInput(State(true, 42)).Cross);
            Assert.IsFalse(debouncer.ProcessInput(State(false, 45)).Cross);

            // New press after the lock duration
            Assert.IsTrue(debouncer.ProcessInput(State(true, 70)).Cross);
        }

        [TestMethod]
        public void DoesNotModifyInputState()
        {
            Debouncer debouncer = CreateDebouncer(20);
            debouncer.ProcessInput(State(true, 0));
            DS4State input = State(false, 5);
            DS4State output = debouncer.ProcessInput(input);
            Assert.IsTrue(output.Cross);
            Assert.IsFalse(input.Cross);
        }

        [TestMethod]
        public void DurationChangeAppliesImmediately()
        {
            double duration = 0;
            Debouncer debouncer = new Debouncer(() => duration);
            debouncer.AddDebouncer(nameof(DS4State.Cross));

            debouncer.ProcessInput(State(false, 0));
            duration = 50;
            Assert.IsTrue(debouncer.ProcessInput(State(true, 10)).Cross);
            Assert.IsTrue(debouncer.ProcessInput(State(false, 20)).Cross);
            Assert.IsFalse(debouncer.ProcessInput(State(false, 70)).Cross);
        }

        [TestMethod]
        public void UnknownButtonIsRejected()
        {
            Debouncer debouncer = new Debouncer(() => 10);
            Assert.ThrowsException<ArgumentException>(() => debouncer.AddDebouncer("NotAButton"));
        }
    }
}
