using Hangfire;
using IconDiffBot.Configuration;
using IconDiffBot.Controllers;
using IconDiffBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
		/// The <see cref="GeneralConfiguration"/> for the <see cref="PayloadProcessor"/>
		/// </summary>
		readonly GeneralConfiguration generalConfiguration;
		/// <summary>
		/// The <see cref="IServiceProvider"/> for the <see cref="PayloadProcessor"/>
		/// </summary>
		readonly IServiceProvider serviceProvider;
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
		/// <param name="generalConfigurationOptions">The <see cref="IOptions{TOptions}"/> containing the value of <see cref="generalConfiguration"/></param>
		/// <param name="serviceProvider">The value of <see cref="serviceProvider"/></param>
		/// <param name="logger">The value of <see cref="logger"/></param>
		/// <param name="stringLocalizer">The value of <see cref="stringLocalizer"/></param>
		/// <param name="backgroundJobClient">The value of <see cref="backgroundJobClient"/></param>
		/// <param name="diffGenerator">The value of <see cref="diffGenerator"/></param>
		public PayloadProcessor(IOptions<GeneralConfiguration> generalConfigurationOptions, IServiceProvider serviceProvider, ILogger<PayloadProcessor> logger, IStringLocalizer<PayloadProcessor> stringLocalizer, IBackgroundJobClient backgroundJobClient, IDiffGenerator diffGenerator)
		{
			generalConfiguration = generalConfigurationOptions?.Value ?? throw new ArgumentNullException(nameof(generalConfigurationOptions));
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
				Name = String.Format(CultureInfo.InvariantCulture, "Diffs - Pull Request #{0}", pullRequest.Number),
				StartedAt = DateTimeOffset.Now,
				Status = CheckStatus.Queued
			};

			if (pullRequest.Head.Repository.Id == repositoryId)
				ncr.HeadBranch = pullRequest.Head.Ref;

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
				var changedDmis = allChangedFiles.Where(x => x.FileName.EndsWith(".dmi", StringComparison.InvariantCultureIgnoreCase)).ToList();
				if (changedDmis.Count == 0)
				{
					logger.LogDebug("Pull request has no changed .dmis, exiting");

					await gitHubManager.UpdateCheckRun(repositoryId, installationId, checkRunId, new CheckRunUpdate
					{
						CompletedAt = DateTimeOffset.Now,
						Status = CheckStatus.Completed,
						Conclusion = CheckConclusion.Neutral,
						Output = new CheckRunOutput(stringLocalizer["No Modified Icons"], stringLocalizer["No modified .dmi files were detected in this pull request"], null, null, null)
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
						Output = new CheckRunOutput(stringLocalizer["Error generating diffs!"], stringLocalizer["Exception details:\n\n```\n{0}\n```\n\nPlease report this [here]({1})", e.ToString(), IssueReportUrl], null, null, null)
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
		/// Generate map diffs for a given <paramref name="pullRequest"/>
		/// </summary>
		/// <param name="pullRequest">The <see cref="PullRequest"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="checkRunId">The <see cref="CheckRun.Id"/></param>
		/// <param name="changedDmis"><see cref="IReadOnlyList{T}"/> of <see cref="PullRequestFile"/>s for changed .dmis</param>
		/// <param name="scope">The <see cref="IServiceScope"/> for the operation</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		async Task GenerateDiffs(PullRequest pullRequest, long installationId, long checkRunId, IReadOnlyList<PullRequestFile> changedDmis, IServiceScope scope, CancellationToken cancellationToken)
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
				int fileIdCounter = 0;
				var finalImageDictionary = new Dictionary<string, Image>();
				async Task DiffDmi(PullRequestFile file)
				{
					async Task<MemoryStream> GetImageFor(string commit, bool before)
					{
						var data = await gitHubManager.GetFileAtCommit(pullRequest.Base.Repository.Id, installationId, before ? (file.PreviousFileName ?? file.FileName) : file.FileName, commit, cancellationToken).ConfigureAwait(false);
						return new MemoryStream(data);
					}
					Task<MemoryStream> beforeTask;
					if (file.Status == "added")
						beforeTask = Task.FromResult<MemoryStream>(null);
					else
						beforeTask = GetImageFor(pullRequest.Base.Sha, true);
					Task<MemoryStream> afterTask;
					if (file.Status == "removed")
						afterTask = Task.FromResult<MemoryStream>(null);
					else
						afterTask = GetImageFor(pullRequest.Head.Sha, false);

					using (var after = await afterTask.ConfigureAwait(false))
					using (var before = await beforeTask.ConfigureAwait(false))
					{
						var diffs = diffGenerator.GenerateDiffs(before, after);

						if (diffs == null)
						{
							//System.Drawing issue
							lock (results)
								results.Add(new IconDiff
								{
									DmiPath = file.FileName ?? file.PreviousFileName,
									CheckRunId = checkRunId,
									FileId = ++fileIdCounter,
									RepositoryId = pullRequest.Base.Repository.Id
								});
						}
						else
						{
							int baseFileId;
							lock (results)
							{
								baseFileId = fileIdCounter;
								fileIdCounter += diffs.Count;
							}

							//populate metadata
							lock (finalImageDictionary)
								foreach (var I in diffs)
								{
									I.CheckRunId = checkRunId;
									I.DmiPath = file.FileName ?? file.PreviousFileName;
									I.RepositoryId = pullRequest.Base.Repository.Id;
									I.FileId = ++baseFileId;
									if (I.Before != null)
										if (finalImageDictionary.TryGetValue(I.Before.Sha1, out Image cachedImage))
											I.Before = cachedImage;
										else
											finalImageDictionary.Add(I.Before.Sha1, I.Before);
									if (I.After != null)
										if (finalImageDictionary.TryGetValue(I.After.Sha1, out Image cachedImage))
											I.After = cachedImage;
										else
											finalImageDictionary.Add(I.After.Sha1, I.After);
								}

							lock (results)
								results.AddRange(diffs);
						}
					}
				};

				await Task.WhenAll(changedDmis.Select(x => DiffDmi(x))).ConfigureAwait(false);

				var databaseContext = scope.ServiceProvider.GetRequiredService<IDatabaseContext>();
				//decide whether to attach or add images
				var dbImages = await databaseContext.Images.Where(x => finalImageDictionary.Any(y => y.Key == x.Sha1)).Select(x => new Image
				{
					Id = x.Id,
					Sha1 = x.Sha1
				}).ToListAsync(cancellationToken).ConfigureAwait(false);

				foreach (var I in dbImages)
				{
					var image = finalImageDictionary[I.Sha1];
					image.Id = I.Id;
					databaseContext.Images.Attach(image);
					finalImageDictionary.Remove(I.Sha1);
				}

				foreach (var I in finalImageDictionary.Select(x => x.Value))
					databaseContext.Images.Add(I);

				await checkRunDequeueUpdate.ConfigureAwait(false);
				await HandleResults(pullRequest, installationId, checkRunId, results, scope, databaseContext, cancellationToken).ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Publish a <see cref="List{T}"/> of <paramref name="diffResults"/>s to the <paramref name="databaseContext"/> and GitHub
		/// </summary>
		/// <param name="pullRequest">The <see cref="PullRequest"/> the <paramref name="diffResults"/> are for</param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="checkRunId">The <see cref="CheckRun.Id"/></param>
		/// <param name="diffResults">The <see cref="IconDiff"/>s</param>
		/// <param name="scope">The <see cref="IServiceScope"/> for the operation</param>
		/// <param name="databaseContext">The <see cref="IDatabaseContext"/> for the operation</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		async Task HandleResults(PullRequest pullRequest, long installationId, long checkRunId, List<IconDiff> diffResults, IServiceScope scope, IDatabaseContext databaseContext, CancellationToken cancellationToken)
		{			
			logger.LogTrace("Generating check run output and preparing database query...");

			var outputImages = new List<CheckRunImage>()
			{
				Capacity = diffResults.Count
			};

			var prefix = generalConfiguration.ApplicationPrefix;
			var commentBuilder = new StringBuilder();

			//create a dic of filepath -> IconDiff
			var dic = new Dictionary<string, List<IconDiff>>();
			foreach (var I in diffResults)
			{
				if (!dic.TryGetValue(I.DmiPath, out List<IconDiff> list))
				{
					list = new List<IconDiff>();
					dic.Add(I.DmiPath, list);
				}
				list.Add(I);
				databaseContext.IconDiffs.Add(I);
			}

			foreach (var kv in dic)
			{

				commentBuilder.Append(String.Format(CultureInfo.InvariantCulture,
					"<details><summary>{0}</summary>{5}{5}{1} | {2} | {3} | {4}{5}--- | --- | --- | ---",
					kv.Key,
					stringLocalizer["State"],
					stringLocalizer["Old"],
					stringLocalizer["New"],
					stringLocalizer["Status"],
					Environment.NewLine));
				
				foreach(var I in kv.Value)
				{
					var beforeUrl = String.Concat(prefix, FilesController.RouteTo(pullRequest.Base.Repository, checkRunId, I.FileId, true, (I.Before ?? I.After)?.IsGif ?? false));
					var afterUrl = String.Concat(prefix, FilesController.RouteTo(pullRequest.Base.Repository, checkRunId, I.FileId, false, (I.After ?? I.Before)?.IsGif ?? false));

					string status;
					if (I.Before == null && I.After == null)
						status = stringLocalizer["This icon could not be renderered due to an error in System.Drawing. Please add a permalink to the .dmi that caused this [here](https://github.com/tgstation/IconDiffBot/issues/17) to help discover the reason."];
					else
						status = stringLocalizer[I.Before == null ? "Created" : I.After == null ? "Deleted" : "Modified"];

					commentBuilder.Append(String.Format(CultureInfo.InvariantCulture,
						"{0}{1} | ![]({2}) | ![]({3}) | {4}",
						Environment.NewLine,
						I.StateName,
						beforeUrl,
						afterUrl,
						status
						));
				}

				commentBuilder.Append(String.Format(CultureInfo.InvariantCulture, "{0}{0}</details>{0}{0}", Environment.NewLine));
			}

			var comment = String.Format(CultureInfo.CurrentCulture,
				"{0}{1}{1}{1}{1}<br>{1}{1}{2}",
				commentBuilder,
				Environment.NewLine,
				stringLocalizer["Please report any issues [here]({0}).", IssueReportUrl]
			);

			logger.LogTrace("Committing new IconDiffs to the database...");
			await databaseContext.Save(cancellationToken).ConfigureAwait(false);
			logger.LogTrace("Finalizing GitHub Check...");

			var ncr = new CheckRunUpdate
			{
				Status = CheckStatus.Completed,
				CompletedAt = DateTimeOffset.Now,
				Output = new CheckRunOutput(stringLocalizer["Icon Diffs"], stringLocalizer["Icons with diff:"], comment, null, outputImages),
				Conclusion = CheckConclusion.Success
			};
			await scope.ServiceProvider.GetRequiredService<IGitHubManager>().UpdateCheckRun(pullRequest.Base.Repository.Id, installationId, checkRunId, ncr, cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Creates a <see cref="CheckRun"/> for a <paramref name="checkSuiteSha"/> saying no pull requests could be associated with it
		/// </summary>
		/// <param name="repositoryId">The <see cref="Repository.Id"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="gitHubManager">The <see cref="IGitHubManager"/> for the operation</param>
		/// <param name="checkSuiteSha">The <see cref="CheckSuite.HeadSha"/></param>
		/// <param name="checkSuiteBranch">The <see cref="CheckSuite.HeadBranch"/></param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		async Task CreateUnassociatedCheck(long repositoryId, long installationId, IGitHubManager gitHubManager, string checkSuiteSha, string checkSuiteBranch, CancellationToken cancellationToken)
		{
			var now = DateTimeOffset.Now;
			var nmc = stringLocalizer["No Associated Pull Request"];
			await gitHubManager.CreateCheckRun(repositoryId, installationId, new NewCheckRun
			{
				CompletedAt = now,
				StartedAt = now,
				Conclusion = CheckConclusion.Neutral,
				HeadSha = checkSuiteSha,
				HeadBranch = checkSuiteBranch,
				Name = nmc,
				Output = new CheckRunOutput(nmc, String.Empty, null, null, null),
				Status = CheckStatus.Completed
			}, cancellationToken).ConfigureAwait(false);
		}

		/// <summary>
		/// Checks a <see cref="CheckSuite"/> for existing <see cref="CheckRun"/>s and calls <see cref="ScanPullRequest(long, int, long, IJobCancellationToken)"/> as necessary
		/// </summary>
		/// <param name="repositoryId">The <see cref="PullRequest.Base"/> <see cref="Repository.Id"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="checkSuiteId">The <see cref="CheckSuite.Id"/></param>
		/// <param name="checkSuiteSha">The <see cref="CheckSuite.HeadSha"/></param>
		/// <param name="checkSuiteBranch">The <see cref="CheckSuite.HeadBranch"/></param>
		/// <param name="jobCancellationToken">The <see cref="IJobCancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		[AutomaticRetry(Attempts = 0)]
		public async Task ScanCheckSuite(long repositoryId, long installationId, long checkSuiteId, string checkSuiteSha, string checkSuiteBranch, IJobCancellationToken jobCancellationToken)
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
					await CreateUnassociatedCheck(repositoryId, installationId, gitHubManager, checkSuiteSha, checkSuiteBranch, cancellationToken).ConfigureAwait(false);
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

		/// <inheritdoc />
		public void ProcessPayload(PullRequestEventPayload payload)
		{
			if ((payload.Action != "opened" && payload.Action != "synchronize") || payload.PullRequest.State.Value != ItemState.Open)
				return;
			backgroundJobClient.Enqueue(() => ScanPullRequest(payload.Repository.Id, payload.PullRequest.Number, payload.Installation.Id, JobCancellationToken.Null));
		}

		/// <inheritdoc />
		public void ProcessPayload(CheckSuiteEventPayload payload)
		{
			if (payload.Action != "rerequested")
				return;

			//don't rely on CheckSuite.PullRequests, it doesn't include PRs from forks.
			backgroundJobClient.Enqueue(() => ScanCheckSuite(payload.Repository.Id, payload.Installation.Id, payload.CheckSuite.Id, payload.CheckSuite.HeadSha, payload.CheckSuite.HeadBranch, JobCancellationToken.Null));
		}        

		/// <inheritdoc />
		public async Task ProcessPayload(CheckRunEventPayload payload, IGitHubManager gitHubManager, CancellationToken cancellationToken)
		{
			if (payload.Action != "rerequested")
				return;
			var prNumber = GetPullRequestNumberFromCheckRun(payload.CheckRun);
			if (prNumber.HasValue)
				backgroundJobClient.Enqueue(() => ScanPullRequest(payload.Repository.Id, prNumber.Value, payload.Installation.Id, JobCancellationToken.Null));
			else
				await CreateUnassociatedCheck(payload.Repository.Id, payload.Installation.Id, gitHubManager, payload.CheckRun.CheckSuite.HeadSha, payload.CheckRun.CheckSuite.HeadBranch, cancellationToken).ConfigureAwait(false);
		}
	}
}
