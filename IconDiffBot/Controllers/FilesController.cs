using IconDiffBot.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
	/// <see cref="Controller"/> used for loading stored images
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
		/// <param name="before"><see langword="true"/> for "before", "after" otherwise</param>
		/// <param name="isGif">If the <see cref="IconDiff"/> is for a .gif</param>
		/// <returns>A relative url to the appropriate <see cref="FilesController"/> action</returns>
		public static string RouteTo(Repository repository, long checkRunId, int fileId, bool before, bool isGif) => String.Format(CultureInfo.InvariantCulture, "/{4}/{0}/{1}/{2}/{3}.{5}", repository.Id, checkRunId, fileId, before ? "before" : "after", Route, isGif ? "gif" : "png");

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
		/// <param name="postfix">The requested image extension</param>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task{TResult}"/> resulting in the <see cref="IActionResult"/> of the operation</returns>
		[HttpGet("{repositoryId}/{checkRunId}/{fileId}/{beforeOrAfter}.{postfix}")]
		[ResponseCache(Duration = Int32.MaxValue)]
		public async Task<IActionResult> HandleIconGet(long repositoryId, long checkRunId, int fileId, string beforeOrAfter, string postfix, CancellationToken cancellationToken)
		{
			if (beforeOrAfter == null)
				throw new ArgumentNullException(nameof(beforeOrAfter));
			if (postfix == null)
				throw new ArgumentNullException(nameof(postfix));

			postfix = postfix.ToUpperInvariant();
			var gif = postfix == "GIF";
			if (!gif && postfix != "PNG")
				return BadRequest();

			logger.LogTrace("Recieved GET: {0}/{1}/{2}/{3}.png", repositoryId, checkRunId, fileId, beforeOrAfter);

			beforeOrAfter = beforeOrAfter.ToUpperInvariant();
			var before = beforeOrAfter == "BEFORE";
			if (!before && beforeOrAfter != "AFTER")
				return BadRequest();

			var diff = await databaseContext
				.IconDiffs
				.Where(x => x.RepositoryId == repositoryId && x.CheckRunId == checkRunId && x.FileId == fileId && (before ? x.Before != null : x.After != null))
				.Select(x => before ? x.Before : x.After)
				.Select(x => x.Data).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);

			if (diff == null)
				return NotFound();

			return File(diff, gif ? "image/png" : "image/gif");
		}
	}
}
