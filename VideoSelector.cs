using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CyberConcubineSelection
{
    /// <summary>
    /// Core logic for selecting videos based on active mode, enabled paths, and filters.
    /// Implements video scanning, aggregation, and deduplication from multiple paths.
    /// Validates: Requirements 2.6, 17.1, 17.2, 17.3, 17.5
    /// </summary>
    public class VideoSelector
    {
        private readonly PathManager pathManager;
        private readonly ModeManager modeManager;
        private readonly HistoryManager historyManager;
        private readonly FavoritesManager favoritesManager;
        private readonly BlackWhiteListManager blackWhiteListManager;
        private readonly string configDirectory;

        // Supported video file extensions
        private static readonly HashSet<string> VideoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".wmv", ".mov", ".flv", ".rmvb"
        };

        // Cached keyword lists
        private List<string>? superResolutionKeywords;
        private List<string>? uncensoredKeywords;

        /// <summary>
        /// Creates a new VideoSelector instance with required dependencies.
        /// </summary>
        /// <param name="pathManager">The path manager for accessing enabled paths</param>
        /// <param name="modeManager">The mode manager for accessing active mode and settings</param>
        /// <param name="historyManager">The history manager for tracking watched videos</param>
        /// <param name="favoritesManager">The favorites manager for accessing favorite videos</param>
        /// <param name="blackWhiteListManager">The black/white list manager for folder filtering</param>
        public VideoSelector(
            PathManager pathManager, 
            ModeManager modeManager, 
            HistoryManager historyManager, 
            FavoritesManager favoritesManager,
            BlackWhiteListManager blackWhiteListManager)
        {
            this.pathManager = pathManager ?? throw new ArgumentNullException(nameof(pathManager));
            this.modeManager = modeManager ?? throw new ArgumentNullException(nameof(modeManager));
            this.historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
            this.favoritesManager = favoritesManager ?? throw new ArgumentNullException(nameof(favoritesManager));
            this.blackWhiteListManager = blackWhiteListManager ?? throw new ArgumentNullException(nameof(blackWhiteListManager));
            this.configDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
        }

        /// <summary>
        /// Scans all enabled paths and aggregates videos into a single deduplicated list.
        /// Validates: Requirements 2.6, 17.1, 17.2, 17.3, 17.5
        /// </summary>
        /// <returns>List of unique video file paths from all enabled paths</returns>
        public List<string> ScanEnabledPaths()
        {
            // Get all enabled paths from PathManager
            List<string> enabledPaths = pathManager.GetEnabledPaths();

            // Use HashSet for automatic deduplication
            HashSet<string> uniqueVideos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Scan each enabled path
            foreach (string path in enabledPaths)
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                {
                    continue;
                }

                try
                {
                    // Recursively scan for video files
                    ScanDirectory(path, uniqueVideos);
                }
                catch (Exception ex)
                {
                    // Log error but continue with other paths
                    Console.WriteLine($"Error scanning path {path}: {ex.Message}");
                }
            }

            // Convert HashSet to List and return
            return uniqueVideos.ToList();
        }

        /// <summary>
        /// Recursively scans a directory for video files and adds them to the collection.
        /// </summary>
        /// <param name="directoryPath">The directory to scan</param>
        /// <param name="videoCollection">The collection to add found videos to</param>
        private void ScanDirectory(string directoryPath, HashSet<string> videoCollection)
        {
            try
            {
                // Get all files in current directory
                string[] files = Directory.GetFiles(directoryPath);

                // Filter for video files and add to collection
                foreach (string file in files)
                {
                    string extension = Path.GetExtension(file);
                    if (VideoExtensions.Contains(extension))
                    {
                        // HashSet automatically handles deduplication
                        videoCollection.Add(file);
                    }
                }

                // Recursively scan subdirectories
                string[] subdirectories = Directory.GetDirectories(directoryPath);
                foreach (string subdirectory in subdirectories)
                {
                    // Skip hidden and system directories
                    DirectoryInfo dirInfo = new DirectoryInfo(subdirectory);
                    if ((dirInfo.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden ||
                        (dirInfo.Attributes & FileAttributes.System) == FileAttributes.System)
                    {
                        continue;
                    }

                    // Skip recycle bin directories
                    string dirName = dirInfo.Name.ToLower();
                    if (dirName == "$recycle.bin" || dirName == "recycler" || dirName == "recycled")
                    {
                        continue;
                    }

                    ScanDirectory(subdirectory, videoCollection);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have permission to access
            }
            catch (Exception ex)
            {
                // Log other errors but continue scanning
                Console.WriteLine($"Error scanning directory {directoryPath}: {ex.Message}");
            }
        }
        /// <summary>
        /// Loads super resolution keywords from config/super_resolution_keywords.txt.
        /// Returns default keywords if file doesn't exist.
        /// </summary>
        private List<string> LoadSuperResolutionKeywords()
        {
            if (superResolutionKeywords != null)
            {
                return superResolutionKeywords;
            }

            string filePath = Path.Combine(configDirectory, "super_resolution_keywords.txt");

            if (File.Exists(filePath))
            {
                try
                {
                    superResolutionKeywords = File.ReadAllLines(filePath)
                        .Select(line => line.Trim().ToLower())
                        .Where(line => !string.IsNullOrEmpty(line))
                        .ToList();

                    if (superResolutionKeywords.Count > 0)
                    {
                        return superResolutionKeywords;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading super resolution keywords: {ex.Message}");
                }
            }

            // Default keywords if file doesn't exist or is empty
            superResolutionKeywords = new List<string> { "iris", "prob", "apo" };
            return superResolutionKeywords;
        }

        /// <summary>
        /// Loads uncensored keywords from config/uncensored_keywords.txt.
        /// Returns default keywords if file doesn't exist.
        /// </summary>
        private List<string> LoadUncensoredKeywords()
        {
            if (uncensoredKeywords != null)
            {
                return uncensoredKeywords;
            }

            string filePath = Path.Combine(configDirectory, "uncensored_keywords.txt");

            if (File.Exists(filePath))
            {
                try
                {
                    uncensoredKeywords = File.ReadAllLines(filePath)
                        .Select(line => line.Trim().ToLower())
                        .Where(line => !string.IsNullOrEmpty(line))
                        .ToList();

                    if (uncensoredKeywords.Count > 0)
                    {
                        return uncensoredKeywords;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading uncensored keywords: {ex.Message}");
                }
            }

            // Default keywords if file doesn't exist or is empty
            uncensoredKeywords = new List<string> { "wm", "无码", "破解", "restored","uncensored","uc","censored" };
            return uncensoredKeywords;
        }

        /// <summary>
        /// Checks if a filename contains any of the specified keywords.
        /// For short keywords (2 characters or less), uses exact word matching to avoid false positives.
        /// For longer keywords, uses simple contains matching.
        /// </summary>
        private bool ContainsKeyword(string fileName, List<string> keywords)
        {
            fileName = fileName.ToLower();

            foreach (string keyword in keywords)
            {
                if (keyword.Length <= 2)
                {
                    // Short keyword: use word boundary matching
                    // Match if keyword appears as a separate word (surrounded by non-letter characters)
                    if (System.Text.RegularExpressions.Regex.IsMatch(fileName, $@"\b{System.Text.RegularExpressions.Regex.Escape(keyword)}\b"))
                    {
                        return true;
                    }
                }
                else
                {
                    // Long keyword: simple contains matching
                    if (fileName.Contains(keyword))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Applies mode-specific filtering logic to the video list.
        /// Group A: Applies black/white list filtering ONLY in FolderMode
        /// Group B: Applies special mode filtering (Never Watched, Favorites Only)
        /// Validates: Requirements 3.3, 3.4, 5.5, 5.6, 8.1, 9.1
        /// </summary>
        /// <param name="videos">The list of videos to filter</param>
        /// <returns>Filtered list of videos based on active mode</returns>
        public List<string> ApplyModeLogic(List<string> videos)
        {
            if (videos == null || videos.Count == 0)
            {
                return new List<string>();
            }

            // Both modes now apply black list filtering (not white list)
            // Black list is used for filtering, white list is only for display
            return ApplyBlackWhiteListFiltering(videos);
        }

        /// <summary>
        /// Applies filter options (formerly Group B modes) to the video list.
        /// This is called after primary mode filtering and exclusion settings.
        /// </summary>
        /// <param name="videos">The list of videos to filter</param>
        /// <returns>Filtered list of videos based on active filter options</returns>
        private List<string> ApplyFilterOptions(List<string> videos)
        {
            if (videos == null || videos.Count == 0)
            {
                return new List<string>();
            }

            FilterOptions activeFilters = modeManager.GetActiveFilters();

            // Early exit if no filters
            if (activeFilters == FilterOptions.None)
            {
                return videos;
            }

            // Apply filters in order of selectivity (most restrictive first)
            
            // 1. Custom Keywords (user-defined filter)
            if ((activeFilters & FilterOptions.CustomKeywords) == FilterOptions.CustomKeywords)
            {
                string customKeywords = modeManager.GetCustomKeywords();
                if (!string.IsNullOrEmpty(customKeywords))
                {
                    string[] keywords = customKeywords.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(k => k.Trim().ToLower())
                        .Where(k => !string.IsNullOrEmpty(k))
                        .ToArray();
                    
                    if (keywords.Length > 0)
                    {
                        videos = videos.Where(v =>
                        {
                            string fileName = Path.GetFileName(v).ToLower();
                            return keywords.Any(keyword => fileName.Contains(keyword));
                        }).ToList();
                        if (videos.Count == 0) return videos; // Early exit
                    }
                }
            }

            // 2. Super Resolution (keyword-based filter)
            if ((activeFilters & FilterOptions.SuperResolution) == FilterOptions.SuperResolution)
            {
                List<string> keywords = LoadSuperResolutionKeywords();
                videos = videos.Where(v =>
                {
                    string fileName = Path.GetFileName(v);
                    return ContainsKeyword(fileName, keywords);
                }).ToList();
                if (videos.Count == 0) return videos; // Early exit
            }

            // 3. Uncensored (keyword-based filter)
            if ((activeFilters & FilterOptions.Uncensored) == FilterOptions.Uncensored)
            {
                List<string> keywords = LoadUncensoredKeywords();
                videos = videos.Where(v =>
                {
                    string fileName = Path.GetFileName(v);
                    return ContainsKeyword(fileName, keywords);
                }).ToList();
                if (videos.Count == 0) return videos; // Early exit
            }

            // 4. Favorites Only (typically most restrictive)
            if ((activeFilters & FilterOptions.FavoritesOnly) == FilterOptions.FavoritesOnly)
            {
                videos = videos.Where(v => favoritesManager.IsFavorite(v)).ToList();
                if (videos.Count == 0) return videos; // Early exit
            }

            // 5. Never Watched (moderately restrictive)
            if ((activeFilters & FilterOptions.NeverWatched) == FilterOptions.NeverWatched)
            {
                videos = videos.Where(v => !historyManager.HasBeenWatched(v)).ToList();
                if (videos.Count == 0) return videos; // Early exit
            }

            // 6. Time-based sorting is handled in CalculatePriorityWeights
            // No filtering needed here, just return the videos

            return videos;
        }

        /// <summary>
        /// Applies black list and white list filtering to videos (Group A only).
        /// White list (stopWords): Exact match on folder names
        /// Black list (blackList): Contains match on folder names
        /// </summary>
        /// <param name="videos">The list of videos to filter</param>
        /// <returns>Filtered list of videos</returns>
        private List<string> ApplyBlackWhiteListFiltering(List<string> videos)
        {
            List<string> filteredVideos = new List<string>();

            foreach (string videoPath in videos)
            {
                if (ShouldIncludeVideo(videoPath))
                {
                    filteredVideos.Add(videoPath);
                }
            }

            return filteredVideos;
        }

        /// <summary>
        /// Determines if a video should be included based on black/white list filtering.
        /// </summary>
        /// <param name="videoPath">The full path to the video file</param>
        /// <returns>True if the video should be included, false otherwise</returns>
        private bool ShouldIncludeVideo(string videoPath)
        {
            try
            {
                DirectoryInfo? dir = new FileInfo(videoPath).Directory;
                if (dir == null) return false;

                // Get all enabled paths to determine root boundaries
                List<string> enabledPaths = pathManager.GetEnabledPaths();

                // Traverse up the directory tree
                while (dir != null)
                {
                    string name = dir.Name;
                    string nameUpper = name.ToUpper();

                    // Check if we've reached a root path boundary
                    bool isRootPath = enabledPaths.Any(p => 
                        dir.FullName.TrimEnd('\\').Equals(p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
                    
                    if (isRootPath)
                    {
                        // Reached root, include the video
                        return true;
                    }

                    // Check black list (contains match) - if matched, exclude video
                    // Black list is used for filtering in both File Mode and Folder Mode
                    if (blackWhiteListManager.IsInBlackList(name))
                    {
                        return false;
                    }

                    // Move to parent directory
                    dir = dir.Parent;
                }

                // If we've exhausted the tree without hitting black list, include the video
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking video {videoPath}: {ex.Message}");
                // On error, include the video (fail open)
                return true;
            }
        }

        /// <summary>
        /// Applies exclusion settings to filter out recently played videos.
        /// For Folder Mode: Applies MinVideosForExclusion threshold per folder.
        /// For Video Mode: Applies exclusion to all videos.
        /// Validates: Requirements 4.4, 4.5, 4.6, 8.3
        /// </summary>
        /// <param name="videos">The list of videos to filter</param>
        /// <returns>Filtered list of videos with recent videos excluded</returns>
        public List<string> ApplyExclusionSettings(List<string> videos)
        {
            if (videos == null || videos.Count == 0)
            {
                return new List<string>();
            }

            // Get primary mode and exclusion settings
            PrimaryMode primaryMode = modeManager.GetPrimaryMode();
            ExclusionSettings exclusionSettings = modeManager.GetExclusionSettings();

            // If no exclusion is configured, return all videos
            if (exclusionSettings.ExcludeRecentDays <= 0 && exclusionSettings.ExcludeRecentCount <= 0)
            {
                return videos;
            }

            // Get recent videos to exclude based on settings
            HashSet<string> recentVideos = GetRecentVideosToExclude(exclusionSettings);

            // Check if we're in Folder Mode with MinVideosForExclusion threshold
            bool isFolderMode = primaryMode == PrimaryMode.FolderMode;

            if (isFolderMode && exclusionSettings.MinVideosForExclusion > 0)
            {
                // Apply per-folder exclusion threshold
                return ApplyFolderThresholdExclusion(videos, recentVideos, exclusionSettings.MinVideosForExclusion);
            }
            else
            {
                // Apply exclusion to all videos
                return videos.Where(v => !recentVideos.Contains(v)).ToList();
            }
        }

        /// <summary>
        /// Gets the set of recent videos to exclude based on exclusion settings.
        /// Applies both days-based and count-based exclusion, using whichever is more restrictive.
        /// </summary>
        /// <param name="settings">The exclusion settings to apply</param>
        /// <returns>HashSet of video paths to exclude</returns>
        private HashSet<string> GetRecentVideosToExclude(ExclusionSettings settings)
        {
            HashSet<string> recentVideos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Get videos from recent days
            if (settings.ExcludeRecentDays > 0)
            {
                DateTime startDate = DateTime.Now.AddDays(-settings.ExcludeRecentDays);
                DateTime endDate = DateTime.Now;
                List<string> videosInDateRange = historyManager.GetVideosInDateRange(startDate, endDate);

                foreach (string video in videosInDateRange)
                {
                    recentVideos.Add(video);
                }
            }

            // Get videos from recent count
            if (settings.ExcludeRecentCount > 0)
            {
                List<string> recentCountVideos = historyManager.GetRecentVideos(settings.ExcludeRecentCount);

                foreach (string video in recentCountVideos)
                {
                    recentVideos.Add(video);
                }
            }

            return recentVideos;
        }

        /// <summary>
        /// Applies exclusion logic with per-folder threshold for Group A folder mode.
        /// If a folder has fewer videos than the threshold, exclusion is skipped for that folder.
        /// </summary>
        /// <param name="videos">The list of videos to filter</param>
        /// <param name="recentVideos">The set of recent videos to exclude</param>
        /// <param name="minVideosThreshold">The minimum number of videos required before applying exclusion</param>
        /// <returns>Filtered list of videos</returns>
        private List<string> ApplyFolderThresholdExclusion(List<string> videos, HashSet<string> recentVideos, int minVideosThreshold)
        {
            // Group videos by their parent folder
            Dictionary<string, List<string>> videosByFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string videoPath in videos)
            {
                try
                {
                    string? folderPath = Path.GetDirectoryName(videoPath);
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        if (!videosByFolder.ContainsKey(folderPath))
                        {
                            videosByFolder[folderPath] = new List<string>();
                        }
                        videosByFolder[folderPath].Add(videoPath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing video path {videoPath}: {ex.Message}");
                }
            }

            // Apply exclusion per folder based on threshold
            List<string> filteredVideos = new List<string>();

            foreach (var folderGroup in videosByFolder)
            {
                string folderPath = folderGroup.Key;
                List<string> folderVideos = folderGroup.Value;

                // If folder has fewer videos than threshold, skip exclusion for this folder
                if (folderVideos.Count < minVideosThreshold)
                {
                    // Include all videos from this folder (no exclusion)
                    filteredVideos.AddRange(folderVideos);
                }
                else
                {
                    // Apply exclusion to this folder
                    filteredVideos.AddRange(folderVideos.Where(v => !recentVideos.Contains(v)));
                }
            }

            return filteredVideos;
        }

        /// <summary>
        /// Calculates priority weights for videos based on time-based sorting filters.
        /// Uses exponential decay function to assign higher weights to newer/older videos.
        /// Validates: Requirements 5.1, 5.2, 7.1, 7.2, 7.3, 7.4
        /// </summary>
        /// <param name="videos">The list of video paths to calculate weights for</param>
        /// <returns>Dictionary mapping video paths to their priority weights</returns>
        public Dictionary<string, double> CalculatePriorityWeights(List<string> videos)
        {
            Dictionary<string, double> priorities = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            if (videos == null || videos.Count == 0)
            {
                return priorities;
            }

            // Get active sorting filter (handles conflict resolution)
            FilterOptions activeSorting = modeManager.GetActiveSortingFilter();

            // Check if a time-based sorting filter is active
            if (activeSorting != FilterOptions.NewestToOldest && activeSorting != FilterOptions.OldestToNewest)
            {
                // Return uniform weights for non-time-based modes
                foreach (string video in videos)
                {
                    priorities[video] = 1.0;
                }
                return priorities;
            }

            // Create VideoMetadata objects to get timestamps
            List<VideoMetadata> videoMetadataList = new List<VideoMetadata>();
            foreach (string videoPath in videos)
            {
                try
                {
                    VideoMetadata metadata = new VideoMetadata(videoPath);
                    videoMetadataList.Add(metadata);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error creating metadata for {videoPath}: {ex.Message}");
                    // Skip videos that can't be accessed
                }
            }

            if (videoMetadataList.Count == 0)
            {
                return priorities;
            }

            // Calculate max age for OldestToNewest mode
            DateTime now = DateTime.Now;
            double maxAgeInDays = 0;

            if (activeSorting == FilterOptions.OldestToNewest)
            {
                // Find the oldest video (maximum age)
                DateTime oldestTime = videoMetadataList.Min(v => v.EarliestTime);
                maxAgeInDays = (now - oldestTime).TotalDays;
            }

            // Exponential decay parameter
            double lambda = 0.1;

            // Calculate priority weight for each video
            foreach (VideoMetadata video in videoMetadataList)
            {
                double ageInDays = (now - video.EarliestTime).TotalDays;
                double weight;

                if (activeSorting == FilterOptions.NewestToOldest)
                {
                    // Newer videos get higher weights
                    // Weight decreases exponentially with age
                    weight = Math.Exp(-lambda * ageInDays);
                }
                else // FilterOptions.OldestToNewest
                {
                    // Older videos get higher weights
                    // Invert the age calculation
                    double invertedAge = maxAgeInDays - ageInDays;
                    weight = Math.Exp(-lambda * invertedAge);
                }

                priorities[video.FullPath] = weight;
            }

            return priorities;
        }

        /// <summary>
        /// Selects a video using weighted random selection based on priority weights.
        /// Uses cumulative weight distribution for proper weighted random selection.
        /// Validates: Requirements 7.5
        /// </summary>
        /// <param name="videoPriorities">Dictionary mapping video paths to their priority weights</param>
        /// <returns>Selected video path, or null if no videos available</returns>
        private string? SelectWithPriority(Dictionary<string, double> videoPriorities)
        {
            if (videoPriorities == null || videoPriorities.Count == 0)
            {
                return null;
            }

            // Build cumulative weight distribution
            List<string> videos = new List<string>();
            List<double> cumulativeWeights = new List<double>();
            double totalWeight = 0;

            foreach (var kvp in videoPriorities)
            {
                videos.Add(kvp.Key);
                totalWeight += kvp.Value;
                cumulativeWeights.Add(totalWeight);
            }

            // Handle edge case where all weights are zero
            if (totalWeight <= 0)
            {
                // Fall back to uniform random selection
                Random random = new Random();
                int index = random.Next(videos.Count);
                return videos[index];
            }

            // Select using cumulative weight distribution
            Random rng = new Random();
            double randomValue = rng.NextDouble() * totalWeight;

            // Binary search for the selected video
            for (int i = 0; i < cumulativeWeights.Count; i++)
            {
                if (randomValue <= cumulativeWeights[i])
                {
                    return videos[i];
                }
            }

            // Fallback (should not reach here due to floating point precision)
            return videos[videos.Count - 1];
        }

        /// <summary>
        /// Main video selection method that orchestrates the entire selection pipeline.
        /// New pipeline: Scan → Primary Mode Filter → Exclusion → Filter Options → Prioritize → Select
        /// Validates: Requirements 4.1, 7.5, 8.2, 9.2, 17.4
        /// </summary>
        /// <returns>Selected video path, or null if no videos available</returns>
        public string? SelectVideo()
        {
            // Phase 1: Scan all enabled paths
            List<string> videos = ScanEnabledPaths();

            if (videos == null || videos.Count == 0)
            {
                Console.WriteLine("Error: No videos found in enabled paths. Please enable at least one path with video files.");
                return null;
            }

            // Phase 2: Apply Primary Mode filtering (black/white lists)
            videos = ApplyModeLogic(videos);

            if (videos == null || videos.Count == 0)
            {
                Console.WriteLine("Error: No videos match the primary mode filters. Try changing the mode or adjusting black/white lists.");
                return null;
            }

            // Phase 3: Apply exclusion settings
            videos = ApplyExclusionSettings(videos);

            if (videos == null || videos.Count == 0)
            {
                Console.WriteLine("Error: All videos have been excluded by recent play settings. Try disabling exclusion or enabling more paths.");
                return null;
            }

            // Phase 4: Apply filter options (Never Watched, Favorites Only)
            videos = ApplyFilterOptions(videos);

            if (videos == null || videos.Count == 0)
            {
                FilterOptions activeFilters = modeManager.GetActiveFilters();
                
                if ((activeFilters & FilterOptions.NeverWatched) == FilterOptions.NeverWatched)
                {
                    Console.WriteLine("Error: No unwatched videos available. All videos have been watched. Try disabling the 'Never Watched' filter.");
                    return null;
                }
                
                if ((activeFilters & FilterOptions.FavoritesOnly) == FilterOptions.FavoritesOnly)
                {
                    Console.WriteLine("Error: No favorite videos available. Please add videos to your favorites list or disable the 'Favorites Only' filter.");
                    return null;
                }

                Console.WriteLine("Error: No videos match the current filter combination. Try adjusting your filter settings.");
                return null;
            }

            // Phase 5: Calculate priority weights (considers time-based sorting filters)
            Dictionary<string, double> priorities = CalculatePriorityWeights(videos);

            if (priorities == null || priorities.Count == 0)
            {
                Console.WriteLine("Error: Failed to calculate video priorities.");
                return null;
            }

            // Phase 6: Select video using weighted random selection
            string? selectedVideo = SelectWithPriority(priorities);

            if (selectedVideo == null)
            {
                Console.WriteLine("Error: Failed to select a video from the available pool.");
                return null;
            }

            return selectedVideo;
        }

    }
}
