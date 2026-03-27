using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cyber​​ConcubineSelection
{
    /// <summary>
    /// Handles image selection logic with mode filtering and exclusion settings.
    /// Similar to VideoSelector but for images.
    /// </summary>
    public class ImageSelector
    {
        private readonly PathManager pathManager;
        private readonly ModeManager modeManager;
        private readonly ImageHistoryManager imageHistoryManager;
        private readonly FavoritesManager favoritesManager;
        private readonly BlackWhiteListManager blackWhiteListManager;

        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };

        public ImageSelector(
            PathManager pathManager,
            ModeManager modeManager,
            ImageHistoryManager imageHistoryManager,
            FavoritesManager favoritesManager,
            BlackWhiteListManager blackWhiteListManager)
        {
            this.pathManager = pathManager ?? throw new ArgumentNullException(nameof(pathManager));
            this.modeManager = modeManager ?? throw new ArgumentNullException(nameof(modeManager));
            this.imageHistoryManager = imageHistoryManager ?? throw new ArgumentNullException(nameof(imageHistoryManager));
            this.favoritesManager = favoritesManager ?? throw new ArgumentNullException(nameof(favoritesManager));
            this.blackWhiteListManager = blackWhiteListManager ?? throw new ArgumentNullException(nameof(blackWhiteListManager));
        }

        /// <summary>
        /// Selects an image using the configured mode and filter settings.
        /// </summary>
        public string? SelectImage()
        {
            // Phase 1: Scan all enabled paths
            List<string> images = ScanEnabledPaths();

            if (images == null || images.Count == 0)
            {
                Console.WriteLine("Error: No images found in enabled paths.");
                return null;
            }

            // Phase 2: Apply Primary Mode filtering (black list)
            images = ApplyModeLogic(images);

            if (images == null || images.Count == 0)
            {
                Console.WriteLine("Error: No images match the primary mode filters.");
                return null;
            }

            // Phase 3: Apply exclusion settings
            ExclusionSettings exclusionSettings = modeManager.GetExclusionSettings();
            if (exclusionSettings.ExcludeRecentDays > 0 || exclusionSettings.ExcludeRecentCount > 0)
            {
                images = ApplyExclusionSettings(images, exclusionSettings);

                if (images == null || images.Count == 0)
                {
                    Console.WriteLine("Error: All images have been excluded by recent view settings.");
                    return null;
                }
            }

            // Phase 4: Apply filter options (Never Watched, Favorites Only)
            images = ApplyFilterOptions(images);

            if (images == null || images.Count == 0)
            {
                FilterOptions activeFilters = modeManager.GetActiveFilters();
                
                if ((activeFilters & FilterOptions.NeverWatched) == FilterOptions.NeverWatched)
                {
                    Console.WriteLine("Error: No unwatched images available. Try disabling the 'Never Watched' filter.");
                    return null;
                }
                
                if ((activeFilters & FilterOptions.FavoritesOnly) == FilterOptions.FavoritesOnly)
                {
                    Console.WriteLine("Error: No favorite images available. Try disabling the 'Favorites Only' filter.");
                    return null;
                }

                Console.WriteLine("Error: No images match the current filter combination.");
                return null;
            }

            // Phase 5: Calculate priority weights (time-based sorting)
            Dictionary<string, double> imagePriorities = CalculatePriorityWeights(images);

            // Phase 6: Weighted random selection
            return SelectWithPriority(imagePriorities);
        }

        /// <summary>
        /// Scans all enabled paths for image files.
        /// </summary>
        private List<string> ScanEnabledPaths()
        {
            List<string> allImages = new List<string>();
            List<string> enabledPaths = pathManager.GetEnabledPaths();

            foreach (string path in enabledPaths)
            {
                if (Directory.Exists(path))
                {
                    HashSet<string> imageCollection = new HashSet<string>();
                    ScanDirectory(path, imageCollection);
                    allImages.AddRange(imageCollection);
                }
            }

            return allImages;
        }

        /// <summary>
        /// Recursively scans a directory for image files, skipping system and hidden directories.
        /// </summary>
        private void ScanDirectory(string directoryPath, HashSet<string> imageCollection)
        {
            try
            {
                // Scan current directory for images
                string[] files = Directory.GetFiles(directoryPath);
                foreach (string file in files)
                {
                    string extension = Path.GetExtension(file).ToLower();
                    if (ImageExtensions.Contains(extension))
                    {
                        imageCollection.Add(file);
                    }
                }

                // Recursively scan subdirectories
                string[] subdirectories = Directory.GetDirectories(directoryPath);
                foreach (string subdirectory in subdirectories)
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(subdirectory);
                    
                    // Skip hidden and system directories
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

                    ScanDirectory(subdirectory, imageCollection);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we don't have permission to access
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning directory {directoryPath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies mode-specific filtering logic (black/white lists).
        /// </summary>
        private List<string> ApplyModeLogic(List<string> images)
        {
            if (images == null || images.Count == 0)
            {
                return new List<string>();
            }

            // Both modes now apply black list filtering (not white list)
            // Black list is used for filtering, white list is only for display
            return ApplyBlackWhiteListFiltering(images);
        }

        /// <summary>
        /// Applies black list and white list filtering based on folder names.
        /// </summary>
        private List<string> ApplyBlackWhiteListFiltering(List<string> images)
        {
            string customKeywords = modeManager.GetCustomKeywords();

            // Parse custom keywords
            List<string> customKeywordList = string.IsNullOrWhiteSpace(customKeywords)
                ? new List<string>()
                : customKeywords.Split(',').Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k)).ToList();

            List<string> filtered = new List<string>();

            foreach (string image in images)
            {
                if (ShouldIncludeImage(image, customKeywordList))
                {
                    filtered.Add(image);
                }
            }

            return filtered;
        }

        /// <summary>
        /// Determines if an image should be included based on black list filtering.
        /// Traverses up the directory tree to check all parent folders.
        /// </summary>
        private bool ShouldIncludeImage(string imagePath, List<string> customKeywords)
        {
            try
            {
                DirectoryInfo? dir = new FileInfo(imagePath).Directory;
                if (dir == null) return false;

                // Get all enabled paths to determine root boundaries
                List<string> enabledPaths = pathManager.GetEnabledPaths();

                // Traverse up the directory tree
                while (dir != null)
                {
                    string name = dir.Name;

                    // Check if we've reached a root path boundary
                    bool isRootPath = enabledPaths.Any(p => 
                        dir.FullName.TrimEnd('\\').Equals(p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
                    
                    if (isRootPath)
                    {
                        // Reached root, include the image
                        return true;
                    }

                    // Check black list (contains match) with custom keywords - if matched, exclude image
                    // Black list is used for filtering in both File Mode and Folder Mode
                    if (blackWhiteListManager.IsInBlackList(name, customKeywords))
                    {
                        return false;
                    }

                    // Move to parent directory
                    dir = dir.Parent;
                }

                // If we've exhausted the tree without hitting black list, include the image
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking image {imagePath}: {ex.Message}");
                // On error, include the image (fail open)
                return true;
            }
        }

        /// <summary>
        /// Applies filter options (Never Watched, Favorites Only, Exclusion Settings).
        /// </summary>
        private List<string> ApplyFilterOptions(List<string> images)
        {
            if (images == null || images.Count == 0)
            {
                return new List<string>();
            }

            FilterOptions activeFilters = modeManager.GetActiveFilters();

            // Apply Never Watched filter
            if ((activeFilters & FilterOptions.NeverWatched) == FilterOptions.NeverWatched)
            {
                images = images.Where(img => !imageHistoryManager.HasBeenViewed(img)).ToList();
            }

            // Apply Favorites Only filter
            if ((activeFilters & FilterOptions.FavoritesOnly) == FilterOptions.FavoritesOnly)
            {
                images = images.Where(img => favoritesManager.IsFavorite(img)).ToList();
            }

            return images;
        }

        /// <summary>
        /// Applies exclusion settings to filter out recently viewed images.
        /// </summary>
        private List<string> ApplyExclusionSettings(List<string> images, ExclusionSettings settings)
        {
            if (images == null || images.Count == 0)
            {
                return new List<string>();
            }

            // If no exclusion is configured, return all images
            if (settings.ExcludeRecentDays <= 0 && settings.ExcludeRecentCount <= 0)
            {
                return images;
            }

            // Get recent images to exclude based on settings
            HashSet<string> recentImages = GetRecentImagesToExclude(settings);

            // Get primary mode to check if we're in Folder Mode
            PrimaryMode primaryMode = modeManager.GetPrimaryMode();
            bool isFolderMode = primaryMode == PrimaryMode.FolderMode;

            if (isFolderMode && settings.MinVideosForExclusion > 0)
            {
                // Apply per-folder exclusion threshold
                return ApplyFolderThresholdExclusion(images, recentImages, settings.MinVideosForExclusion);
            }
            else
            {
                // Apply exclusion to all images
                return images.Where(img => !recentImages.Contains(img)).ToList();
            }
        }

        /// <summary>
        /// Gets the set of recent images to exclude based on exclusion settings.
        /// Applies both days-based and count-based exclusion, using whichever is more restrictive.
        /// </summary>
        private HashSet<string> GetRecentImagesToExclude(ExclusionSettings settings)
        {
            HashSet<string> recentImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Get images from recent days
            if (settings.ExcludeRecentDays > 0)
            {
                DateTime startDate = DateTime.Now.AddDays(-settings.ExcludeRecentDays);
                DateTime endDate = DateTime.Now;
                List<string> imagesInDateRange = imageHistoryManager.GetImagesInDateRange(startDate, endDate);

                foreach (string image in imagesInDateRange)
                {
                    recentImages.Add(image);
                }
            }

            // Get images from recent count
            if (settings.ExcludeRecentCount > 0)
            {
                List<string> recentCountImages = imageHistoryManager.GetRecentImages(settings.ExcludeRecentCount);

                foreach (string image in recentCountImages)
                {
                    recentImages.Add(image);
                }
            }

            return recentImages;
        }

        /// <summary>
        /// Applies exclusion logic with per-folder threshold for Folder Mode.
        /// If a folder has fewer images than the threshold, exclusion is skipped for that folder.
        /// </summary>
        private List<string> ApplyFolderThresholdExclusion(List<string> images, HashSet<string> recentImages, int minImagesThreshold)
        {
            // Group images by their parent folder
            Dictionary<string, List<string>> imagesByFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (string imagePath in images)
            {
                try
                {
                    string? folderPath = Path.GetDirectoryName(imagePath);
                    if (!string.IsNullOrEmpty(folderPath))
                    {
                        if (!imagesByFolder.ContainsKey(folderPath))
                        {
                            imagesByFolder[folderPath] = new List<string>();
                        }
                        imagesByFolder[folderPath].Add(imagePath);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error processing image path {imagePath}: {ex.Message}");
                }
            }

            // Apply exclusion per folder based on threshold
            List<string> filteredImages = new List<string>();

            foreach (var folderGroup in imagesByFolder)
            {
                List<string> folderImages = folderGroup.Value;

                // If folder has fewer images than threshold, skip exclusion for this folder
                if (folderImages.Count < minImagesThreshold)
                {
                    // Include all images from this folder (no exclusion)
                    filteredImages.AddRange(folderImages);
                }
                else
                {
                    // Apply exclusion to this folder
                    filteredImages.AddRange(folderImages.Where(img => !recentImages.Contains(img)));
                }
            }

            return filteredImages;
        }

        /// <summary>
        /// Calculates priority weights for images based on time-based sorting filters.
        /// Supports NewestToOldest and OldestToNewest sorting.
        /// </summary>
        private Dictionary<string, double> CalculatePriorityWeights(List<string> images)
        {
            Dictionary<string, double> priorities = new Dictionary<string, double>();

            if (images == null || images.Count == 0)
            {
                return priorities;
            }

            // Get active sorting filter (handles conflict resolution)
            FilterOptions activeSorting = modeManager.GetActiveSortingFilter();

            // Check if a time-based sorting filter is active
            if (activeSorting != FilterOptions.NewestToOldest && activeSorting != FilterOptions.OldestToNewest)
            {
                // Return uniform weights for non-time-based modes
                foreach (string image in images)
                {
                    priorities[image] = 1.0;
                }
                return priorities;
            }

            // Get file modification times and calculate ages
            DateTime now = DateTime.Now;
            double maxAgeInDays = 0;

            List<(string path, DateTime modTime, double age)> imageData = new List<(string, DateTime, double)>();
            foreach (string image in images)
            {
                try
                {
                    FileInfo fileInfo = new FileInfo(image);
                    DateTime modTime = fileInfo.LastWriteTime;
                    double ageInDays = (now - modTime).TotalDays;
                    imageData.Add((image, modTime, ageInDays));

                    if (ageInDays > maxAgeInDays)
                    {
                        maxAgeInDays = ageInDays;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not get file time for {image}: {ex.Message}");
                    // Default to equal weight for files with errors
                    priorities[image] = 1.0;
                }
            }

            // Exponential decay parameter
            double lambda = 0.1;

            // Calculate priority weight for each image
            foreach (var (path, modTime, age) in imageData)
            {
                double weight;

                if (activeSorting == FilterOptions.NewestToOldest)
                {
                    // Newer images get higher weights
                    // Weight decreases exponentially with age
                    weight = Math.Exp(-lambda * age);
                }
                else // FilterOptions.OldestToNewest
                {
                    // Older images get higher weights
                    // Invert the age calculation
                    double invertedAge = maxAgeInDays - age;
                    weight = Math.Exp(-lambda * invertedAge);
                }

                priorities[path] = weight;
            }

            return priorities;
        }

        /// <summary>
        /// Selects an image using weighted random selection based on priority weights.
        /// Uses cumulative weight distribution for proper weighted random selection.
        /// </summary>
        private string? SelectWithPriority(Dictionary<string, double> imagePriorities)
        {
            if (imagePriorities == null || imagePriorities.Count == 0)
            {
                return null;
            }

            // Build cumulative weight distribution
            List<string> images = new List<string>();
            List<double> cumulativeWeights = new List<double>();
            double totalWeight = 0;

            foreach (var kvp in imagePriorities)
            {
                images.Add(kvp.Key);
                totalWeight += kvp.Value;
                cumulativeWeights.Add(totalWeight);
            }

            // Handle edge case where all weights are zero
            if (totalWeight <= 0)
            {
                // Fall back to uniform random selection
                Random random = new Random(Guid.NewGuid().GetHashCode());
                int index = random.Next(images.Count);
                return images[index];
            }

            // Weighted random selection
            Random rng = new Random(Guid.NewGuid().GetHashCode());
            double randomValue = rng.NextDouble() * totalWeight;

            // Binary search for the selected image
            int selectedIndex = cumulativeWeights.BinarySearch(randomValue);
            if (selectedIndex < 0)
            {
                // BinarySearch returns bitwise complement of the next larger element
                selectedIndex = ~selectedIndex;
            }

            // Ensure index is within bounds
            if (selectedIndex >= images.Count)
            {
                selectedIndex = images.Count - 1;
            }

            return images[selectedIndex];
        }
    }
}
