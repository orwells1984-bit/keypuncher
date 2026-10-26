namespace Keypuncher.Core;

/// <summary>The shapes the touch exit asks a grown-up to tap, by name.</summary>
public enum GateShape { Circle, Square, Triangle, Star, Heart, Moon }

/// <summary>One shape of the touch exit's challenge: where it sits (0..1 of the screen), its radius as a
/// share of the short screen side, and whether it has already been tapped in the right order.</summary>
public readonly record struct GateTarget(GateShape Shape, double X, double Y, double Radius, bool Tapped);
