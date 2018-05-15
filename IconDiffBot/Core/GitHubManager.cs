using IconDiffBot.Configuration;
using IconDiffBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IconDiffBot.Core
{
	/// <inheritdoc />
#pragma warning disable CA1812
	sealed class GitHubManager : IGitHubManager
#pragma warning restore CA1812
	{
		/// <summary>
		/// The <see cref="GitHubConfiguration"/> for the <see cref="GitHubManager"/>
		/// </summary>
		readonly GitHubConfiguration gitHubConfiguration;
		/// <summary>
		/// The <see cref="GitHubConfiguration"/> for the <see cref="GitHubManager"/>
		/// </summary>
		readonly IGitHubClientFactory gitHubClientFactory;
		/// <summary>
		/// The <see cref="IDatabaseContext"/> for the <see cref="GitHubManager"/>
		/// </summary>
		readonly IDatabaseContext databaseContext;
		/// <summary>
		/// The <see cref="ILogger"/> for the <see cref="GitHubManager"/>
		/// </summary>
		readonly ILogger<GitHubManager> logger;

		/// <summary>
		/// Construct a <see cref="GitHubManager"/>
		/// </summary>
		/// <param name="gitHubConfigurationOptions">The <see cref="IOptions{TOptions}"/> containing the value of <see cref="gitHubConfiguration"/></param>
		/// <param name="gitHubClientFactory">The value of <see cref="gitHubClientFactory"/></param>
		/// <param name="databaseContext">The value of <see cref="databaseContext"/></param>
		/// <param name="logger">The value of <see cref="logger"/></param>
		public GitHubManager(IOptions<GitHubConfiguration> gitHubConfigurationOptions, IGitHubClientFactory gitHubClientFactory, IDatabaseContext databaseContext, ILogger<GitHubManager> logger)
		{
			gitHubConfiguration = gitHubConfigurationOptions?.Value ?? throw new ArgumentNullException(nameof(gitHubConfigurationOptions));
			this.gitHubClientFactory = gitHubClientFactory ?? throw new ArgumentNullException(nameof(gitHubClientFactory));
			this.databaseContext = databaseContext ?? throw new ArgumentNullException(nameof(databaseContext));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
		}

		/// <summary>
		/// Create a <see cref="IGitHubClient"/> based on a <see cref="Repository.Id"/> in a <see cref="Installation"/>
		/// </summary>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in a new <see cref="IGitHubClient"/></returns>
		async Task<IGitHubClient> CreateInstallationClient(long installationId, CancellationToken cancellationToken)
		{
			var installation = await databaseContext.Installations.Where(x => x.Id == installationId).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

			if (installation != null)
			{
				if (installation.AccessTokenExpiry < DateTimeOffset.UtcNow.AddMinutes(-10))
				{
					var newToken = await gitHubClientFactory.CreateAppClient().GitHubApps.CreateInstallationToken(installation.Id).ConfigureAwait(false);
					installation.AccessToken = newToken.Token;
					installation.AccessTokenExpiry = newToken.ExpiresAt;
					await databaseContext.Save(cancellationToken).ConfigureAwait(false);
					return gitHubClientFactory.CreateOauthClient(newToken.Token);
				}
				return gitHubClientFactory.CreateOauthClient(installation.AccessToken);
			}

			//do a discovery
			var client = gitHubClientFactory.CreateAppClient();
			var newInstallation = await client.GitHubApps.GetInstallation(installationId).ConfigureAwait(false);
			var installationToken = await client.GitHubApps.CreateInstallationToken(newInstallation.Id).ConfigureAwait(false);
			var entity = new Models.Installation
			{
				Id = newInstallation.Id,
				AccessToken = installationToken.Token,
				AccessTokenExpiry = installationToken.ExpiresAt
			};
			databaseContext.Installations.Add(entity);
			await databaseContext.Save(cancellationToken).ConfigureAwait(false);
			//its either in newEntities now or it doesn't exist
			return gitHubClientFactory.CreateOauthClient(entity.AccessToken);
		}

		/// <inheritdoc />
		public async Task<IReadOnlyList<PullRequestFile>> GetPullRequestChangedFiles(PullRequest pullRequest, long installationId, CancellationToken cancellationToken)
		{
			if (pullRequest == null)
				throw new ArgumentNullException(nameof(pullRequest));
			logger.LogTrace("Get changed files for {0}/{1} #{2}", pullRequest.Base.Repository.Owner.Login, pullRequest.Base.Repository.Name, pullRequest.Number);
			var gitHubClient = await CreateInstallationClient(installationId, cancellationToken).ConfigureAwait(false);
			return await gitHubClient.PullRequest.Files(pullRequest.Base.Repository.Id, pullRequest.Number).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<PullRequest> GetPullRequest(long repositoryId, long installationId, int pullRequestNumber, CancellationToken cancellationToken)
		{
			logger.LogTrace("Get pull request #{0} on repository {1}", pullRequestNumber, repositoryId);
			var client = await CreateInstallationClient(installationId, cancellationToken).ConfigureAwait(false);
			return await client.PullRequest.Get(repositoryId, pullRequestNumber).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task UpdateCheckRun(long repositoryId, long installationId, long checkRunId, CheckRunUpdate checkRunUpdate, CancellationToken cancellationToken)
		{
			logger.LogTrace("Update check run {0} on repository {1}", checkRunId, repositoryId);
			var client = await CreateInstallationClient(installationId, cancellationToken).ConfigureAwait(false);
			await client.Check.Run.Update(repositoryId, checkRunId, checkRunUpdate).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public async Task<long> CreateCheckRun(long repositoryId, long installationId, NewCheckRun initializer, CancellationToken cancellationToken)
		{
			logger.LogTrace("Create check run for ref {0} on repository {1}", initializer.HeadSha, repositoryId);
			var client = await CreateInstallationClient(installationId, cancellationToken).ConfigureAwait(false);
			var checkRun = await client.Check.Run.Create(repositoryId, initializer).ConfigureAwait(false);
			return checkRun.Id;
		}

		/// <inheritdoc />
		public async Task<byte[]> GetFileAtCommit(long repositoryId, long installationId, string filePath, string commit, CancellationToken cancellationToken)
		{
			if (String.IsNullOrWhiteSpace(filePath))
				throw new ArgumentNullException(nameof(filePath));
			if (String.IsNullOrWhiteSpace(commit))
				throw new ArgumentNullException(nameof(commit));
			logger.LogTrace("Get file for ref {0} on repository {1}", commit, repositoryId);
			var client = await CreateInstallationClient(installationId, cancellationToken).ConfigureAwait(false);
			var files = await client.Repository.Content.GetAllContentsByRef(repositoryId, filePath, commit).ConfigureAwait(false);
			return Convert.FromBase64String(files[0].EncodedContent);
		}

		public async Task<IEnumerable<CheckRun>> GetMatchingCheckRuns(long repositoryId, long installationId, long checkSuiteId, CancellationToken cancellationToken)
		{
			var client = await CreateInstallationClient(installationId, cancellationToken).ConfigureAwait(false);
			return await client.Check.Run.GetAllForCheckSuite(repositoryId, checkSuiteId).ConfigureAwait(false);
		}
	}
}
