namespace Golether.Media.Conference.GStreamer.Native;

/// <summary>
/// An argument of an action signal.
/// </summary>
/// <param name="Type">The GType.</param>
/// <param name="Pointer">The boxed pointer (boxed types).</param>
/// <param name="Number">The number (<see cref="Gst.TypeUInt"/>).</param>
/// <param name="Text">The text (<see cref="Gst.TypeString"/>).</param>
/// <param name="IsObject">Whether <paramref name="Pointer"/> is a GObject (may be zero).</param>
internal readonly record struct SignalArg(nuint Type, nint Pointer, uint Number, string? Text, bool IsObject = false)
{
    /// <summary>
    /// Creates a boxed argument.
    /// </summary>
    /// <param name="type">The boxed type.</param>
    /// <param name="pointer">The pointer (may be zero).</param>
    /// <returns>The argument.</returns>
    public static SignalArg Boxed(nuint type, nint pointer) => new(type, pointer, 0, null);

    /// <summary>
    /// Creates an unsigned integer argument.
    /// </summary>
    /// <param name="number">The number.</param>
    /// <returns>The argument.</returns>
    public static SignalArg UInt(uint number) => new(Gst.TypeUInt, 0, number, null);

    /// <summary>
    /// Creates a signed integer argument.
    /// </summary>
    /// <param name="number">The number.</param>
    /// <returns>The argument.</returns>
    public static SignalArg Int(int number) => new(Gst.TypeInt, 0, unchecked((uint)number), null);

    /// <summary>
    /// Creates a string argument.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The argument.</returns>
    public static SignalArg String(string text) => new(Gst.TypeString, 0, 0, text);

    /// <summary>
    /// Creates an object argument.
    /// </summary>
    /// <param name="type">The object type.</param>
    /// <param name="pointer">The object, or zero for none.</param>
    /// <returns>The argument.</returns>
    public static SignalArg Object(nuint type, nint pointer) => new(type, pointer, 0, null, true);
}
