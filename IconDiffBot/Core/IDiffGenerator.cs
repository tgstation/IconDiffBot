using IconDiffBot.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in a <see cref="IReadOnlyList{T}"/> of <see cref="IconDiff"/>s with only the data fields populated</returns>
		Task<IReadOnlyList<IconDiff>> GenerateDiffs(Stream before, Stream after, CancellationToken cancellationToken);
	}
}