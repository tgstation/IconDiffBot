using Octokit;

namespace IconDiffBot.Core
{
	/// <summary>
	/// Constructs <see cref="IGitHubClient"/>s
	/// </summary>
    interface IGitHubClientFactory
	{
		/// <summary>
		/// Create a <see cref="GitHubApp"/> level <see cref="IGitHubClient"/>
		/// </summary>
		/// <returns>A new <see cref="IGitHubClient"/> valid for only 60s</returns>
		IGitHubClient CreateAppClient();

		/// <summary>
		/// Create a <see cref="IGitHubClient"/> based on an <paramref name="accessToken"/>
		/// </summary>
		/// <param name="accessToken">The oauth access token</param>
		/// <returns>A new <see cref="IGitHubClient"/></returns>
		IGitHubClient CreateOauthClient(string accessToken);
	}
}
