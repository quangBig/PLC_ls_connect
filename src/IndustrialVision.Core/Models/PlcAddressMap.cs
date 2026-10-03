namespace IndustrialVision.Core.Models;

/// <summary>
/// PLC signal address mapping.
/// All addresses are defined in configuration (plc.json) — NEVER hardcoded.
/// If any address is unknown or not used, it remains empty or marked NEEDS_CONFIGURATION.
/// A value containing "NEEDS_" is normalized to an empty string, so every consumer can simply use
/// string.IsNullOrWhiteSpace(...) to know whether a signal is configured. The PLC is never asked
/// to read/write a placeholder text.
/// </summary>
public class PlcAddressMap
{
    private string _ready = string.Empty;
    private string _trigger = string.Empty;
    private string _busy = string.Empty;
    private string _captureComplete = string.Empty;
    private string _result = string.Empty;
    private string _ok = string.Empty;
    private string _ng = string.Empty;
    private string _error = string.Empty;
    private string _reset = string.Empty;
    private string _heartbeat = string.Empty;

    /// <summary>Ready signal to PLC (Bit: PC is ready for next cycle).</summary>
    public string Ready { get => _ready; set => _ready = Normalize(value); }

    /// <summary>Trigger signal from PLC (Bit: Rising edge 0->1 starts inspection).</summary>
    public string Trigger { get => _trigger; set => _trigger = Normalize(value); }

    /// <summary>Busy signal to PLC (Bit: Inspection cycle is in progress).</summary>
    public string Busy { get => _busy; set => _busy = Normalize(value); }

    /// <summary>Capture complete signal to PLC (Bit: Image capture is finished, machine may advance).</summary>
    public string CaptureComplete { get => _captureComplete; set => _captureComplete = Normalize(value); }

    /// <summary>Result string or code address in PLC (Word/Register/String).</summary>
    public string Result { get => _result; set => _result = Normalize(value); }

    /// <summary>OK inspection result signal to PLC (Bit: Product passed).</summary>
    public string OK { get => _ok; set => _ok = Normalize(value); }

    /// <summary>NG inspection result signal to PLC (Bit: Product failed).</summary>
    public string NG { get => _ng; set => _ng = Normalize(value); }

    /// <summary>Error signal to PLC (Bit: System alarm/vision error).</summary>
    public string Error { get => _error; set => _error = Normalize(value); }

    /// <summary>Reset signal from PLC (Bit: Clear alarms/reset cycle).</summary>
    public string Reset { get => _reset; set => _reset = Normalize(value); }

    /// <summary>Heartbeat address in PLC (Bit toggle or Word counter).</summary>
    public string Heartbeat { get => _heartbeat; set => _heartbeat = Normalize(value); }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("NEEDS_", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return value.Trim();
    }
}
