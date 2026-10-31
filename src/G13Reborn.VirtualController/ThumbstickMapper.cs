namespace G13Reborn.VirtualController;

/// <summary>Calibrates raw G13 thumbstick bytes for a conventional analog controller.</summary>
public static class ThumbstickMapper
{
    /// <param name="deadzone">Radial deadzone, from 0.0 up to (but not including) 1.0.</param>
    /// <returns>Normalized X/Y axes, each constrained to [-1, 1].</returns>
    public static (double X, double Y) Map(
        byte rawX, byte rawY, double deadzone = 0.10, bool invertY = false)
    {
        if (deadzone < 0 || deadzone >= 1 || double.IsNaN(deadzone))
            throw new ArgumentOutOfRangeException(nameof(deadzone));

        // 127.5 maps both ends (0 and 255) symmetrically.
        double x = (rawX - 127.5) / 127.5;
        double y = (rawY - 127.5) / 127.5;
        if (invertY) y = -y;

        double length = Math.Sqrt(x * x + y * y);
        if (length <= deadzone)
            return (0, 0);

        // Remove a radial deadzone and rescale remaining travel to full deflection.
        // Clamp diagonal travel to the unit circle while preserving its direction.
        double scaledLength = (Math.Min(1, length) - deadzone) / (1 - deadzone);
        double factor = scaledLength / length;
        return (Math.Clamp(x * factor, -1, 1), Math.Clamp(y * factor, -1, 1));
    }
}
