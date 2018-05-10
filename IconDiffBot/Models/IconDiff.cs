using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IconDiffBot.Models
{
	/// <summary>
	/// Represents a icon diff set
	/// </summary>
    public sealed class IconDiff
	{
		/// <summary>
		/// The <see cref="Octokit.Repository.Id"/>
		/// </summary>
		[Key, Column(Order = 0)]
		[Required]
		public long RepositoryId { get; set; }

		/// <summary>
		/// The <see cref="Octokit.CheckRun.Id"/>
		/// </summary>
		[Key, Column(Order = 1)]
		[Required]
		public long CheckRunId { get; set; }

		/// <summary>
		/// The id of the diffed file
		/// </summary>
		[Key, Column(Order = 2)]
		[Required]
		public int FileId { get; set; }

		/// <summary>
		/// The path of the diffed files
		/// </summary>
		[Required]
		public string DmiPath { get; set; }

		/// <summary>
		/// The before image
		/// </summary>
#pragma warning disable CA1819 // Properties should not return arrays
		public byte[] BeforeImage { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays

		/// <summary>
		/// The after image
		/// </summary>
#pragma warning disable CA1819 // Properties should not return arrays
		public byte[] AfterImage { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays
	}
}
