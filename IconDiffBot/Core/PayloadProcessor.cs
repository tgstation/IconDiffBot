using Hangfire;
using IconDiffBot.Configuration;
using IconDiffBot.Controllers;
using IconDiffBot.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using MemoryStream = System.IO.MemoryStream;

namespace IconDiffBot.Core
{
	/// <inheritdoc />
#pragma warning disable CA1812
	sealed class PayloadProcessor : IPayloadProcessor
#pragma warning restore CA1812
	{
		/// <summary>
		/// The URL to direct user to report issues at
		/// </summary>
		const string IssueReportUrl = "https://github.com/tgstation/IconDiffBot/issues";
		/// <summary>
		/// The intermediate directory for operations
		/// </summary>
		public const string WorkingDirectory = "MapDiffs";

		/// <summary>
		/// The <see cref="GitHubConfiguration"/> for the <see cref="PayloadProcessor"/>
		/// </summary>
		readonly GitHubConfiguration gitHubConfiguration;
		/// <summary>
		/// The <see cref="IGitHubManager"/> for the <see cref="PayloadProcessor"/>
		/// </summary>
		readonly IServiceProvider serviceProvider;
		/// <summary>
		/// The <see cref="IIOManager"/> for the <see cref="PayloadProcessor"/>
		/// </summary>
		readonly IIOManager ioManager;
		/// <summary>
		/// The <see cref="ILogger"/> for the <see cref="PayloadProcessor"/>
		/// </summary>
		readonly ILogger<PayloadProcessor> logger;
		/// <summary>
		/// The <see cref="ILogger"/> for the <see cref="PayloadProcessor"/>
		/// </summary>
		readonly IStringLocalizer<PayloadProcessor> stringLocalizer;
		/// <summary>
		/// The <see cref="IBackgroundJobClient"/> for the <see cref="PayloadProcessor"/>
		/// </summary>
		readonly IBackgroundJobClient backgroundJobClient;
		/// <summary>
		/// The <see cref="IDiffGenerator"/> for the <see cref="PayloadProcessor"/>
		/// </summary>
		readonly IDiffGenerator diffGenerator;

		/// <summary>
		/// Construct a <see cref="PayloadProcessor"/>
		/// </summary>
		/// <param name="gitHubConfigurationOptions">The <see cref="IOptions{TOptions}"/> containing the value of <see cref="gitHubConfiguration"/></param>
		/// <param name="serviceProvider">The value of <see cref="serviceProvider"/></param>
		/// <param name="ioManager">The value of <see cref="ioManager"/></param>
		/// <param name="logger">The value of <see cref="logger"/></param>
		/// <param name="stringLocalizer">The value of <see cref="stringLocalizer"/></param>
		/// <param name="backgroundJobClient">The value of <see cref="backgroundJobClient"/></param>
		/// <param name="diffGenerator">The value of <see cref="diffGenerator"/></param>
		public PayloadProcessor(IOptions<GitHubConfiguration> gitHubConfigurationOptions, IServiceProvider serviceProvider, IIOManager ioManager, ILogger<PayloadProcessor> logger, IStringLocalizer<PayloadProcessor> stringLocalizer, IBackgroundJobClient backgroundJobClient, IDiffGenerator diffGenerator)
		{
			gitHubConfiguration = gitHubConfigurationOptions?.Value ?? throw new ArgumentNullException(nameof(gitHubConfigurationOptions));
			this.ioManager = new ResolvingIOManager(ioManager ?? throw new ArgumentNullException(nameof(ioManager)), WorkingDirectory);
			this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.stringLocalizer = stringLocalizer ?? throw new ArgumentNullException(nameof(stringLocalizer));
			this.backgroundJobClient = backgroundJobClient ?? throw new ArgumentNullException(nameof(backgroundJobClient));
			this.diffGenerator = diffGenerator ?? throw new ArgumentNullException(nameof(diffGenerator));
		}

		/// <summary>
		/// Identifies the <see cref="PullRequest.Number"/> a <see cref="CheckRun"/> originated from
		/// </summary>
		/// <param name="checkRun">The <see cref="CheckRun"/> to test</param>
		/// <returns>The associated <see cref="PullRequest.Number"/> on success, <see langword="null"/> on failure</returns>
		static int? GetPullRequestNumberFromCheckRun(CheckRun checkRun)
		{
			//nice thing about check runs we know they contain our pull request number in the title
			var prRegex = Regex.Match(checkRun.Name, "#([1-9][0-9]*)");
			if (prRegex.Success)
				return Convert.ToInt32(prRegex.Groups[1].Value, CultureInfo.InvariantCulture);
			return null;
		}

		/// <summary>
		/// Generates a icon diff for the specified <see cref="PullRequest"/>
		/// </summary>
		/// <param name="repositoryId">The <see cref="PullRequest.Base"/> <see cref="Repository.Id"/></param>
		/// <param name="pullRequestNumber">The <see cref="PullRequest.Number"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="scope">The <see cref="IServiceScope"/> for the operation</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		async Task ScanPullRequestImpl(long repositoryId, int pullRequestNumber, long installationId, IServiceScope scope, CancellationToken cancellationToken)
		{
			var gitHubManager = scope.ServiceProvider.GetRequiredService<IGitHubManager>();
			var pullRequest = await gitHubManager.GetPullRequest(repositoryId, installationId, pullRequestNumber, cancellationToken).ConfigureAwait(false);

			logger.LogTrace("Repository is {0}/{1}", pullRequest.Base.Repository.Owner.Login, pullRequest.Base.Repository.Name);
			logger.LogTrace("Pull Request: \"{0}\" by {1}", pullRequest.Title, pullRequest.User.Login);

			var allChangedFilesTask = gitHubManager.GetPullRequestChangedFiles(pullRequest, installationId, cancellationToken);
			var requestIdentifier = String.Concat(pullRequest.Base.Repository.Owner.Login, pullRequest.Base.Repository.Name, pullRequest.Number);

			var ncr = new NewCheckRun
			{
				HeadSha = pullRequest.Head.Sha,
				Name = String.Format(CultureInfo.InvariantCulture, "Renderings - Pull Request #{0}", pullRequest.Number),
				StartedAt = DateTimeOffset.Now,
				Status = CheckStatus.Queued
			};
			var checkRunId = await gitHubManager.CreateCheckRun(repositoryId, installationId, ncr, cancellationToken).ConfigureAwait(false);

			Task HandleCancel() => gitHubManager.UpdateCheckRun(repositoryId, installationId, checkRunId, new CheckRunUpdate
			{
				CompletedAt = DateTimeOffset.Now,
				Status = CheckStatus.Completed,
				Conclusion = CheckConclusion.Neutral,
				Output = new CheckRunOutput(stringLocalizer["Operation Cancelled"], stringLocalizer["The operation was cancelled on the server, most likely due to app shutdown. You may attempt re-running it."], null, null, null)
			}, default);

			try
			{
				var allChangedFiles = await allChangedFilesTask.ConfigureAwait(false);
				var changedDmis = allChangedFiles.Where(x => x.FileName.EndsWith(".dmi", StringComparison.InvariantCultureIgnoreCase)).Select(x => x.FileName).ToList();
				if (changedDmis.Count == 0)
				{
					logger.LogDebug("Pull request has no changed .dmis, exiting");

					await gitHubManager.UpdateCheckRun(repositoryId, installationId, checkRunId, new CheckRunUpdate
					{
						CompletedAt = DateTimeOffset.Now,
						Status = CheckStatus.Completed,
						Conclusion = CheckConclusion.Neutral,
						Output = new CheckRunOutput(stringLocalizer["No Modified Icons"], stringLocalizer["No modified .dnu files were detected in this pull request"], null, null, null)
					}, cancellationToken).ConfigureAwait(false);
					return;
				}

				logger.LogTrace("Pull request has icon changes, creating check run");

				await GenerateDiffs(pullRequest, installationId, checkRunId, changedDmis, scope, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				logger.LogTrace("Operation cancelled");

				await HandleCancel().ConfigureAwait(false);
			}
			catch (Exception e)
			{
				logger.LogDebug(e, "Error occurred. Attempting to post debug comment");
				try
				{
					await gitHubManager.UpdateCheckRun(repositoryId, installationId, checkRunId, new CheckRunUpdate
					{
						CompletedAt = DateTimeOffset.Now,
						Status = CheckStatus.Completed,
						Conclusion = CheckConclusion.Failure,
						Output = new CheckRunOutput(stringLocalizer["Error rendering maps!"], stringLocalizer["Exception details:\n\n```\n{0}\n```\n\nPlease report this [here]({1})", e.ToString(), IssueReportUrl], null, null, null)
					}, default).ConfigureAwait(false);
					throw;
				}
				catch (OperationCanceledException)
				{
					logger.LogTrace("Operation cancelled");
					await HandleCancel().ConfigureAwait(false);
				}
				catch (Exception innerException)
				{
					throw new AggregateException(innerException, e);
				}
			}
		}

		/// <summary>
		/// Checks a <see cref="CheckSuite"/> for existing <see cref="CheckRun"/>s and calls <see cref="ScanPullRequest(long, int, long, IJobCancellationToken)"/> as necessary
		/// </summary>
		/// <param name="repositoryId">The <see cref="PullRequest.Base"/> <see cref="Repository.Id"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="checkSuiteId">The <see cref="CheckSuite.Id"/></param>
		/// <param name="checkSuiteSha">The <see cref="CheckSuite.HeadSha"/></param>
		/// <param name="jobCancellationToken">The <see cref="IJobCancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		[AutomaticRetry(Attempts = 0)]
		public async Task ScanCheckSuite(long repositoryId, long installationId, long checkSuiteId, string checkSuiteSha, IJobCancellationToken jobCancellationToken)
		{
			using (logger.BeginScope("Scanning check suite {0} for repository {1}. Sha: ", checkSuiteId, repositoryId, checkSuiteSha))
			using (var scope = serviceProvider.CreateScope())
			{
				var gitHubManager = scope.ServiceProvider.GetRequiredService<IGitHubManager>();
				var cancellationToken = jobCancellationToken.ShutdownToken;
				var checkRuns = await gitHubManager.GetMatchingCheckRuns(repositoryId, installationId, checkSuiteId, cancellationToken).ConfigureAwait(false);
				bool testedAny = false;

				await Task.WhenAll(checkRuns.Select(x =>
				{
					var result = GetPullRequestNumberFromCheckRun(x);
					if (result.HasValue)
					{
						testedAny = true;
						return ScanPullRequestImpl(repositoryId, result.Value, installationId, scope, cancellationToken);
					}
					return Task.CompletedTask;
				})).ConfigureAwait(false);

				if (!testedAny)
				{
					var now = DateTimeOffset.Now;
					var nmc = stringLocalizer["No Known Associated Pull Request"];
					await gitHubManager.CreateCheckRun(repositoryId, installationId, new NewCheckRun
					{
						CompletedAt = now,
						StartedAt = now,
						Conclusion = CheckConclusion.Neutral,
						HeadSha = checkSuiteSha,
						Name = nmc,
						Output = new CheckRunOutput(nmc, stringLocalizer["No pull requests could be associated with this check suite"], null, null, null),
						Status = CheckStatus.Completed
					}, cancellationToken).ConfigureAwait(false);
				}
			}
		}

		/// <summary>
		/// Generates a icon diff for the specified <see cref="PullRequest"/>
		/// </summary>
		/// <param name="repositoryId">The <see cref="PullRequest.Base"/> <see cref="Repository.Id"/></param>
		/// <param name="pullRequestNumber">The <see cref="PullRequest.Number"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="jobCancellationToken">The <see cref="IJobCancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		[AutomaticRetry(Attempts = 0)]
		public async Task ScanPullRequest(long repositoryId, int pullRequestNumber, long installationId, IJobCancellationToken jobCancellationToken)
		{
			using (logger.BeginScope("Scanning pull request #{0} for repository {1}", pullRequestNumber, repositoryId))
			using (var scope = serviceProvider.CreateScope())
				await ScanPullRequestImpl(repositoryId, pullRequestNumber, installationId, scope, jobCancellationToken.ShutdownToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Generate map diffs for a given <paramref name="pullRequest"/>
		/// </summary>
		/// <param name="pullRequest">The <see cref="PullRequest"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="checkRunId">The <see cref="CheckRun.Id"/></param>
		/// <param name="changedDmis">Paths to changed .dmm files</param>
		/// <param name="scope">The <see cref="IServiceScope"/> for the operation</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		async Task GenerateDiffs(PullRequest pullRequest, long installationId, long checkRunId, IReadOnlyList<string> changedDmis, IServiceScope scope, CancellationToken cancellationToken)
		{
			using (logger.BeginScope("Generating {0} diffs for pull request #{1} in {2}/{3}", changedDmis.Count, pullRequest.Number, pullRequest.Base.Repository.Owner.Login, pullRequest.Base.Repository.Name))
			{
				var gitHubManager = scope.ServiceProvider.GetRequiredService<IGitHubManager>();
				var checkRunDequeueUpdate = gitHubManager.UpdateCheckRun(pullRequest.Base.Repository.Id, installationId, checkRunId, new CheckRunUpdate
				{
					Status = CheckStatus.InProgress,
					Output = new CheckRunOutput(stringLocalizer["Generating Diffs"], stringLocalizer["Aww geez rick, I should eventually put some progress message here"], null, null, null),
				}, cancellationToken);

				var results = new List<IconDiff>();

				async Task DiffDmi(string path)
				{
					async Task<Bitmap> GetImageFor(string commit)
					{
						var data = await gitHubManager.GetFileAtCommit(pullRequest.Base.Repository.Id, installationId, path, commit, cancellationToken).ConfigureAwait(false);
						return new Bitmap(new MemoryStream(data));
					}

					var beforeTask = GetImageFor(pullRequest.Base.Sha);
					using (var after = await GetImageFor(pullRequest.Head.Sha).ConfigureAwait(false))
					using (var before = await beforeTask.ConfigureAwait(false))
					{
						var diffs = await diffGenerator.GenerateDiffs(before, after, cancellationToken).ConfigureAwait(false);
						lock (results) {
							var baseCount = results.Count;
							results.AddRange(diffs.Select(x =>
							{
								x.CheckRunId = checkRunId;
								x.DmiPath = path;
								x.FileId = ++baseCount;
								x.RepositoryId = pullRequest.Base.Repository.Id;
								return x;
							}));
						}
					}
				};

				await Task.WhenAll(changedDmis.Select(x => DiffDmi(x))).ConfigureAwait(false);

				await checkRunDequeueUpdate.ConfigureAwait(false);
				await HandleResults(pullRequest, installationId, checkRunId, results, scope, cancellationToken).ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Publish a <see cref="List{T}"/> of <paramref name="diffResults"/>s to the <see cref="IDatabaseContext"/> and GitHub
		/// </summary>
		/// <param name="pullRequest">The <see cref="PullRequest"/> the <paramref name="diffResults"/> are for</param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="checkRunId">The <see cref="CheckRun.Id"/></param>
		/// <param name="diffResults">The <see cref="IconDiff"/>s</param>
		/// <param name="scope">The <see cref="IServiceScope"/> for the operation</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		async Task HandleResults(PullRequest pullRequest, long installationId, long checkRunId, List<IconDiff> diffResults, IServiceScope scope, CancellationToken cancellationToken)
		{
			int formatterCount = 0;

			var databaseContext = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
			logger.LogTrace("Generating check run output and preparing database query...");

			var outputImages = new List<CheckRunImage>()
			{
				Capacity = diffResults.Count
			};

			var prefix = gitHubConfiguration.WebhookBaseUrl.ToString();

			foreach (var kv in diffResults)
			{
				var beforeUrl = String.Concat(prefix, FilesController.RouteTo(pullRequest.Base.Repository, checkRunId, formatterCount, "before"));
				var afterUrl = String.Concat(prefix, FilesController.RouteTo(pullRequest.Base.Repository, checkRunId, formatterCount, "after"));

				logger.LogTrace("Adding IconDiff for {0}...", kv.DmiPath);
				outputImages.Add(new CheckRunImage(null, beforeUrl, stringLocalizer["Old - {0}", kv.DmiPath]));
				outputImages.Add(new CheckRunImage(null, afterUrl, stringLocalizer["New - {0}", kv.DmiPath]));
				databaseContext.IconDiffs.Add(kv);
				++formatterCount;
			}
			
			logger.LogTrace("Committing new IconDiffs to the database...");
			await databaseContext.Save(cancellationToken).ConfigureAwait(false);
			logger.LogTrace("Finalizing GitHub Check...");

			var ncr = new CheckRunUpdate
			{
				Status = CheckStatus.Completed,
				CompletedAt = DateTimeOffset.Now,
				Output = new CheckRunOutput(stringLocalizer["Icon Diffs"], String.Empty, null, null, outputImages),
				Conclusion = CheckConclusion.Success
			};
			await scope.ServiceProvider.GetRequiredService<IGitHubManager>().UpdateCheckRun(pullRequest.Base.Repository.Id, installationId, checkRunId, ncr, cancellationToken).ConfigureAwait(false);
		}

		/// <inheritdoc />
		public void ProcessPayload(PullRequestEventPayload payload)
		{
			if ((payload.Action != "opened" && payload.Action != "synchronize") || payload.PullRequest.State.Value != ItemState.Open || payload.PullRequest.Base.Repository.Id == payload.PullRequest.Head.Repository.Id)
				return;
			backgroundJobClient.Enqueue(() => ScanPullRequest(payload.Repository.Id, payload.PullRequest.Number, payload.Installation.Id, JobCancellationToken.Null));
		}

		/// <inheritdoc />
		public void ProcessPayload(CheckSuiteEventPayload payload)
		{
			if (payload.Action != "requested" && payload.Action != "rerequested")
				return;

			//don't rely on CheckSuite.PullRequests, it doesn't include PRs from forks.
			backgroundJobClient.Enqueue(() => ScanCheckSuite(payload.Repository.Id, payload.Installation.Id, payload.CheckSuite.Id, payload.CheckSuite.HeadSha, JobCancellationToken.Null));

			if (payload.CheckSuite.PullRequests.Any())
				foreach (var I in payload.CheckSuite.PullRequests)
					backgroundJobClient.Enqueue(() => ScanPullRequest(payload.Repository.Id, I.Number, payload.Installation.Id, JobCancellationToken.Null));
		}        

		/// <inheritdoc />
		public async Task ProcessPayload(CheckRunEventPayload payload, IGitHubManager gitHubManager, CancellationToken cancellationToken)
		{
			if (payload.Action != "rerequested")
				return;
			var prNumber = GetPullRequestNumberFromCheckRun(payload.CheckRun);
			if(prNumber.HasValue)
				backgroundJobClient.Enqueue(() => ScanPullRequest(payload.Repository.Id, prNumber.Value, payload.Installation.Id, JobCancellationToken.Null));
			else
			{
				var now = DateTimeOffset.Now;
				var nmc = stringLocalizer["No Associated Pull Request"];
				await gitHubManager.CreateCheckRun(payload.Repository.Id, payload.Installation.Id, new NewCheckRun
				{
					CompletedAt = now,
					StartedAt = now,
					Conclusion = CheckConclusion.Neutral,
					HeadBranch = payload.CheckRun.CheckSuite.HeadBranch,
					HeadSha = payload.CheckRun.CheckSuite.HeadSha,
					Name = nmc,
					Output = new CheckRunOutput(nmc, String.Empty, null, null, null),
					Status = CheckStatus.Completed
				}, cancellationToken).ConfigureAwait(false);
			}
		}
	}
}
