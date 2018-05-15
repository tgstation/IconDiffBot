using IconDiffBot.Models;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace IconDiffBot.Core
{
	/// <inheritdoc />
	sealed class DiffGenerator : IDiffGenerator
	{
		/// <inheritdoc />
		public Task<IReadOnlyList<IconDiff>> GenerateDiffs(Bitmap before, Bitmap after, CancellationToken cancellationToken)
		{
			throw new System.NotImplementedException();
		}
	}
}
