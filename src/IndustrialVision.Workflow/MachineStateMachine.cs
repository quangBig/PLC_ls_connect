using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Interfaces;
using IndustrialVision.Core.Models;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Workflow;

/// <summary>
/// Machine state machine — manages transitions between inspection cycle states.
/// State transitions follow the defined workflow and prevent invalid transitions.
/// 
/// Full implementation in Phase 6.
/// Phase 1: Basic state tracking and transition logging.
/// </summary>
public sealed class MachineStateMachine
{
    private readonly ILogger<MachineStateMachine> _logger;
    private MachineState _currentState = MachineState.Stopped;
    private readonly object _stateLock = new();

    public MachineState CurrentState
    {
        get
        {
            lock (_stateLock) return _currentState;
        }
    }

    public event EventHandler<MachineState>? StateChanged;

    public MachineStateMachine(ILogger<MachineStateMachine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Transition to a new state. Logs the transition and validates it.
    /// </summary>
    public bool TransitionTo(MachineState newState)
    {
        lock (_stateLock)
        {
            var previousState = _currentState;

            if (!IsValidTransition(previousState, newState))
            {
                _logger.LogWarning(
                    "Invalid state transition attempted: {From} → {To}",
                    previousState, newState);
                return false;
            }

            _currentState = newState;
            _logger.LogInformation("State transition: {From} → {To}", previousState, newState);
            StateChanged?.Invoke(this, newState);
            return true;
        }
    }

    /// <summary>
    /// Force transition to Error state from any state.
    /// </summary>
    public void ForceError()
    {
        lock (_stateLock)
        {
            var previousState = _currentState;
            _currentState = MachineState.Error;
            _logger.LogError("Forced error state from {From}", previousState);
            StateChanged?.Invoke(this, MachineState.Error);
        }
    }

    /// <summary>
    /// Validate that a transition is allowed.
    /// </summary>
    private static bool IsValidTransition(MachineState from, MachineState to)
    {
        // Error and Stopped can be reached from any state
        if (to == MachineState.Error || to == MachineState.Stopped)
            return true;

        // Reset from Error goes to Ready
        if (from == MachineState.Error && to == MachineState.Ready)
            return true;

        return (from, to) switch
        {
            (MachineState.Stopped, MachineState.Starting) => true,
            (MachineState.Starting, MachineState.Initializing) => true,
            (MachineState.Initializing, MachineState.Connecting) => true,
            (MachineState.Connecting, MachineState.Ready) => true,
            (MachineState.Ready, MachineState.WaitingTrigger) => true,
            (MachineState.WaitingTrigger, MachineState.Lighting) => true,
            (MachineState.Lighting, MachineState.Capturing) => true,
            (MachineState.Capturing, MachineState.Processing) => true,
            (MachineState.Processing, MachineState.SendingResult) => true,
            (MachineState.SendingResult, MachineState.Completed) => true,
            (MachineState.Completed, MachineState.WaitingTrigger) => true,
            (MachineState.Completed, MachineState.Ready) => true,
            _ => false
        };
    }
}
