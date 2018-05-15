using MetadataExtractor;
using IconDiffBot.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Stream = System.IO.Stream;
using System.Linq;
using System;

namespace IconDiffBot.Core
{
	/// <inheritdoc />
	sealed class DiffGenerator : IDiffGenerator
	{
		static string StreamToMetadata(Stream stream)
		{
			var metadata = ImageMetadataReader.ReadMetadata(stream);
			const string DmiHeader = "# BEGIN DMI";
			var description = metadata.SelectMany(x => x.Tags).First(x => x.Description.Contains(DmiHeader)).Description;
			var startIndex = description.IndexOf(DmiHeader, StringComparison.InvariantCulture) + DmiHeader.Length;
			var length = description.IndexOf("# END DMI", StringComparison.InvariantCulture) - startIndex;
			return description.Substring(startIndex, length);
		}

		/// <inheritdoc />
		public Task<IReadOnlyList<IconDiff>> GenerateDiffs(Stream before, Stream after, CancellationToken cancellationToken)
		{
			var beforeDmi = StreamToMetadata(before);
			var afterDmi = StreamToMetadata(after);


			throw new NotImplementedException();
		}
	}
}
