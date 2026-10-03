using IndustrialVision.Core.Configuration;
using IndustrialVision.Core.Enums;
using IndustrialVision.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialVision.Plc;

/// <summary>
/// Mock PLC service for Simulation Mode.
/// Simulates PLC communication with in-memory storage.
/// Supports Bit, Word, Integer, and String registers.
/// </summary>
public sealed class MockPlcService : IPlcService
{
    private readonly PlcConfiguration _config;
    private readonly ILogger<MockPlcService> _logger;
    private readonly Dictionary<string, object> _memory = new();
    private ConnectionStatus _status = ConnectionStatus.Disconnected;
    private bool _disposed;

    public ConnectionStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(this, value);
            }
        }
    }

    public bool IsConnected => _status == ConnectionStatus.Connected || _status == ConnectionStatus.Ready;

    public event EventHandler<ConnectionStatus>? StatusChanged;

    public MockPlcService(PlcConfiguration config, ILogger<MockPlcService> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[SIMULATION] PLC connecting to {Ip}:{Port}...", _config.IpAddress, _config.Port);
        Status = ConnectionStatus.Connecting;
        Status = ConnectionStatus.Connected;
        Status = ConnectionStatus.Ready;
        _logger.LogInformation("[SIMULATION] PLC connected and ready.");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _logger.LogInformation("[SIMULATION] PLC disconnecting...");
        Status = ConnectionStatus.Disconnected;
        _memory.Clear();
        _logger.LogInformation("[SIMULATION] PLC disconnected.");
        return Task.CompletedTask;
    }

    public Task<bool> IsConnectedAsync()
    {
        return Task.FromResult(IsConnected);
    }

    public Task<bool> ReadBoolAsync(string address, CancellationToken cancellationToken = default)
    {
        var value = _memory.TryGetValue(address, out var obj) && obj is bool b && b;
        _logger.LogDebug("[SIMULATION] PLC ReadBool({Address}) = {Value}", address, value);
        return Task.FromResult(value);
    }

    public Task<bool> ReadBitAsync(string address, CancellationToken cancellationToken = default)
        => ReadBoolAsync(address, cancellationToken);

    public Task WriteBoolAsync(string address, bool value, CancellationToken cancellationToken = default)
    {
        _memory[address] = value;
        _logger.LogDebug("[SIMULATION] PLC WriteBool({Address}, {Value})", address, value);
        return Task.CompletedTask;
    }

    public Task WriteBitAsync(string address, bool value, CancellationToken cancellationToken = default)
        => WriteBoolAsync(address, value, cancellationToken);

    public Task<ushort> ReadWordAsync(string address, CancellationToken cancellationToken = default)
    {
        var value = _memory.TryGetValue(address, out var obj) && obj is ushort u ? u : (ushort)0;
        _logger.LogDebug("[SIMULATION] PLC ReadWord({Address}) = {Value}", address, value);
        return Task.FromResult(value);
    }

    public Task WriteWordAsync(string address, ushort value, CancellationToken cancellationToken = default)
    {
        _memory[address] = value;
        _logger.LogDebug("[SIMULATION] PLC WriteWord({Address}, {Value})", address, value);
        return Task.CompletedTask;
    }

    public Task<int> ReadIntAsync(string address, CancellationToken cancellationToken = default)
    {
        var value = _memory.TryGetValue(address, out var obj) && obj is int i ? i : 0;
        _logger.LogDebug("[SIMULATION] PLC ReadInt({Address}) = {Value}", address, value);
        return Task.FromResult(value);
    }

    public Task WriteIntAsync(string address, int value, CancellationToken cancellationToken = default)
    {
        _memory[address] = value;
        _logger.LogDebug("[SIMULATION] PLC WriteInt({Address}, {Value})", address, value);
        return Task.CompletedTask;
    }

    public Task<string> ReadStringAsync(string address, CancellationToken cancellationToken = default)
    {
        var value = _memory.TryGetValue(address, out var obj) && obj is string s ? s : string.Empty;
        _logger.LogDebug("[SIMULATION] PLC ReadString({Address}) = {Value}", address, value);
        return Task.FromResult(value);
    }

    public Task<string> ReadStringAsync(string address, int length, CancellationToken cancellationToken = default)
    {
        var value = _memory.TryGetValue(address, out var obj) && obj is string s ? s : string.Empty;
        if (length > 0 && value.Length > length)
        {
            value = value[..length];
        }
        _logger.LogDebug("[SIMULATION] PLC ReadString({Address}, len:{Length}) = {Value}", address, length, value);
        return Task.FromResult(value);
    }

    public Task WriteStringAsync(string address, string value, CancellationToken cancellationToken = default)
    {
        _memory[address] = value;
        _logger.LogDebug("[SIMULATION] PLC WriteString({Address}, {Value})", address, value);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _memory.Clear();
        Status = ConnectionStatus.Disconnected;
    }
}
