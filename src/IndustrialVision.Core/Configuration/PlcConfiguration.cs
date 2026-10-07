using IndustrialVision.Core.Models;

namespace IndustrialVision.Core.Configuration;

/// <summary>
/// PLC configuration — all values read from plc.json.
/// 
/// ❌ NEVER HARDCODE:
/// - Brand / Model (e.g. LS XGB, XGK, XGI, XEC)
/// - Communication Protocol (e.g. FEnet, XGT Dedicated, Modbus TCP, Cnet)
/// - IP Address, Port, Timeouts, Signal Addresses
/// </summary>
public sealed class PlcConfiguration
{
    /// <summary>PLC identifier name.</summary>
    public string Name { get; set; } = "PLC_LS";

    /// <summary>PLC hardware model (e.g. XGB, XGK, XGI, XEC). NEEDS_PLC_INFORMATION.</summary>
    public string Model { get; set; } = "NEEDS_PLC_INFORMATION";

    /// <summary>Communication protocol. NEEDS_PLC_PROTOCOL_INFORMATION.</summary>
    public string Protocol { get; set; } = "NEEDS_PLC_PROTOCOL_INFORMATION";

    /// <summary>PLC IP address. NEEDS_CONFIGURATION.</summary>
    public string IpAddress { get; set; } = string.Empty;

    /// <summary>PLC port. NEEDS_CONFIGURATION.</summary>
    public int Port { get; set; }

    /// <summary>Connection timeout in milliseconds.</summary>
    public int ConnectionTimeoutMs { get; set; } = 3000;

    /// <summary>Read timeout in milliseconds.</summary>
    public int ReadTimeoutMs { get; set; } = 3000;

    /// <summary>Write timeout in milliseconds.</summary>
    public int WriteTimeoutMs { get; set; } = 3000;

    /// <summary>Number of retries upon communication failure.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Delay between reconnect attempts in milliseconds.</summary>
    public int ReconnectDelayMs { get; set; } = 1000;

    /// <summary>
    /// Result format: "String", "WordArray", "Ascii", or "VERDICT_WORD" (0=empty, 1=OK, 2=NG).
    /// </summary>
    public string ResultDataType { get; set; } = "UNKNOWN";

    /// <summary>Assert completion only after processing and result writes.</summary>
    public bool CaptureCompleteAfterResult { get; set; }

    /// <summary>In AUTO, wait for PLC to lower Trigger, then clear completion and the verdict word before Ready.</summary>
    public bool WaitForTriggerResetAfterResult { get; set; }

    /// <summary>Maximum wait for the PLC to acknowledge completion.</summary>
    public int TriggerResetTimeoutMs { get; set; } = 3000;

    /// <summary>Require an explicitly mapped Ready bit before arming AUTO.</summary>
    public bool RequiresReadySignal { get; set; }

    public bool UsesVerdictWord => string.Equals(ResultDataType, "VERDICT_WORD", StringComparison.OrdinalIgnoreCase);

    /// <summary>PLC signal address mapping — all from configuration.</summary>
    public PlcAddressMap Addresses { get; set; } = new();

    /// <summary>Heartbeat configuration.</summary>
    public PlcHeartbeatConfiguration Heartbeat { get; set; } = new();

    /// <summary>Read-only diagnostics; these PLC-owned outputs are never written by the handshake.</summary>
    public PlcDiagnosticsConfiguration Diagnostics { get; set; } = new();

    // ── Backward-compatibility bridges ──────────────────────────────
    public int ConnectTimeoutMs
    {
        get => ConnectionTimeoutMs;
        set => ConnectionTimeoutMs = value;
    }

    public int ReadWriteTimeoutMs
    {
        get => ReadTimeoutMs;
        set { ReadTimeoutMs = value; WriteTimeoutMs = value; }
    }

    public PlcSignalConfiguration Signals
    {
        get => new PlcSignalConfiguration
        {
            Ready = Addresses.Ready,
            Trigger = Addresses.Trigger,
            Busy = Addresses.Busy,
            Complete = Addresses.CaptureComplete,
            Ok = Addresses.OK,
            Ng = Addresses.NG,
            Error = Addresses.Error,
            Reset = Addresses.Reset,
            Heartbeat = Addresses.Heartbeat,
            Result = Addresses.Result
        };
        set
        {
            if (value != null)
            {
                Addresses.Ready = value.Ready;
                Addresses.Trigger = value.Trigger;
                Addresses.Busy = value.Busy;
                Addresses.CaptureComplete = value.Complete;
                Addresses.OK = value.Ok;
                Addresses.NG = value.Ng;
                Addresses.Error = value.Error;
                Addresses.Reset = value.Reset;
                Addresses.Heartbeat = value.Heartbeat;
                Addresses.Result = value.Result;
            }
        }
    }
}

/// <summary>
/// Heartbeat configuration for PLC liveness monitoring.
/// </summary>
public sealed class PlcHeartbeatConfiguration
{
    /// <summary>Whether heartbeat is active. Default false until specification is provided.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Heartbeat address in PLC.</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>Heartbeat interval in milliseconds.</summary>
    public int IntervalMs { get; set; } = 1000;

    /// <summary>Heartbeat mode: "Toggle" (0->1->0) or "Counter" (incremental).</summary>
    public string Mode { get; set; } = "Toggle";
}

/// <summary>
/// Legacy signal configuration bridge.
/// </summary>
public sealed class PlcSignalConfiguration
{
    public string Trigger { get; set; } = string.Empty;
    public string Ready { get; set; } = string.Empty;
    public string Busy { get; set; } = string.Empty;
    public string Complete { get; set; } = string.Empty;
    public string Ok { get; set; } = string.Empty;
    public string Ng { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string Reset { get; set; } = string.Empty;
    public string Heartbeat { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
}

/// <summary>Optional periodic reads of handshake registers and PLC-owned result bits.</summary>
public sealed class PlcDiagnosticsConfiguration
{
    public bool Enabled { get; set; }
    public int PollIntervalMs { get; set; } = 500;
    public string PlcOkAddress { get; set; } = string.Empty;
    public string PlcNgAddress { get; set; } = string.Empty;
}
