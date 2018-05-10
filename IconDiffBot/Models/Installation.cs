using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace IconDiffBot.Models
{
	/// <summary>
	/// Represents a <see cref="Octokit.Installation"/>
	/// </summary>
	public sealed class Installation
	{
		/// <summary>
		/// Primary key for the entity
		/// </summary>
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.None)]
		public long Id { get; set; }

		/// <summary>
		/// The oauth access token for the <see cref="Installation"/>
		/// </summary>
		[Required]
		public string AccessToken { get; set; }

		/// <summary>
		/// When <see cref="AccessToken"/> expires
		/// </summary>
		[Required]
		public DateTimeOffset AccessTokenExpiry { get; set; }
	}
}
