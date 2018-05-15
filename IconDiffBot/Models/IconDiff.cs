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
		/// The <see cref="IconState.Name"/>
		/// </summary>
		[Required]
		public string StateName { get; set; }


		/// <summary>
		/// The <see cref="Before"/> <see cref="Image.Id"/>
		/// </summary>
		public long BeforeId { get; set; }

		/// <summary>
		/// The before <see cref="Image"/>
		/// </summary>
		public Image Before { get; set; }

		/// <summary>
		/// The <see cref="After"/> <see cref="Image.Id"/>
		/// </summary>
		public long AfterId { get; set; }

		/// <summary>
		/// The after <see cref="Image"/>
		/// </summary>
		public Image After { get; set; }
	}
}
