using System;
using System.Collections.Generic;
using DS4Windows;

namespace DS4WinWPF.DS4Control;

/// <summary>
/// Button debouncer. The first edge of a button change goes through
/// immediately, then the button state is locked for the configured duration so
/// contact chatter (on press and on release) gets filtered out.
/// Runs on the device input thread for every report, so it avoids allocations
/// and reflection.
/// </summary>
public class Debouncer
{
    private delegate ref bool ButtonAccessor(DS4State state);

    private static readonly Dictionary<string, ButtonAccessor> KnownButtons = new()
    {
        [nameof(DS4State.Cross)] = s => ref s.Cross,
        [nameof(DS4State.Triangle)] = s => ref s.Triangle,
        [nameof(DS4State.Circle)] = s => ref s.Circle,
        [nameof(DS4State.Square)] = s => ref s.Square,
        [nameof(DS4State.R3)] = s => ref s.R3,
        [nameof(DS4State.L3)] = s => ref s.L3,
        [nameof(DS4State.Options)] = s => ref s.Options,
        [nameof(DS4State.Share)] = s => ref s.Share,
        [nameof(DS4State.R2Btn)] = s => ref s.R2Btn,
        [nameof(DS4State.L2Btn)] = s => ref s.L2Btn,
        [nameof(DS4State.R1)] = s => ref s.R1,
        [nameof(DS4State.L1)] = s => ref s.L1,
        [nameof(DS4State.PS)] = s => ref s.PS,
        [nameof(DS4State.TouchButton)] = s => ref s.TouchButton,
        [nameof(DS4State.Capture)] = s => ref s.Capture,
        [nameof(DS4State.SideL)] = s => ref s.SideL,
        [nameof(DS4State.SideR)] = s => ref s.SideR,
        [nameof(DS4State.DpadUp)] = s => ref s.DpadUp,
        [nameof(DS4State.DpadDown)] = s => ref s.DpadDown,
        [nameof(DS4State.DpadLeft)] = s => ref s.DpadLeft,
        [nameof(DS4State.DpadRight)] = s => ref s.DpadRight,
        [nameof(DS4State.Mute)] = s => ref s.Mute,
        [nameof(DS4State.FnL)] = s => ref s.FnL,
        [nameof(DS4State.FnR)] = s => ref s.FnR,
        [nameof(DS4State.BLP)] = s => ref s.BLP,
        [nameof(DS4State.BRP)] = s => ref s.BRP,
    };

    private readonly Func<double> _durationMsProvider;
    private readonly List<DebouncerInstance> _debouncers = new();
    // Reused output state. Only touched from the device input thread
    private readonly DS4State _outputState = new();

    public Debouncer(TimeSpan duration) : this(() => duration.TotalMilliseconds)
    {
    }

    /// <param name="durationMsProvider">Returns the current debounce duration in ms.
    /// Read for every report so setting changes apply immediately</param>
    public Debouncer(Func<double> durationMsProvider)
    {
        _durationMsProvider = durationMsProvider;
    }

    public void AddDebouncer(string name)
    {
        if (!KnownButtons.TryGetValue(name, out ButtonAccessor accessor))
        {
            throw new ArgumentException($"Unknown button field {name}", nameof(name));
        }

        _debouncers.RemoveAll(d => d.Name == name);
        _debouncers.Add(new DebouncerInstance(name, accessor));
    }

    public DS4State ProcessInput(DS4State cState)
    {
        double durationMs = _durationMsProvider();
        if (durationMs <= 0)
        {
            // Keep tracking the raw state so a later activation starts clean
            for (int i = 0; i < _debouncers.Count; i++)
            {
                _debouncers[i].Track(cState);
            }

            return cState;
        }

        cState.CopyTo(_outputState);
        DateTime timestamp = cState.ReportTimeStamp;
        for (int i = 0; i < _debouncers.Count; i++)
        {
            _debouncers[i].Process(_outputState, timestamp, durationMs);
        }

        return _outputState;
    }

    private sealed class DebouncerInstance(string name, ButtonAccessor accessor)
    {
        private bool _outputState;
        private DateTime _lastChangeTime = DateTime.MinValue;

        public string Name { get; } = name;

        public void Track(DS4State state)
        {
            _outputState = accessor(state);
        }

        public void Process(DS4State state, DateTime timestamp, double durationMs)
        {
            ref bool button = ref accessor(state);
            if (button != _outputState &&
                (timestamp - _lastChangeTime).TotalMilliseconds >= durationMs)
            {
                // Accept the new state and ignore further changes for the duration
                _outputState = button;
                _lastChangeTime = timestamp;
            }

            button = _outputState;
        }
    }
}
