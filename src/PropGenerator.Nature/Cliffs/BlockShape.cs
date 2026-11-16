namespace PropGenerator.Nature.Cliffs;

/// <summary>
/// What one block of a slab cliff is like: its size, how its sides are cut and tilted, and which cuts it carries. Cliff space is
/// Z-up; the block's front faces -y and its width runs along x. <see cref="BlockMaker"/> turns it into a solid.
/// </summary>
/// <param name="Base">The footprint's centre at the block's ground line, in metres.</param>
/// <param name="Width">The footprint's size along x, in metres.</param>
/// <param name="Depth">The footprint's size along y, front to back, in metres.</param>
/// <param name="Height">How far the top stands above the ground line at the centre, in metres.</param>
/// <param name="Sink">How far the flat bottom lies below the ground line, in metres.</param>
/// <param name="Faces">How many side planes surround the footprint (4 or more).</param>
/// <param name="Roughness">The most a side plane is set in, in metres.</param>
/// <param name="Lean">How far the front faces batter back at the top, in degrees; negative overhangs.</param>
/// <param name="TopTilt">How far the top tilts from level, in degrees, in a seeded direction.</param>
/// <param name="Bevel">How far the edge chamfers cut in, in metres; 0 means none.</param>
/// <param name="Steps">How many step cuts lower part of the top.</param>
/// <param name="Bites">How many bites are taken out of the corners.</param>
/// <param name="Grooves">How many grooves lower part of one face behind an upright step plane.</param>
/// <param name="CutDepth">How deep cuts go, 0 to 1 of the most allowed (40% of the block's local width).</param>
/// <param name="TurnLeft">How far the -x end face turns back toward the front, cutting the front end corner, in degrees; 0 means no end turn.</param>
/// <param name="TurnRight">How far the +x end face turns back toward the front, cutting the front end corner, in degrees; 0 means no end turn.</param>
/// <param name="MinThickness">The least width the block may have along any of its plane normals, in metres.</param>
public sealed record BlockShape(
    V3 Base,
    double Width,
    double Depth,
    double Height,
    double Sink,
    int Faces,
    double Roughness,
    double Lean,
    double TopTilt,
    double Bevel,
    int Steps,
    int Bites,
    int Grooves,
    double CutDepth,
    double TurnLeft,
    double TurnRight,
    double MinThickness);
