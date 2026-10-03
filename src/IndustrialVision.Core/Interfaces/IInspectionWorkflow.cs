using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Models;

namespace IndustrialVision.Core.Interfaces;

/// <summary>
/// Inspection workflow engine.
/// Orchestrates the complete inspection cycle: PLC → Light → Camera → OCR → PLC.
/// </summary>
public interface IInspectionWorkflow
{
    /// <summary>Current machine state.</summary>
    MachineState CurrentState { get; }

    /// <summary>Current operation mode (Auto/Manual).</summary>
    OperationMode CurrentMode { get; }

    /// <summary>Whether the auto cycle is running.</summary>
    bool IsRunning { get; }

    /// <summary>Raised when machine state changes.</summary>
    event EventHandler<MachineState> StateChanged;

    /// <summary>Raised when an inspection cycle completes.</summary>
    event EventHandler<InspectionCycleResult> CycleCompleted;

    /// <summary>Start the auto inspection cycle.</summary>
    Task StartAutoAsync(CancellationToken cancellationToken = default);

    /// <summary>Stop the auto inspection cycle.</summary>
    Task StopAutoAsync();

    /// <summary>Run a single manual inspection cycle.</summary>
    Task<InspectionCycleResult> RunSingleCycleAsync(CancellationToken cancellationToken = default);

    /// <summary>Reset the system to Ready state after an error.</summary>
    Task ResetAsync();
}
