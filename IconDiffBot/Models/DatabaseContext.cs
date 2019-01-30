﻿using IconDiffBot.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;
using ZNetCS.AspNetCore.Logging.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;

namespace IconDiffBot.Models
{
	/// <inheritdoc />
#pragma warning disable CA1812
	sealed class DatabaseContext : DbContext, IDatabaseContext
#pragma warning restore CA1812
	{
		/// <inheritdoc />
		public DbSet<Installation> Installations { get; set; }

		/// <inheritdoc />
		public DbSet<IconDiff> IconDiffs { get; set; }


		/// <inheritdoc />
		public DbSet<Image> Images { get; set; }

		/// <summary>
		/// The <see cref="DbSet{TEntity}"/> for <see cref="Log"/>s
		/// </summary>
		public DbSet<Log> Logs { get; set; }

		/// <summary>
		/// The <see cref="DatabaseConfiguration"/> for the <see cref="DatabaseContext"/>
		/// </summary>
		readonly DatabaseConfiguration databaseConfiguration;
		/// <summary>
		/// The <see cref="ILoggerFactory"/> for the <see cref="DatabaseContext"/>
		/// </summary>
		readonly ILoggerFactory loggerFactory;
		/// <summary>
		/// The <see cref="IHostingEnvironment"/> for the <see cref="DatabaseContext"/>
		/// </summary>
		readonly IHostingEnvironment hostingEnvironment;

		/// <summary>
		/// Construct a <see cref="DatabaseContext"/>
		/// </summary>
		/// <param name="options">The <see cref="DbContextOptions{TContext}"/> for the <see cref="DatabaseContext"/></param>
		/// <param name="databaseConfigurationOptions">The <see cref="IOptions{TOptions}"/> containing the value of <see cref="databaseConfiguration"/></param>
		/// <param name="loggerFactory">The value of <see cref="loggerFactory"/></param>
		/// <param name="hostingEnvironment">The value of <see cref="hostingEnvironment"/></param>
		public DatabaseContext(DbContextOptions<DatabaseContext> options, IOptions<DatabaseConfiguration> databaseConfigurationOptions, ILoggerFactory loggerFactory, IHostingEnvironment hostingEnvironment) : base(options)
		{
			databaseConfiguration = databaseConfigurationOptions?.Value ?? throw new ArgumentNullException(nameof(databaseConfigurationOptions));
			this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
			this.hostingEnvironment = hostingEnvironment ?? throw new ArgumentNullException(nameof(hostingEnvironment));
		}

		/// <inheritdoc />
		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);

			// build default model.
			LogModelBuilderHelper.Build(modelBuilder.Entity<Log>());
			// real relation database can map table:
			modelBuilder.Entity<Log>().ToTable(nameof(Log));

			//enable map diff indexing
			modelBuilder.Entity<IconDiff>().HasKey(x => new { x.RepositoryId, x.CheckRunId, x.FileId });

			//unique image shas
			modelBuilder.Entity<Image>().HasIndex(x => x.Sha1).IsUnique();
		}

		/// <inheritdoc />
		protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
		{
			if (databaseConfiguration.IsMySQL)
				optionsBuilder.UseMySql(databaseConfiguration.ConnectionString);
			else
				optionsBuilder.UseSqlServer(databaseConfiguration.ConnectionString);
			optionsBuilder.UseLoggerFactory(loggerFactory);
			if (hostingEnvironment.IsDevelopment())
				optionsBuilder.EnableSensitiveDataLogging();
		}

		/// <inheritdoc />
		public Task Save(CancellationToken cancellationToken) => SaveChangesAsync(cancellationToken);

		/// <inheritdoc />
		public Task Initialize(CancellationToken cancellationToken) => Database.EnsureCreatedAsync(cancellationToken);
	}
}
