namespace IconDiffBot.Configuration
{
	/// <summary>
	/// GitHub configuration settings
	/// </summary>
	public sealed class GitHubConfiguration
	{
		/// <summary>
		/// The configuration section the <see cref="GitHubConfiguration"/> resides in
		/// </summary>
		public const string Section = "GitHub";

		/// <summary>
		/// The secret to use for hashing webhook payloads
		/// </summary>
		public string WebhookSecret { get; set; }

		/// <summary>
		/// The GitHub App PEM private key file path
		/// </summary>
		public string PemPath { get; set; }

		/// <summary>
		/// The ISS value for creating a JWT of the private key at <see cref="PemPath"/>
		/// </summary>
		public int AppID { get; set; }
	}
}
