using System.Collections.Generic;

namespace IconDiffBot.Configuration
{
	/// <summary>
	/// General configuration settings
	/// </summary>
	public sealed class GeneralConfiguration
	{
		/// <summary>
		/// The configuration section the <see cref="GeneralConfiguration"/> resides in
		/// </summary>
		public const string Section = "General";

		/// <summary>
		/// The public URL for the application
		/// </summary>
		public string ApplicationPrefix { get; set; }

		/// <summary>
		/// A list of blacklisted repos
		/// </summary>
#pragma warning disable CA2227 // Collection properties should be read only
		public List<long> BlacklistedRepos { get; set; }
#pragma warning restore CA2227 // Collection properties should be read only
	}
}
