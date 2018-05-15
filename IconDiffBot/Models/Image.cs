using System.ComponentModel.DataAnnotations;

namespace IconDiffBot.Models
{
	/// <summary>
	/// Represents binary image data
	/// </summary>
	public sealed class Image
	{
		/// <summary>
		/// The column Id
		/// </summary>
		public long Id { get; set; }

		/// <summary>
		/// The <see cref="Sha1"/> of <see cref="Data"/>
		/// </summary>
		[Required, StringLength(40, MinimumLength = 40)]
		public string Sha1 { get; set; }

		/// <summary>
		/// The binary image data
		/// </summary>
		[Required]
#pragma warning disable CA1819 // Properties should not return arrays
		public byte[] Data { get; set; }
#pragma warning restore CA1819 // Properties should not return arrays
	}
}