using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Plc.Handshake;

/// <summary>
/// Implementation of IPlcHandshakeService.
/// Coordinates the bit and word handshakes using addresses from PlcConfiguration.
/// </summary>
public sealed class PlcHandshakeService : IPlcHandshakeService
{
    private readonly IPlcService _plc;
    private readonly PlcConfiguration _config;
    private readonly ILogger<PlcHandshakeService> _logger;

    public PlcHandshakeService(
        IPlcService plc,
        PlcConfiguration config,
        ILogger<PlcHandshakeService> logger)
    {
        _plc = plc ?? throw new ArgumentNullException(nameof(plc));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SetReadyAsync(bool ready, CancellationToken cancellationToken = default)
    {
        var address = _config.Addresses.Ready;
        if (string.IsNullOrWhiteSpace(address))
        {
            _logger.LogDebug("[HANDSHAKE] Ready address not configured — skipped.");
            return;
        }

        await _plc.WriteBitAsync(address, ready, cancellationToken);
        _logger.LogInformation("[HANDSHAKE] Ready set to {Value} on {Address}", ready, address);
    }

    public async Task SetBusyAsync(bool busy, CancellationToken cancellationToken = default)
    {
        var address = _config.Addresses.Busy;
        if (string.IsNullOrWhiteSpace(address))
        {
            _logger.LogDebug("[HANDSHAKE] Busy address not configured — skipped.");
            return;
        }

        await _plc.WriteBitAsync(address, busy, cancellationToken);
        _logger.LogInformation("[HANDSHAKE] Busy set to {Value} on {Address}", busy, address);
    }

    public async Task SetCaptureCompleteAsync(bool complete, CancellationToken cancellationToken = default)
    {
        var address = _config.Addresses.CaptureComplete;
        if (string.IsNullOrWhiteSpace(address))
        {
            _logger.LogDebug("[HANDSHAKE] CaptureComplete address not configured — skipped.");
            return;
        }

        await _plc.WriteBitAsync(address, complete, cancellationToken);
        _logger.LogInformation("[HANDSHAKE] CaptureComplete set to {Value} on {Address}", complete, address);
    }

    public async Task SetResultOkAsync(CancellationToken cancellationToken = default)
    {
        var okAddr = _config.Addresses.OK;
        var ngAddr = _config.Addresses.NG;

        if (!string.IsNullOrWhiteSpace(ngAddr))
        {
            await _plc.WriteBitAsync(ngAddr, false, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(okAddr))
        {
            await _plc.WriteBitAsync(okAddr, true, cancellationToken);
            _logger.LogInformation("[HANDSHAKE] OK set to TRUE on {Address}", okAddr);
        }
    }

    public async Task SetResultNgAsync(CancellationToken cancellationToken = default)
    {
        var okAddr = _config.Addresses.OK;
        var ngAddr = _config.Addresses.NG;

        if (!string.IsNullOrWhiteSpace(okAddr))
        {
            await _plc.WriteBitAsync(okAddr, false, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(ngAddr))
        {
            await _plc.WriteBitAsync(ngAddr, true, cancellationToken);
            _logger.LogInformation("[HANDSHAKE] NG set to TRUE on {Address}", ngAddr);
        }
    }

    public async Task SetErrorAsync(bool error, CancellationToken cancellationToken = default)
    {
        var address = _config.Addresses.Error;
        if (string.IsNullOrWhiteSpace(address))
        {
            _logger.LogDebug("[HANDSHAKE] Error address not configured — skipped.");
            return;
        }

        await _plc.WriteBitAsync(address, error, cancellationToken);
        _logger.LogWarning("[HANDSHAKE] Error flag set to {Value} on {Address}", error, address);
    }

    public async Task WriteOcrResultAsync(string resultText, CancellationToken cancellationToken = default)
    {
        var address = _config.Addresses.Result;
        if (string.IsNullOrWhiteSpace(address))
        {
            _logger.LogDebug("[HANDSHAKE] Result address not configured — skipped.");
            return;
        }

        string dataType = _config.ResultDataType?.ToUpperInvariant() ?? "STRING";

        switch (dataType)
        {
            case "WORDARRAY":
            case "ASCII":
                // Encode ASCII string into 16-bit registers (2 chars per word, big-endian)
                _logger.LogInformation("[HANDSHAKE] Writing OCR result '{Text}' as WordArray to {Address}", resultText, address);
                await _plc.WriteStringAsync(address, resultText, cancellationToken);
                break;

            case "STRING":
            default:
                _logger.LogInformation("[HANDSHAKE] Writing OCR result '{Text}' as String to {Address}", resultText, address);
                await _plc.WriteStringAsync(address, resultText, cancellationToken);
                break;
        }
    }

    public async Task ClearResultFlagsAsync(CancellationToken cancellationToken = default)
    {
        var okAddr = _config.Addresses.OK;
        var ngAddr = _config.Addresses.NG;
        var compAddr = _config.Addresses.CaptureComplete;

        if (!string.IsNullOrWhiteSpace(okAddr)) await _plc.WriteBitAsync(okAddr, false, cancellationToken);
        if (!string.IsNullOrWhiteSpace(ngAddr)) await _plc.WriteBitAsync(ngAddr, false, cancellationToken);
        if (!string.IsNullOrWhiteSpace(compAddr)) await _plc.WriteBitAsync(compAddr, false, cancellationToken);

        _logger.LogDebug("[HANDSHAKE] Result flags cleared.");
    }

    public async Task<bool> ReadTriggerAsync(CancellationToken cancellationToken = default)
    {
        var address = _config.Addresses.Trigger;
        if (string.IsNullOrWhiteSpace(address)) return false;

        return await _plc.ReadBitAsync(address, cancellationToken);
    }

    public async Task<bool> ReadResetAsync(CancellationToken cancellationToken = default)
    {
        var address = _config.Addresses.Reset;
        if (string.IsNullOrWhiteSpace(address)) return false;

        return await _plc.ReadBitAsync(address, cancellationToken);
    }
}
