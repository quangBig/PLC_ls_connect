using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Interfaces;

namespace IndustrialVision.Plc.Diagnostics;

public sealed record PlcDiagnosticSnapshot(
    DateTimeOffset ReadAt, bool? Trigger, bool? Complete, ushort? Result,
    bool? Heartbeat, bool? PlcOk, bool? PlcNg);

/// <summary>Read PLC values independently of local UI flags. Does not write any device.</summary>
public sealed class PlcDiagnosticsService(IPlcService plc, PlcConfiguration config)
{
    public async Task<PlcDiagnosticSnapshot> ReadAsync(CancellationToken token = default)
    {
        if (!plc.IsConnected) throw new InvalidOperationException("PLC is not connected.");
        var trigger = await ReadOptionalBitAsync(config.Addresses.Trigger, token);
        var complete = await ReadOptionalBitAsync(config.Addresses.CaptureComplete, token);
        ushort? result = config.UsesVerdictWord && IsConfigured(config.Addresses.Result)
            ? await plc.ReadWordAsync(config.Addresses.Result, token) : null;
        var heartbeat = string.Equals(config.Heartbeat.Mode, "Toggle", StringComparison.OrdinalIgnoreCase)
            ? await ReadOptionalBitAsync(config.Heartbeat.Address, token) : null;
        var ok = await ReadOptionalBitAsync(config.Diagnostics.PlcOkAddress, token);
        var ng = await ReadOptionalBitAsync(config.Diagnostics.PlcNgAddress, token);
        return new(DateTimeOffset.Now, trigger, complete, result, heartbeat, ok, ng);
    }

    private async Task<bool?> ReadOptionalBitAsync(string? address, CancellationToken token)
        => IsConfigured(address) ? await plc.ReadBitAsync(address!, token) : null;

    private static bool IsConfigured(string? address)
        => !string.IsNullOrWhiteSpace(address) && !address.Contains("NEEDS_", StringComparison.OrdinalIgnoreCase);
}
