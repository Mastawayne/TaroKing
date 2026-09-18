using System.Numerics;

namespace TaroKing.Engine.Randomness;

/// <summary>
/// A small PCG32 generator. The engine uses this instead of <see cref="System.Random"/> so that a seed
/// means the same hand on every machine and every runtime version, today and after a framework upgrade —
/// which is what makes saved games and replays reproducible.
/// </summary>
public sealed class Pcg32 {

	private const ulong Multiplier = 6364136223846793005UL;

	private readonly ulong _increment;
	private ulong _state;

	public Pcg32(ulong seed, ulong sequence = 0xDA3E39CB94B95BDBUL) {
		_increment = (sequence << 1) | 1UL;
		_state = 0UL;
		NextUInt32();
		_state = unchecked(_state + seed);
		NextUInt32();
	}

	public Pcg32(int seed) : this(unchecked((ulong)seed)) {
	}

	/// <summary>The next 32 random bits.</summary>
	public uint NextUInt32() {
		ulong previous = _state;
		_state = unchecked((previous * Multiplier) + _increment);

		uint xorshifted = (uint)(((previous >> 18) ^ previous) >> 27);
		int rotation = (int)(previous >> 59);
		return BitOperations.RotateRight(xorshifted, rotation);
	}

	/// <summary>A value in [0, exclusiveUpperBound), without modulo bias.</summary>
	public int Next(int exclusiveUpperBound) {
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveUpperBound);

		uint bound = (uint)exclusiveUpperBound;
		uint threshold = (uint)((0x1_0000_0000UL - bound) % bound);

		while (true) {
			uint value = NextUInt32();
			if (value >= threshold) {
				return (int)(value % bound);
			}
		}
	}

	/// <summary>Fisher-Yates, in place.</summary>
	public void Shuffle<T>(IList<T> items) {
		ArgumentNullException.ThrowIfNull(items);

		for (int i = items.Count - 1; i > 0; i--) {
			int j = Next(i + 1);
			(items[i], items[j]) = (items[j], items[i]);
		}
	}
}
