using GitHubJwt;
using IconDiffBot.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using System;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace IconDiffBot.Core
{
	/// <inheritdoc />
#pragma warning disable CA1812
	sealed class GitHubClientFactory : IGitHubClientFactory, IPrivateKeySource
#pragma warning restore CA1812
	{
		/// <summary>
		/// The user agent string to provide to various APIs
		/// </summary>
		static readonly string userAgent = String.Format(CultureInfo.InvariantCulture, "IconDiffBot-v{0}", Assembly.GetExecutingAssembly().GetName().Version);

		/// <summary>
		/// Creates a <see cref="GitHubClient"/> with the correct <see cref="ProductHeaderValue"/>
		/// </summary>
		/// <returns>A new <see cref="GitHubClient"/></returns>
		static GitHubClient CreateBareClient() => new GitHubClient(new ProductHeaderValue(userAgent));

		/// <summary>
		/// The <see cref="GitHubConfiguration"/> for the <see cref="GitHubClientFactory"/>
		/// </summary>
		readonly GitHubConfiguration gitHubConfiguration;
		/// <summary>
		/// The <see cref="ILogger"/> for the <see cref="GitHubClientFactory"/>
		/// </summary>
		readonly ILogger logger;

		/// <summary>
		/// Construct a <see cref="GitHubClientFactory"/>
		/// </summary>
		/// <param name="gitHubConfigurationOptions">The <see cref="IOptions{TOptions}"/> containing the value of <see cref="gitHubConfiguration"/></param>
		/// <param name="logger">The value of <see cref="logger"/></param>
		public GitHubClientFactory(IOptions<GitHubConfiguration> gitHubConfigurationOptions, ILogger<GitHubClientFactory> logger)
		{
			gitHubConfiguration = gitHubConfigurationOptions?.Value ?? throw new ArgumentNullException(nameof(gitHubConfigurationOptions));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		/// <inheritdoc />
		public IGitHubClient CreateAppClient()
		{
			//use app auth, max expiration time
			var jwtFac = new GitHubJwtFactory(this, new GitHubJwtFactoryOptions { AppIntegrationId = gitHubConfiguration.AppID, ExpirationSeconds = 600 });
			var jwt = jwtFac.CreateEncodedJwtToken();
			var client = CreateBareClient();
			client.Credentials = new Credentials(jwt, AuthenticationType.Bearer);
			return client;
		}

		/// <inheritdoc />
		public IGitHubClient CreateOauthClient(string accessToken)
		{
			if (accessToken == null)
				throw new ArgumentNullException(nameof(accessToken));
			var client = CreateBareClient();
			client.Credentials = new Credentials(accessToken, AuthenticationType.Oauth);
			return client;
		}

		/// <inheritdoc />
		public TextReader GetPrivateKeyReader()
		{
			logger.LogTrace("Opening private key file: {0}", gitHubConfiguration.PemPath);
			return File.OpenText(gitHubConfiguration.PemPath);
		}
	}
}
