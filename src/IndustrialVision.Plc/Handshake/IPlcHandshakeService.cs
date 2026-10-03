namespace IndustrialVision.Plc.Handshake;

/// <summary>
/// Abstraction for PLC handshake coordination.
/// Manages Ready, Busy, CaptureComplete, OK, NG, Error, Reset, and Result data
/// according to the configured PlcAddressMap.
/// </summary>
public interface IPlcHandshakeService
{
    /// <summary>Set PLC Ready signal (PC ready for next inspection cycle).</summary>
    Task SetReadyAsync(bool ready, CancellationToken cancellationToken = default);

    /// <summary>Set PLC Busy signal (inspection cycle in progress).</summary>
    Task SetBusyAsync(bool busy, CancellationToken cancellationToken = default);

    /// <summary>Set CaptureComplete signal (camera captured, product can move).</summary>
    Task SetCaptureCompleteAsync(bool complete, CancellationToken cancellationToken = default);

    /// <summary>Set OK inspection result signal.</summary>
    Task SetResultOkAsync(CancellationToken cancellationToken = default);

    /// <summary>Set NG inspection result signal.</summary>
    Task SetResultNgAsync(CancellationToken cancellationToken = default);

    /// <summary>Set Error alarm signal to PLC.</summary>
    Task SetErrorAsync(bool error, CancellationToken cancellationToken = default);

    /// <summary>Write OCR text result to PLC according to configured ResultDataType.</summary>
    Task WriteOcrResultAsync(string resultText, CancellationToken cancellationToken = default);

    /// <summary>Clear result flags (OK, NG, CaptureComplete) before next cycle.</summary>
    Task ClearResultFlagsAsync(CancellationToken cancellationToken = default);

    /// <summary>Read Trigger signal current state from PLC.</summary>
    Task<bool> ReadTriggerAsync(CancellationToken cancellationToken = default);

    /// <summary>Read Reset signal current state from PLC.</summary>
    Task<bool> ReadResetAsync(CancellationToken cancellationToken = default);
}
