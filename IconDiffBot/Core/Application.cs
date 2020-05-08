using Hangfire;
using Hangfire.Dashboard;
using Hangfire.MySql;
using Hangfire.SqlServer;
using IconDiffBot.Configuration;
using IconDiffBot.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing.Matching;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using ZNetCS.AspNetCore.Logging.EntityFrameworkCore;

namespace IconDiffBot.Core
{
	/// <summary>
	/// Startup point for the web application
	/// </summary>
	public sealed class Application
	{
		/// <summary>
		/// The <see cref="IConfiguration"/> for the <see cref="Application"/>
		/// </summary>
		readonly IConfiguration configuration;

		/// <summary>
		/// Construct an <see cref="Application"/>
		/// </summary>
		/// <param name="configuration">The value of <see cref="configuration"/></param>
		public Application(IConfiguration configuration) =>	this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

		/// <summary>
		/// Configure dependency injected services
		/// </summary>
		/// <param name="services">The <see cref="IServiceCollection"/> to configure</param>
		public void ConfigureServices(IServiceCollection services)
		{
			if (services == null)
				throw new ArgumentNullException(nameof(services));

			services.Configure<IISOptions>((options) => options.ForwardClientCertificate = false);
			services.Configure<GitHubConfiguration>(configuration.GetSection(GitHubConfiguration.Section));
			services.Configure<GeneralConfiguration>(configuration.GetSection(GeneralConfiguration.Section));
			var dbConfigSection = configuration.GetSection(DatabaseConfiguration.Section);
			services.Configure<DatabaseConfiguration>(dbConfigSection);

			services.AddHangfire((builder) =>
			{
				var dbConfig = dbConfigSection.Get<DatabaseConfiguration>();
				if (dbConfig.IsMySQL)
					builder.UseStorage(new MySqlStorage(dbConfig.ConnectionString, new MySqlStorageOptions { PrepareSchemaIfNecessary = true }));
				else
					builder.UseSqlServerStorage(dbConfig.ConnectionString, new SqlServerStorageOptions { PrepareSchemaIfNecessary = true });
			});

			services.AddHangfireServer();

			services.Configure<IISOptions>((options) => options.ForwardClientCertificate = false);
			services.AddOptions();
			services.AddLocalization();
			services.AddControllersWithViews();
			services.AddRazorPages();

			services.AddDbContext<DatabaseContext>(ServiceLifetime.Transient);
			services.AddScoped<IDatabaseContext>(x => x.GetRequiredService<DatabaseContext>());
			services.AddScoped<IGitHubClientFactory, GitHubClientFactory>();
			services.AddScoped<IGitHubManager, GitHubManager>();
			services.AddScoped<IWebRequestManager, WebRequestManager>();

			services.AddSingleton<IWebRequestManager, WebRequestManager>();
			services.AddSingleton<IPayloadProcessor, PayloadProcessor>();
			services.AddSingleton<IDiffGenerator, DiffGenerator>();
		}

		/// <summary>
		/// Configure the <see cref="Application"/>
		/// </summary>
		/// <param name="applicationBuilder">The <see cref="IApplicationBuilder"/> to configure</param>
		/// <param name="hostingEnvironment">The <see cref="IWebHostEnvironment"/> of the <see cref="Application"/></param>
		/// <param name="loggerFactory">The <see cref="ILoggerFactory"/> to configure</param>
		/// <param name="databaseContext">The <see cref="IDatabaseContext"/> to configure</param>
		/// <param name="applicationLifetime">The <see cref="IApplicationLifetime"/> to use <see cref="System.Threading.CancellationToken"/>s from</param>
		/// <param name="databaseConfigurationOptions">The <see cref="IOptions{TOptions}"/> for the <see cref="DatabaseConfiguration"/>.</param>
		public void Configure(IApplicationBuilder applicationBuilder, IWebHostEnvironment hostingEnvironment, ILoggerFactory loggerFactory, IHostApplicationLifetime applicationLifetime, IDatabaseContext databaseContext, IOptions<DatabaseConfiguration> databaseConfigurationOptions)
		{
			if (applicationBuilder == null)
				throw new ArgumentNullException(nameof(applicationBuilder));
			if (hostingEnvironment == null)
				throw new ArgumentNullException(nameof(hostingEnvironment));
			if (loggerFactory == null)
				throw new ArgumentNullException(nameof(loggerFactory));
			if (applicationLifetime == null)
				throw new ArgumentNullException(nameof(applicationLifetime));
			if (databaseContext == null)
				throw new ArgumentNullException(nameof(databaseContext));
			
			databaseContext.Initialize(applicationLifetime.ApplicationStopping).GetAwaiter().GetResult();

			if (databaseConfigurationOptions.Value.EnableLogging)
				loggerFactory.AddEntityFramework<DatabaseContext>(applicationBuilder.ApplicationServices);

			if (hostingEnvironment.IsDevelopment())
				applicationBuilder.UseDeveloperExceptionPage();

			var defaultCulture = new CultureInfo("en");
			var supportedCultures = new List<CultureInfo>
			{
				defaultCulture
			};

			CultureInfo.CurrentCulture = defaultCulture;
			CultureInfo.CurrentUICulture = defaultCulture;

			applicationBuilder.UseRequestLocalization(new RequestLocalizationOptions
			{
				SupportedCultures = supportedCultures,
				SupportedUICultures = supportedCultures,
			});
			
			applicationBuilder.UseHangfireServer();

			if (hostingEnvironment.IsDevelopment())
				applicationBuilder.UseHangfireDashboard("/Hangfire", new DashboardOptions
				{
					Authorization = new List<IDashboardAuthorizationFilter> { }
				});

			applicationBuilder.UseRouting();
			applicationBuilder.UseEndpoints(endpoints => {
				endpoints.MapControllers();
			});
		}
    }
}
