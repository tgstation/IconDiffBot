using IconDiffBot.Models;
using System.Collections.Generic;

using Stream = System.IO.Stream;

namespace IconDiffBot.Core
{
	/// <summary>
	/// For generating <see cref="IconDiff"/>s from <see cref="Stream"/>s
	/// </summary>
	interface IDiffGenerator
	{
		/// <summary>
		/// Generate a set of <see cref="IconDiff"/>s
		/// </summary>
		/// <param name="before">The before <see cref="Stream"/></param>
		/// <param name="after">The after <see cref="Stream"/></param>
		/// <returns>A <see cref="List{T}"/> of <see cref="IconDiff"/>s with only the data fields populated</returns>
		List<IconDiff> GenerateDiffs(Stream before, Stream after);
	}
}