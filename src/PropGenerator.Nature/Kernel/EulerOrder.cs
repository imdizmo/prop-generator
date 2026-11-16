// Ported from three.js r186 src/math/Euler.js (MIT, © 2010-2026 three.js authors).
namespace PropGenerator.Nature.Kernel;

/// <summary>The order an <see cref="Euler"/> angle's rotations apply in. easy-props uses these three of three's six.</summary>
public enum EulerOrder
{
    /// <summary>X, then Y, then Z: three's default.</summary>
    XYZ,

    /// <summary>Y, then X, then Z.</summary>
    YXZ,

    /// <summary>Z, then Y, then X.</summary>
    ZYX,
}
