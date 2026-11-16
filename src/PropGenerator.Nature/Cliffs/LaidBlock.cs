namespace PropGenerator.Nature.Cliffs;

/// <summary>One block of a laid-out cliff (see <see cref="CliffLayout"/>): its solid, the shape it was made from, and where it sits.</summary>
/// <param name="Block">The solid as laid: the <see cref="BlockMaker"/> block, turned if it is a plate, with its ends finished.</param>
/// <param name="Shape">The shape the block was made from, with the sink it was settled to.</param>
/// <param name="Column">Which block along the width it is, from 0 at the -x end.</param>
/// <param name="Tier">Which level it stands on, from 0 on the ground.</param>
/// <param name="Layer">Which plate of its column and tier it is, from 0 at the bottom; 0 when the block is not cut into plates.</param>
internal sealed record LaidBlock(ConvexBlock Block, BlockShape Shape, int Column, int Tier, int Layer);
