namespace IndustrialVision.Core.Exceptions;

/// <summary>
/// Base exception for all IndustrialVision hardware/service errors.
/// </summary>
public class IndustrialVisionException : Exception
{
    public string Component { get; }

    public IndustrialVisionException(string component, string message)
        : base(message)
    {
        Component = component;
    }

    public IndustrialVisionException(string component, string message, Exception innerException)
        : base(message, innerException)
    {
        Component = component;
    }
}

/// <summary>
/// Camera-specific exception.
/// </summary>
public class CameraException : IndustrialVisionException
{
    public CameraException(string message)
        : base("Camera", message) { }

    public CameraException(string message, Exception innerException)
        : base("Camera", message, innerException) { }
}

/// <summary>
/// PLC-specific exception.
/// </summary>
public class PlcException : IndustrialVisionException
{
    public PlcException(string message)
        : base("PLC", message) { }

    public PlcException(string message, Exception innerException)
        : base("PLC", message, innerException) { }
}

/// <summary>
/// The PLC link is healthy but a single request was rejected (invalid address, PLC NAK, bad data).
/// Not retried and does not put the PLC connection into Error state.
/// </summary>
public class PlcRequestException : PlcException
{
    public PlcRequestException(string message)
        : base(message) { }

    public PlcRequestException(string message, Exception innerException)
        : base(message, innerException) { }
}

/// <summary>
/// Light controller-specific exception.
/// </summary>
public class LightControllerException : IndustrialVisionException
{
    public LightControllerException(string message)
        : base("Light", message) { }

    public LightControllerException(string message, Exception innerException)
        : base("Light", message, innerException) { }
}

/// <summary>
/// OCR service-specific exception.
/// </summary>
public class OcrException : IndustrialVisionException
{
    public OcrException(string message)
        : base("OCR", message) { }

    public OcrException(string message, Exception innerException)
        : base("OCR", message, innerException) { }
}

/// <summary>
/// Configuration validation exception.
/// </summary>
public class ConfigurationValidationException : IndustrialVisionException
{
    public IReadOnlyList<string> ValidationErrors { get; }

    public ConfigurationValidationException(IReadOnlyList<string> errors)
        : base("Configuration", $"Configuration validation failed with {errors.Count} error(s).")
    {
        ValidationErrors = errors;
    }
}
