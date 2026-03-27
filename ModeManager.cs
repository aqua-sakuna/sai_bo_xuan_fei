using System;
using System.Collections.Generic;
using System.IO;

namespace Cyber​​ConcubineSelection
{
    /// <summary>
    /// Manages mode selection and exclusion settings.
    /// Handles persistence of mode configuration to config files.
    /// Supports migration from old Group A/B format to new Primary Mode + Filter Options format.
    /// </summary>
    public class ModeManager
    {
        // New data model
        private PrimaryMode primaryMode;
        private FilterOptions activeFilters;
        private ExclusionSettings exclusionSettings;
        private string customKeywords;
        
        // Track filter toggle times for conflict resolution
        private Dictionary<FilterOptions, DateTime> filterToggleTimes;

        private readonly string configDirectory;
        private readonly string modeFilePath;
        private readonly string exclusionFilePath;
        private readonly string customKeywordsFilePath;
        
        // Old file paths for migration
        private readonly string oldExclusionGroupAFilePath;
        private readonly string oldExclusionGroupBFilePath;

        /// <summary>
        /// Creates a new ModeManager instance with default configuration paths.
        /// </summary>
        public ModeManager() : this("config")
        {
        }

        /// <summary>
        /// Creates a new ModeManager instance with a custom configuration directory.
        /// </summary>
        /// <param name="configDir">The directory where configuration files are stored.</param>
        public ModeManager(string configDir)
        {
            configDirectory = configDir;
            modeFilePath = Path.Combine(configDirectory, "mode.txt");
            exclusionFilePath = Path.Combine(configDirectory, "exclusion.txt");
            customKeywordsFilePath = Path.Combine(configDirectory, "custom_keywords.txt");
            
            // Old paths for migration
            oldExclusionGroupAFilePath = Path.Combine(configDirectory, "exclusion_groupA.txt");
            oldExclusionGroupBFilePath = Path.Combine(configDirectory, "exclusion_groupB.txt");

            // Initialize with defaults
            primaryMode = PrimaryMode.VideoMode;
            activeFilters = FilterOptions.None;
            exclusionSettings = new ExclusionSettings();
            customKeywords = string.Empty;
            filterToggleTimes = new Dictionary<FilterOptions, DateTime>();
        }

        #region Primary Mode Management

        /// <summary>
        /// Sets the active primary mode.
        /// </summary>
        /// <param name="mode">The primary mode to activate (VideoMode or FolderMode).</param>
        public void SetPrimaryMode(PrimaryMode mode)
        {
            primaryMode = mode;
        }

        /// <summary>
        /// Gets the currently active primary mode.
        /// </summary>
        /// <returns>The active primary mode.</returns>
        public PrimaryMode GetPrimaryMode()
        {
            return primaryMode;
        }

        #endregion

        #region Filter Options Management

        /// <summary>
        /// Enables a specific filter option.
        /// </summary>
        /// <param name="filter">The filter to enable.</param>
        public void EnableFilter(FilterOptions filter)
        {
            if (filter == FilterOptions.None)
                return;
                
            activeFilters |= filter;
            filterToggleTimes[filter] = DateTime.Now;
        }

        /// <summary>
        /// Disables a specific filter option.
        /// </summary>
        /// <param name="filter">The filter to disable.</param>
        public void DisableFilter(FilterOptions filter)
        {
            if (filter == FilterOptions.None)
                return;
                
            activeFilters &= ~filter;
        }

        /// <summary>
        /// Toggles a specific filter option.
        /// </summary>
        /// <param name="filter">The filter to toggle.</param>
        public void ToggleFilter(FilterOptions filter)
        {
            if (filter == FilterOptions.None)
                return;
                
            if (IsFilterEnabled(filter))
            {
                DisableFilter(filter);
            }
            else
            {
                EnableFilter(filter);
            }
        }

        /// <summary>
        /// Checks if a specific filter is enabled.
        /// </summary>
        /// <param name="filter">The filter to check.</param>
        /// <returns>True if the filter is enabled, false otherwise.</returns>
        public bool IsFilterEnabled(FilterOptions filter)
        {
            if (filter == FilterOptions.None)
                return activeFilters == FilterOptions.None;
                
            return (activeFilters & filter) == filter;
        }

        /// <summary>
        /// Gets all currently enabled filters.
        /// </summary>
        /// <returns>The active filters as a bitwise combination.</returns>
        public FilterOptions GetActiveFilters()
        {
            return activeFilters;
        }

