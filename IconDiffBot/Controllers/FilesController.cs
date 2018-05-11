using IconDiffBot.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Octokit;
using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IconDiffBot.Controllers
{
	/// <summary>
	/// <see cref="Controller"/> used for recieving GitHub webhooks
	/// </summary>
	[Route(Route)]
	public sealed class FilesController : Controller
	{
		/// <summary>
		/// The route to the <see cref="FilesController"/>
		/// </summary>
		const string Route = "Files";

		/// <summary>
		/// The <see cref="ILogger{TCategoryName}"/> for the <see cref="FilesController"/>
		/// </summary>
		readonly ILogger<FilesController> logger;
		/// <summary>
		/// The <see cref="IDatabaseContext"/> for the <see cref="FilesController"/>
		/// </summary>
		readonly IDatabaseContext databaseContext;

		/// <summary>
		/// Create a route for a <paramref name="checkRunId"/> diff image
		/// </summary>
		/// <param name="repository">The <see cref="Repository"/></param>
		/// <param name="checkRunId">The <see cref="CheckRun.Id"/></param>
		/// <param name="fileId">The <see cref="IconDiff.FileId"/></param>
		/// <param name="postfix">Either "before", "after" or "logs"</param>
		/// <returns>A relative url to the appropriate <see cref="FilesController"/> action</returns>
		public static string RouteTo(Repository repository, long checkRunId, int fileId, string postfix) => String.Format(CultureInfo.InvariantCulture, "/{5}/{0}/{1}/{2}/{3}.{4}", repository.Id, checkRunId, fileId, postfix, postfix == "logs" ? "txt" : "png", Route);

		/// <summary>
		/// Construct a <see cref="FilesController"/>
		/// </summary>
		/// <param name="logger">The value of <see cref="logger"/></param>
		/// <param name="databaseContext">The value of <see cref="databaseContext"/></param>
		public FilesController(ILogger<FilesController> logger, IDatabaseContext databaseContext)
		{
			this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
			this.databaseContext = databaseContext ?? throw new ArgumentNullException(nameof(databaseContext));
		}

		/// <summary>
		/// Handle a GET of a <see cref="IconDiff"/>
		/// </summary>
		/// <param name="repositoryId">The <see cref="IconDiff.RepositoryId"/></param>
		/// <param name="checkRunId">The <see cref="CheckRun.Id"/></param>
		/// <param name="fileId">The <see cref="IconDiff.FileId"/></param>
		/// <param name="beforeOrAfter">"before" or "after"</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the operation</returns>
		[HttpGet("{repositoryId}/{checkRunId}/{fileId}/{beforeOrAfter}.png")]
		[ResponseCache(VaryByHeader = "User-Agent", Duration = 60)]
		public async Task<IActionResult> HandleIconGet(long repositoryId, long checkRunId, int fileId, string beforeOrAfter, CancellationToken cancellationToken)
		{
			if (beforeOrAfter == null)
				throw new ArgumentNullException(nameof(beforeOrAfter));

			logger.LogTrace("Recieved GET: {0}/{1}/{2}/{3}.png", repositoryId, checkRunId, fileId, beforeOrAfter);

			beforeOrAfter = beforeOrAfter.ToUpperInvariant();
			bool before = beforeOrAfter == "BEFORE";
			if (!before && beforeOrAfter != "AFTER")
				return BadRequest();

			var diff = await databaseContext.IconDiffs.Where(x => x.RepositoryId == repositoryId && x.CheckRunId == checkRunId && x.FileId == fileId).Select(x => before ? x.BeforeImage : x.AfterImage).ToAsyncEnumerable().FirstOrDefault(cancellationToken).ConfigureAwait(false);

			if (diff == null)
				return NotFound();

			return File(diff, "image/png");
		}
	}
}