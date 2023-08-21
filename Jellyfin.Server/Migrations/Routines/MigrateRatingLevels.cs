using System;
using System.Globalization;
using System.IO;

using Emby.Server.Implementations.Data;
using Jellyfin.Server.Extensions;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Model.Globalization;
using Microsoft.Extensions.Logging;
using SQLitePCL.pretty;

namespace Jellyfin.Server.Migrations.Routines
{
    /// <summary>
    /// Migrate rating levels to new rating level system.
    /// </summary>
    internal class MigrateRatingLevels : IMigrationRoutine
    {
        private const string DbFilename = "library.db";
        private readonly ILogger<MigrateRatingLevels> _logger;
        private readonly IServerApplicationPaths _applicationPaths;
        private readonly ILocalizationManager _localizationManager;
        private readonly IItemRepository _repository;

        public MigrateRatingLevels(
            IServerApplicationPaths applicationPaths,
            ILoggerFactory loggerFactory,
            ILocalizationManager localizationManager,
            IItemRepository repository)
        {
            _applicationPaths = applicationPaths;
            _localizationManager = localizationManager;
            _repository = repository;
            _logger = loggerFactory.CreateLogger<MigrateRatingLevels>();
        }

        /// <inheritdoc/>
        public Guid Id => Guid.Parse("{67445D54-B895-4B24-9F4C-35CE0690EA07}");

        /// <inheritdoc/>
        public string Name => "MigrateRatingLevels";

        /// <inheritdoc/>
        public bool PerformOnNewInstall => false;

        /// <inheritdoc/>
        public void Perform()
        {
            var dbPath = Path.Combine(_applicationPaths.DataPath, DbFilename);

            // Back up the database before modifying any entries
            for (int i = 1; ; i++)
            {
                var bakPath = string.Format(CultureInfo.InvariantCulture, "{0}.bak{1}", dbPath, i);
                if (!File.Exists(bakPath))
                {
                    try
                    {
                        File.Copy(dbPath, bakPath);
                        _logger.LogInformation("Library database backed up to {BackupPath}", bakPath);
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Cannot make a backup of {Library} at path {BackupPath}", DbFilename, bakPath);
                        throw;
                    }
                }
            }

            // Migrate parental rating strings to new levels
            _logger.LogInformation("Recalculating parental rating levels based on rating string.");
            using (var connection = SQLite3.Open(
                dbPath,
                ConnectionFlags.ReadWrite,
                null))
            {
                var queryResult = connection.Query("SELECT DISTINCT OfficialRating FROM TypedBaseItems");
                foreach (var entry in queryResult)
                {
                    var ratingString = entry[0].ToString();
                    if (string.IsNullOrEmpty(ratingString))
                    {
                        connection.Execute("UPDATE TypedBaseItems SET InheritedParentalRatingValue = NULL WHERE OfficialRating IS NULL OR OfficialRating='';");
                    }
                    else
                    {
                        var ratingValue = _localizationManager.GetRatingLevel(ratingString).ToString();
                        if (string.IsNullOrEmpty(ratingValue))
                        {
                            ratingValue = "NULL";
                        }

                        using var statement = connection.PrepareStatement("UPDATE TypedBaseItems SET InheritedParentalRatingValue = @Value WHERE OfficialRating = @Rating;");
                        statement.TryBind("@Value", ratingValue);
                        statement.TryBind("@Rating", ratingString);
                        statement.ExecuteQuery();
                    }
                }
            }
        }
    }
}
