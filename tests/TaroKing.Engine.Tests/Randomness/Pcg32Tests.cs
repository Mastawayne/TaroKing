using TaroKing.Engine.Randomness;

namespace TaroKing.Engine.Tests.Randomness;

public class Pcg32Tests {

	[Fact]
	public void The_same_seed_produces_the_same_sequence() {
		Pcg32 first = new(seed: 42);
		Pcg32 second = new(seed: 42);

		for (int i = 0; i < 100; i++) {
			Assert.Equal(first.NextUInt32(), second.NextUInt32());
		}
	}

	[Fact]
	public void Different_seeds_diverge() {
		Pcg32 first = new(seed: 1);
		Pcg32 second = new(seed: 2);

		bool differs = false;
		for (int i = 0; i < 10 && !differs; i++) {
			differs = first.NextUInt32() != second.NextUInt32();
		}

		Assert.True(differs);
	}

	[Fact]
	public void Bounded_values_stay_in_range_and_cover_it() {
		Pcg32 random = new(seed: 7);
		int[] seen = new int[6];

		for (int i = 0; i < 6000; i++) {
			int value = random.Next(6);
			Assert.InRange(value, 0, 5);
			seen[value]++;
		}

		Assert.DoesNotContain(seen, count => count == 0);
	}

	[Fact]
	public void A_bound_must_be_positive() {
		Pcg32 random = new(seed: 1);

		Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(0));
		Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(-3));
	}

	[Fact]
	public void Shuffling_is_a_permutation() {
		List<int> items = [.. Enumerable.Range(0, 54)];
		new Pcg32(seed: 3).Shuffle(items);

		Assert.Equal(54, items.Count);
		Assert.Equal(Enumerable.Range(0, 54), items.OrderBy(value => value));
		Assert.NotEqual(Enumerable.Range(0, 54), items);
	}
}
