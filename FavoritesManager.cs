using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CyberConcubineSelection
{
    /// <summary>
    /// Manages the favorites list and provides query/modification operations.
    /// Supports persistence to config/favorites.txt with case-insensitive path comparison.
    /// Maintains insertion order - newly added favorites appear at the end.
    /// </summary>
    public class FavoritesManager
    {
        private List<string> favoriteVideos;
        private readonly string configFilePath;
        private readonly StringComparer comparer;

        /// <summary>
        /// Creates a new FavoritesManager instance.
        /// </summary>
        /// <param name="configDirectory">The directory where config files are stored. Defaults to "config".</param>
        public FavoritesManager(string configDirectory = "config")
        {
            favoriteVideos = new List<string>();
            comparer = StringComparer.OrdinalIgnoreCase;
            configFilePath = Path.Combine(configDirectory, "favorites.txt");
        }

        /// <summary>
        /// Checks if a video is in the favorites list.
        /// </summary>
        /// <param name="videoPath">The full path to the video file.</param>
        /// <returns>True if the video is favorited, false otherwise.</returns>
        public bool IsFavorite(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath))
                return false;
            
            return favoriteVideos.Any(fav => comparer.Equals(fav, videoPath));
        }

        /// <summary>
        /// Adds a video to the favorites list.
        /// If the video is already in the list, it will be removed and re-added at the end.
        /// </summary>
        /// <param name="videoPath">The full path to the video file.</param>
        public void AddFavorite(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath))
                return;
            
            // Remove if already exists (to ensure it's added at the end)
            RemoveFavorite(videoPath);
            
            // Add to the end of the list
            favoriteVideos.Add(videoPath);
        }

        /// <summary>
        /// Removes a video from the favorites list.
        /// </summary>
        /// <param name="videoPath">The full path to the video file.</param>
        public void RemoveFavorite(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath))
                return;
            
            // Remove all matching entries (case-insensitive)
            favoriteVideos.RemoveAll(fav => comparer.Equals(fav, videoPath));
        }

        /// <summary>
        /// Toggles the favorite status of a video.
        /// If the video is favorited, it will be removed. If not favorited, it will be added.
        /// </summary>
        /// <param name="videoPath">The full path to the video file.</param>
        public void ToggleFavorite(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath))
                return;
            
            if (IsFavorite(videoPath))
            {
                RemoveFavorite(videoPath);
            }
            else
            {
                AddFavorite(videoPath);
            }
        }

        /// <summary>
        /// Gets all favorited video paths.
        /// </summary>
        /// <returns>List of all favorited video paths.</returns>
        public List<string> GetAllFavorites()
        {
            return new List<string>(favoriteVideos);
        }

        /// <summary>
        /// Gets only the favorited video paths that still exist on disk.
        /// </summary>
        /// <returns>List of favorited video paths that exist on disk.</returns>
        public List<string> GetExistingFavorites()
        {
            return favoriteVideos.Where(path => File.Exists(path)).ToList();
        }

        /// <summary>
        /// Loads favorites from config/favorites.txt.
        /// Creates the config directory if it doesn't exist.
        /// </summary>
        public void LoadFromConfig()
        {
            favoriteVideos.Clear();

            // Ensure config directory exists
            string? configDir = Path.GetDirectoryName(configFilePath);
            if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            // Load favorites if file exists
            if (File.Exists(configFilePath))
            {
                try
                {
                    string[] lines = File.ReadAllLines(configFilePath);
                    foreach (string line in lines)
                    {
                        string trimmedLine = line.Trim();
                        if (!string.IsNullOrWhiteSpace(trimmedLine))
                        {
                            favoriteVideos.Add(trimmedLine);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Log error but don't crash - continue with empty favorites
                    Console.WriteLine("Error loading favorites: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Saves favorites to config/favorites.txt.
        /// Creates the config directory if it doesn't exist.
        /// </summary>
        public void SaveToConfig()
        {
            try
            {
                // Ensure config directory exists
                string? configDir = Path.GetDirectoryName(configFilePath);
                if (!string.IsNullOrEmpty(configDir) && !Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                // Write all favorites to file (one per line)
                File.WriteAllLines(configFilePath, favoriteVideos);
            }
            catch (Exception ex)
            {
                // Log error but don't crash
                Console.WriteLine("Error saving favorites: " + ex.Message);
            }
        }
    }
}