        /// <summary>
        /// Sets all active filters at once.
        /// </summary>
        /// <param name="filters">The filters to enable.</param>
        public void SetActiveFilters(FilterOptions filters)
        {
            activeFilters = filters;
        }

        /// <summary>
        /// Gets the most recently toggled sorting filter (NewestToOldest or OldestToNewest).
        /// Used to resolve conflicts when both sorting filters are enabled.
        /// </summary>
        /// <returns>The most recent sorting filter, or None if no sorting filter is enabled.</returns>
        public FilterOptions GetActiveSortingFilter()
        {
            var sortingFilters = new[] 
            { 
                FilterOptions.NewestToOldest, 
                FilterOptions.OldestToNewest 
            };

            FilterOptions mostRecent = FilterOptions.None;
            DateTime mostRecentTime = DateTime.MinValue;

            foreach (var filter in sortingFilters)
            {
                if (IsFilterEnabled(filter) && filterToggleTimes.ContainsKey(filter))
                {
                    if (filterToggleTimes[filter] > mostRecentTime)
                    {
                        mostRecentTime = filterToggleTimes[filter];
                        mostRecent = filter;
                    }
                }
            }

            return mostRecent;
        }

        #endregion

        #region Exclusion Settings Management

        /// <summary>
        /// Gets the current exclusion settings.
        /// </summary>
        /// <returns>A copy of the exclusion settings.</returns>
        public ExclusionSettings GetExclusionSettings()
        {
            return exclusionSettings.Clone();
        }

        /// <summary>
        /// Sets the exclusion settings.
        /// </summary>
        /// <param name="settings">The exclusion settings to apply.</param>
        public void SetExclusionSettings(ExclusionSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            exclusionSettings = settings.Clone();
        }

        #endregion

        #region Persistence

        /// <summary>
        /// Loads mode configuration and exclusion settings from config files.
        /// Handles migration from old format if needed.
        /// If files don't exist or are invalid, uses default values.
        /// </summary>
        public void LoadFromConfig()
        {
            // Ensure config directory exists
            if (!Directory.Exists(configDirectory))
            {
                Directory.CreateDirectory(configDirectory);
            }

            // Check if migration is needed
            if (NeedsMigration())
            {
                MigrateOldConfig();
            }
            else
            {
                // Load new format
                LoadModeConfig();
                LoadExclusionConfig();
                LoadCustomKeywords();
            }
        }

        /// <summary>
        /// Saves mode configuration and exclusion settings to config files.
        /// </summary>
        public void SaveToConfig()
        {
            // Ensure config directory exists
            if (!Directory.Exists(configDirectory))
            {
                Directory.CreateDirectory(configDirectory);
            }

            SaveModeConfig();
            SaveExclusionConfig();
            SaveCustomKeywords();
        }

        private bool NeedsMigration()
        {
            // Check if old format exists and new format doesn't
            if (!File.Exists(modeFilePath))
            {
                // Check for old mode.txt with "ActiveGroup" key
                string oldModePath = Path.Combine(configDirectory, "mode.txt");
                if (File.Exists(oldModePath))
                {
                    try
                    {
                        string content = File.ReadAllText(oldModePath);
                        return content.Contains("ActiveGroup=");
                    }
                    catch
                    {
                        return false;
                    }
                }
            }
            return false;
        }

        private void LoadModeConfig()
        {
            if (!File.Exists(modeFilePath))
            {
                // Use defaults
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(modeFilePath);
                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    
                    // Skip empty lines and comments
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                        continue;

                    string[] parts = trimmedLine.Split('=');
                    if (parts.Length != 2)
                        continue;

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    switch (key)
                    {
                        case "PrimaryMode":
                            if (Enum.TryParse<PrimaryMode>(value, out PrimaryMode mode))
                            {
                                primaryMode = mode;
                            }
                            break;

                        case "ActiveFilters":
                            ParseActiveFilters(value);
                            break;
                    }
                }
            }
            catch
            {
                // If any error occurs, keep default values
            }
        }

        private void ParseActiveFilters(string value)
        {
            activeFilters = FilterOptions.None;

            if (string.IsNullOrEmpty(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase))
                return;

            string[] filterNames = value.Split(',');
            foreach (string filterName in filterNames)
            {
                string trimmed = filterName.Trim();
                if (Enum.TryParse<FilterOptions>(trimmed, out FilterOptions filter))
                {
                    activeFilters |= filter;
                }
            }
        }

