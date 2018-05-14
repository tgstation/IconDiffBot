using Octokit;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IconDiffBot.Core
{
	/// <summary>
	/// Manages operations with GitHub.com
	/// </summary>
	public interface IGitHubManager
	{
		/// <summary>
		/// Gets a <see cref="PullRequest"/>
		/// </summary>
		/// <param name="repositoryId">The <see cref="Repository.Id"/> of the <see cref="PullRequest.Base"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="pullRequestNumber">The <see cref="PullRequest.Number"/></param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in a <see cref="PullRequest"/></returns>
		Task<PullRequest> GetPullRequest(long repositoryId, long installationId, int pullRequestNumber, CancellationToken cancellationToken);

		/// <summary>
		/// Get the files changed by a <see cref="PullRequest"/>
		/// </summary>
		/// <param name="pullRequest">The <see cref="PullRequest"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in a <see cref="IReadOnlyList{T}"/> of <see cref="PullRequestFile"/>s</returns>
		Task<IReadOnlyList<PullRequestFile>> GetPullRequestChangedFiles(PullRequest pullRequest, long installationId, CancellationToken cancellationToken);

		/// <summary>
		/// Get the content of a <paramref name="filePath"/> at a given <paramref name="commit"/>
		/// </summary>
		/// <param name="repositoryId">The <see cref="Repository.Id"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="filePath">The path to the file to download</param>
		/// <param name="commit">The commit sha of the file to download</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in a <see cref="byte"/> array of the file data</returns>
		Task<byte[]> GetFileAtCommit(long repositoryId, long installationId, string filePath, string commit, CancellationToken cancellationToken);

		/// <summary>
		/// Update a <see cref="CheckRun"/>
		/// </summary>
		/// <param name="repositoryId">The <see cref="Repository.Id"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="checkRunId">The <see cref="CheckRun.Id"/></param>
		/// <param name="checkRunUpdate">The <see cref="CheckRunUpdate"/></param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		Task UpdateCheckRun(long repositoryId, long installationId, long checkRunId, CheckRunUpdate checkRunUpdate, CancellationToken cancellationToken);

		/// <summary>
		/// Create a <see cref="CheckRun"/>
		/// </summary>
		/// <param name="repositoryId">The <see cref="Repository.Id"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="newCheckRun">The <see cref="NewCheckRun"/></param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the new <see cref="CheckRun.Id"/></returns>
		Task<long> CreateCheckRun(long repositoryId, long installationId, NewCheckRun newCheckRun, CancellationToken cancellationToken);

		/// <summary>
		/// Get <see cref="CheckRun"/>s matching a <paramref name="checkSuiteSha"/>
		/// </summary>
		/// <param name="repositoryId">The <see cref="Repository.Id"/></param>
		/// <param name="installationId">The <see cref="InstallationId.Id"/></param>
		/// <param name="checkSuiteId">The <see cref="CheckSuite.Id"/></param>
		/// <param name="checkSuiteSha">The <see cref="CheckSuite.HeadSha"/></param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in an <see cref="IEnumerable{T}"/> of relevant <see cref="CheckRun"/>s</returns>
		Task<IEnumerable<CheckRun>> GetMatchingCheckRuns(long repositoryId, long installationId, long checkSuiteId, string checkSuiteSha, CancellationToken cancellationToken);
    }
}
