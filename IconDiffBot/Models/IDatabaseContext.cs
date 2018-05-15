using IconDiffBot.Core;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;

namespace IconDiffBot.Models
{
	/// <summary>
	/// Represents a database containing models
	/// </summary>
    public interface IDatabaseContext
	{
		/// <summary>
		/// The <see cref="Installation"/>s in the database
		/// </summary>
		DbSet<Installation> Installations { get; set; }

		/// <summary>
		/// The <see cref="IconDiff"/>s in the database
		/// </summary>
		DbSet<IconDiff> IconDiffs { get; set; }

		/// <summary>
		/// The <see cref="IconState"/>s in the database
		/// </summary>
		DbSet<IconState> IconStates { get; set; }

		/// <summary>
		/// Save changes to the <see cref="IDatabaseContext"/>
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		Task Save(CancellationToken cancellationToken);

		/// <summary>
		/// Ensure the <see cref="IDatabaseContext"/> is ready
		/// </summary>
		/// <param name="cancellationToken">The <see cref="CancellationToken"/> for the operation</param>
		/// <returns>A <see cref="Task"/> representing the running operation</returns>
		Task Initialize(CancellationToken cancellationToken);
	}
}