        private void SaveModeConfig()
        {
            try
            {
                List<string> lines = new List<string>
                {
                    "# Mode Configuration",
                    $"PrimaryMode={primaryMode}",
                    $"ActiveFilters={FormatActiveFilters()}"
                };

                File.WriteAllLines(modeFilePath, lines);
            }
            catch
            {
                // Silently fail - configuration will remain in memory
            }
        }

        private string FormatActiveFilters()
        {
            if (activeFilters == FilterOptions.None)
                return "None";

            List<string> enabledFilters = new List<string>();

            if ((activeFilters & FilterOptions.NewestToOldest) == FilterOptions.NewestToOldest)
                enabledFilters.Add("NewestToOldest");
            if ((activeFilters & FilterOptions.OldestToNewest) == FilterOptions.OldestToNewest)
                enabledFilters.Add("OldestToNewest");
            if ((activeFilters & FilterOptions.NeverWatched) == FilterOptions.NeverWatched)
                enabledFilters.Add("NeverWatched");
            if ((activeFilters & FilterOptions.FavoritesOnly) == FilterOptions.FavoritesOnly)
                enabledFilters.Add("FavoritesOnly");

            return string.Join(",", enabledFilters);
        }

        private void LoadExclusionConfig()
        {
            if (!File.Exists(exclusionFilePath))
            {
                // Use defaults
                return;
            }

            try
            {
                ExclusionSettings settings = new ExclusionSettings();
                string[] lines = File.ReadAllLines(exclusionFilePath);

                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    
                    // Skip empty lines and comments
                    if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                        continue;

                    string[] parts = trimmedLine.Split('=');
                    if (parts.Length != 2)
                        continue;

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    if (int.TryParse(value, out int intValue))
                    {
                        switch (key)
                        {
                            case "ExcludeRecentDays":
                                settings.ExcludeRecentDays = intValue;
                                break;

                            case "ExcludeRecentCount":
                                settings.ExcludeRecentCount = intValue;
                                break;

                            case "MinVideosForExclusion":
                                settings.MinVideosForExclusion = intValue;
                                break;
                        }
                    }
                }

                exclusionSettings = settings;
            }
            catch
            {
                // If any error occurs, keep default values
            }
        }

        private void SaveExclusionConfig()
        {
            try
            {
                List<string> lines = new List<string>
                {
                    "# Exclusion Settings (applies to all mode combinations)",
                    $"ExcludeRecentDays={exclusionSettings.ExcludeRecentDays}",
                    $"ExcludeRecentCount={exclusionSettings.ExcludeRecentCount}",
                    $"MinVideosForExclusion={exclusionSettings.MinVideosForExclusion}"
                };

                File.WriteAllLines(exclusionFilePath, lines);
            }
            catch
            {
                // Silently fail - configuration will remain in memory
            }
        }

        #endregion

        #region Migration

        #pragma warning disable CS0618 // Type or member is obsolete
        
        /// <summary>
        /// Migrates old Group A/B configuration to new format.
        /// </summary>
        private void MigrateOldConfig()
        {
            try
            {
                // Load old format
                ModeGroup oldActiveGroup = ModeGroup.GroupA;
                GroupAMode oldGroupAMode = GroupAMode.VideoMode;
                GroupBMode oldGroupBMode = GroupBMode.NewestToOldest;

                LoadOldModeConfig(ref oldActiveGroup, ref oldGroupAMode, ref oldGroupBMode);

                // Convert to new format
                if (oldActiveGroup == ModeGroup.GroupA)
                {
                    // Map Group A mode directly to Primary Mode
                    primaryMode = (oldGroupAMode == GroupAMode.VideoMode) 
                        ? PrimaryMode.VideoMode 
                        : PrimaryMode.FolderMode;
                    activeFilters = FilterOptions.None;
                }
                else // Group B was active
                {
                    // Default to Video Mode for primary
                    primaryMode = PrimaryMode.VideoMode;

                    // Map Group B mode to corresponding filter
                    activeFilters = oldGroupBMode switch
                    {
                        GroupBMode.NewestToOldest => FilterOptions.NewestToOldest,
                        GroupBMode.OldestToNewest => FilterOptions.OldestToNewest,
                        GroupBMode.NeverWatched => FilterOptions.NeverWatched,
                        GroupBMode.FavoritesOnly => FilterOptions.FavoritesOnly,
                        _ => FilterOptions.None
                    };
                }

                // Load and merge exclusion settings (use Group A settings as base)
                LoadOldExclusionConfig();

                // Save in new format
                SaveToConfig();

                // Backup old files
                BackupOldConfigFiles();
            }
            catch (Exception ex)
            {
                // Log error and use safe defaults
                LogError($"Configuration migration failed: {ex.Message}");
                
                primaryMode = PrimaryMode.VideoMode;
                activeFilters = FilterOptions.None;
                exclusionSettings = new ExclusionSettings();
            }
        }

        private void LoadOldModeConfig(ref ModeGroup activeGroup, ref GroupAMode groupAMode, ref GroupBMode groupBMode)
        {
            string oldModePath = Path.Combine(configDirectory, "mode.txt");
            if (!File.Exists(oldModePath))
                return;

            try
            {
                string[] lines = File.ReadAllLines(oldModePath);
                foreach (string line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (string.IsNullOrEmpty(trimmedLine))
                        continue;

                    string[] parts = trimmedLine.Split('=');
                    if (parts.Length != 2)
                        continue;

                    string key = parts[0].Trim();
                    string value = parts[1].Trim();

                    switch (key)
                    {
                        case "ActiveGroup":
                            if (Enum.TryParse<ModeGroup>(value, out ModeGroup group))
                            {
                                activeGroup = group;
                            }
                            break;

                        case "GroupAMode":
                            if (Enum.TryParse<GroupAMode>(value, out GroupAMode modeA))
                            {
                                groupAMode = modeA;
                            }
                            break;

                        case "GroupBMode":
                            if (Enum.TryParse<GroupBMode>(value, out GroupBMode modeB))
                            {
                                groupBMode = modeB;
                            }
                            break;
                    }
                }
            }
            catch
            {
                // Keep defaults
            }
        }

        private void LoadOldExclusionConfig()
        {
            // Try to load Group A exclusion settings (use as base)
            if (File.Exists(oldExclusionGroupAFilePath))
            {
                try
                {
                    ExclusionSettings settings = new ExclusionSettings();
                    string[] lines = File.ReadAllLines(oldExclusionGroupAFilePath);

                    foreach (string line in lines)
                    {
                        string trimmedLine = line.Trim();
                        if (string.IsNullOrEmpty(trimmedLine))
                            continue;

                        string[] parts = trimmedLine.Split('=');
                        if (parts.Length != 2)
                            continue;

                        string key = parts[0].Trim();
                        string value = parts[1].Trim();

                        if (int.TryParse(value, out int intValue))
                        {
                            switch (key)
                            {
                                case "ExcludeRecentDays":
                                    settings.ExcludeRecentDays = intValue;
                                    break;

                                case "ExcludeRecentCount":
                                    settings.ExcludeRecentCount = intValue;
                                    break;

                                case "MinVideosForExclusion":
                                    settings.MinVideosForExclusion = intValue;
                                    break;
                            }
                        }
                    }

                    exclusionSettings = settings;
                }
                catch
                {
                    // Keep defaults
                }
            }
        }

        private void BackupOldConfigFiles()
        {
            try
            {
                string backupDir = Path.Combine(configDirectory, "backup_old_format");
                Directory.CreateDirectory(backupDir);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

                TryBackupFile("mode.txt", Path.Combine(backupDir, $"mode_{timestamp}.txt"));
                TryBackupFile("exclusion_groupA.txt", Path.Combine(backupDir, $"exclusion_groupA_{timestamp}.txt"));
                TryBackupFile("exclusion_groupB.txt", Path.Combine(backupDir, $"exclusion_groupB_{timestamp}.txt"));
            }
            catch
            {
                // Backup failure is not critical
            }
        }

        private void TryBackupFile(string fileName, string backupPath)
        {
            try
            {
                string sourcePath = Path.Combine(configDirectory, fileName);
                if (File.Exists(sourcePath))
                {
                    File.Copy(sourcePath, backupPath, overwrite: true);
                }
            }
            catch
            {
                // Individual file backup failure is not critical
            }
        }

        private void LogError(string message)
        {
            try
            {
                string logPath = Path.Combine(configDirectory, "migration_errors.log");
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(logPath, logEntry);
            }
            catch
            {
                // Even logging can fail, but we don't want to crash
            }
        }

        #pragma warning restore CS0618 // Type or member is obsolete

        #endregion

        #region Obsolete Methods (for backward compatibility)

        #pragma warning disable CS0618 // Type or member is obsolete

        /// <summary>
        /// Sets the active mode for a specific mode group.
        /// </summary>
        /// <param name="group">The mode group to configure.</param>
        /// <param name="mode">The mode to activate.</param>
        [Obsolete("Use SetPrimaryMode() and EnableFilter() instead. This method is kept for backward compatibility.")]
        public void SetMode(ModeGroup group, object mode)
        {
            if (group == ModeGroup.GroupA)
            {
                if (mode is GroupAMode groupAMode)
                {
                    primaryMode = (groupAMode == GroupAMode.VideoMode) 
                        ? PrimaryMode.VideoMode 
                        : PrimaryMode.FolderMode;
                }
            }
            else if (group == ModeGroup.GroupB)
            {
                if (mode is GroupBMode groupBMode)
                {
                    FilterOptions filter = groupBMode switch
                    {
                        GroupBMode.NewestToOldest => FilterOptions.NewestToOldest,
                        GroupBMode.OldestToNewest => FilterOptions.OldestToNewest,
                        GroupBMode.NeverWatched => FilterOptions.NeverWatched,
                        GroupBMode.FavoritesOnly => FilterOptions.FavoritesOnly,
                        _ => FilterOptions.None
                    };
                    
                    activeFilters = filter;
                }
            }
        }

        /// <summary>
        /// Gets the currently active mode group and mode.
        /// </summary>
        /// <returns>A tuple containing the active group and mode.</returns>
        [Obsolete("Use GetPrimaryMode() and GetActiveFilters() instead. This method is kept for backward compatibility.")]
        public (ModeGroup Group, object Mode) GetActiveMode()
        {
            // For backward compatibility, return Group A if no filters, Group B if filters active
            if (activeFilters == FilterOptions.None)
            {
                GroupAMode mode = (primaryMode == PrimaryMode.VideoMode) 
                    ? GroupAMode.VideoMode 
                    : GroupAMode.FolderMode;
                return (ModeGroup.GroupA, mode);
            }
            else
            {
                // Return the first active filter as Group B mode
                GroupBMode mode = GroupBMode.NewestToOldest;
                if ((activeFilters & FilterOptions.NewestToOldest) == FilterOptions.NewestToOldest)
                    mode = GroupBMode.NewestToOldest;
                else if ((activeFilters & FilterOptions.OldestToNewest) == FilterOptions.OldestToNewest)
                    mode = GroupBMode.OldestToNewest;
                else if ((activeFilters & FilterOptions.NeverWatched) == FilterOptions.NeverWatched)
                    mode = GroupBMode.NeverWatched;
                else if ((activeFilters & FilterOptions.FavoritesOnly) == FilterOptions.FavoritesOnly)
                    mode = GroupBMode.FavoritesOnly;
                    
                return (ModeGroup.GroupB, mode);
            }
        }

        /// <summary>
        /// Gets the exclusion settings for a specific mode group.
        /// </summary>
        /// <param name="group">The mode group to query.</param>
        /// <returns>A copy of the exclusion settings.</returns>
        [Obsolete("Use GetExclusionSettings() instead. This method is kept for backward compatibility.")]
        public ExclusionSettings GetExclusionSettings(ModeGroup group)
        {
            return exclusionSettings.Clone();
        }

        /// <summary>
        /// Sets the exclusion settings for a specific mode group.
        /// </summary>
        /// <param name="group">The mode group to configure.</param>
        /// <param name="settings">The exclusion settings to apply.</param>
        [Obsolete("Use SetExclusionSettings() instead. This method is kept for backward compatibility.")]
        public void SetExclusionSettings(ModeGroup group, ExclusionSettings settings)
        {
            SetExclusionSettings(settings);
        }

        #pragma warning restore CS0618 // Type or member is obsolete

        #endregion

        #region Custom Keywords Management

        /// <summary>
        /// Sets the custom keywords for filtering.
        /// </summary>
        /// <param name="keywords">Comma-separated keywords.</param>
        public void SetCustomKeywords(string keywords)
        {
            customKeywords = keywords ?? string.Empty;
        }

        /// <summary>
        /// Gets the custom keywords for filtering.
        /// </summary>
        /// <returns>Comma-separated keywords.</returns>
        public string GetCustomKeywords()
        {
            return customKeywords ?? string.Empty;
        }

        /// <summary>
        /// Saves custom keywords to config/custom_keywords.txt.
        /// </summary>
        private void SaveCustomKeywords()
        {
            try
            {
                string keywords = customKeywords ?? string.Empty;
                File.WriteAllText(customKeywordsFilePath, keywords);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving custom keywords: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads custom keywords from config/custom_keywords.txt.
        /// </summary>
        private void LoadCustomKeywords()
        {
            try
            {
                if (File.Exists(customKeywordsFilePath))
                {
                    customKeywords = File.ReadAllText(customKeywordsFilePath).Trim();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading custom keywords: {ex.Message}");
            }
        }

        #endregion
    }
}
