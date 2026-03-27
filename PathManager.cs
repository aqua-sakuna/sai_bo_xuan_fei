using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cyber​​ConcubineSelection
{
    /// <summary>
    /// Manages the 6 path slots and their persistence to config/paths.txt.
    /// Handles loading, saving, and querying path configurations.
    /// Validates: Requirements 1.1, 1.2, 1.3, 1.4, 1.5, 16.2, 16.3, 16.5
    /// </summary>
    public class PathManager
    {
        private const int PATH_SLOT_COUNT = 6;
        private readonly PathSlot[] pathSlots;
        private readonly string configFilePath;

        /// <summary>
        /// Creates a new PathManager instance.
        /// Initializes 6 empty path slots.
        /// </summary>
        public PathManager()
        {
            pathSlots = new PathSlot[PATH_SLOT_COUNT];
            for (int i = 0; i < PATH_SLOT_COUNT; i++)
            {
                pathSlots[i] = new PathSlot(i);
            }

            string configDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
            configFilePath = Path.Combine(configDirectory, "paths.txt");
        }

        /// <summary>
        /// Gets all path slots (all 6 slots).
        /// Validates: Requirement 1.1
        /// </summary>
        /// <returns>Array of all 6 path slots</returns>
        public PathSlot[] GetAllPaths()
        {
            return pathSlots;
        }

        /// <summary>
        /// Sets the path and enabled state for a specific slot.
        /// Validates: Requirement 1.3
        /// </summary>
        /// <param name="index">The slot index (0-5)</param>
        /// <param name="path">The directory path to set</param>
        /// <param name="enabled">Whether the slot should be enabled</param>
        public void SetPath(int index, string path, bool enabled)
        {
            if (index < 0 || index >= PATH_SLOT_COUNT)
            {
                throw new ArgumentOutOfRangeException(nameof(index), 
                    $"Index must be between 0 and {PATH_SLOT_COUNT - 1}");
            }

            pathSlots[index].DirectoryPath = path ?? string.Empty;
            pathSlots[index].IsEnabled = enabled;
        }

        /// <summary>
        /// Gets a list of all enabled path directories.
        /// Only returns paths that are enabled and valid (non-empty).
        /// Validates: Requirement 2.6
        /// </summary>
        /// <returns>List of enabled directory paths</returns>
        public List<string> GetEnabledPaths()
        {
            return pathSlots
                .Where(slot => slot.IsEnabled && !string.IsNullOrEmpty(slot.DirectoryPath))
                .Select(slot => slot.DirectoryPath)
                .ToList();
        }

        /// <summary>
        /// Loads path configuration from config/paths.txt.
        /// If the file doesn't exist, initializes all slots as empty and disabled.
        /// Also handles migration from old last_path.txt format.
        /// Validates: Requirements 1.2, 16.3, 16.5
        /// </summary>
        public void LoadFromConfig()
        {
            // Ensure config directory exists
            string? configDirectory = Path.GetDirectoryName(configFilePath);
            if (configDirectory != null && !Directory.Exists(configDirectory))
            {
                Directory.CreateDirectory(configDirectory);
            }

            // If paths.txt doesn't exist, try to migrate from last_path.txt
            if (!File.Exists(configFilePath))
            {
                MigrateFromLastPath();
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(configFilePath);

                // Parse each line (expecting exactly 6 lines)
                for (int i = 0; i < PATH_SLOT_COUNT && i < lines.Length; i++)
                {
                    ParsePathLine(lines[i], i);
                }

                // If file has fewer than 6 lines, remaining slots stay empty/disabled
            }
            catch (Exception ex)
            {
                // Log error but don't crash - use default empty slots
                Console.WriteLine($"Error loading paths configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves all path slots to config/paths.txt.
        /// Validates: Requirements 1.3, 16.4
        /// </summary>
        public void SaveToConfig()
        {
            try
            {
                // Ensure config directory exists
                string? configDirectory = Path.GetDirectoryName(configFilePath);
                if (configDirectory != null && !Directory.Exists(configDirectory))
                {
                    Directory.CreateDirectory(configDirectory);
                }

                // Build lines in format: [Enabled/Disabled]|<path>
                List<string> lines = new List<string>();
                foreach (var slot in pathSlots)
                {
                    string enabledText = slot.IsEnabled ? "Enabled" : "Disabled";
                    string line = $"[{enabledText}]|{slot.DirectoryPath}";
                    lines.Add(line);
                }

                // Write all lines to file
                File.WriteAllLines(configFilePath, lines);
            }
            catch (Exception ex)
            {
                // Log error but don't crash
                Console.WriteLine($"Error saving paths configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Parses a single line from paths.txt and updates the corresponding slot.
        /// Format: [Enabled/Disabled]|<path>
        /// </summary>
        /// <param name="line">The line to parse</param>
        /// <param name="index">The slot index to update</param>
        private void ParsePathLine(string line, int index)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            // Split on pipe character
            string[] parts = line.Split('|');
            if (parts.Length != 2)
            {
                return;
            }

            // Parse enabled state
            string enabledPart = parts[0].Trim();
            bool isEnabled = enabledPart.Contains("Enabled");

            // Parse path
            string path = parts[1].Trim();

            // Update slot
            pathSlots[index].IsEnabled = isEnabled;
            pathSlots[index].DirectoryPath = path;
        }

        /// <summary>
        /// Migrates from old last_path.txt format (single path) to new paths.txt format (6 slots).
        /// If last_path.txt exists, puts that path in the first slot as enabled.
        /// Otherwise, initializes all slots as empty and disabled.
        /// Validates: Requirement 16.5
        /// </summary>
        private void MigrateFromLastPath()
        {
            string? configDirectory = Path.GetDirectoryName(configFilePath);
            if (configDirectory == null)
            {
                return;
            }

            string lastPathFile = Path.Combine(configDirectory, "last_path.txt");

            if (File.Exists(lastPathFile))
            {
                try
                {
                    // Read the old single path
                    string oldPath = File.ReadAllText(lastPathFile).Trim();

                    // Put it in the first slot as enabled
                    if (!string.IsNullOrEmpty(oldPath))
                    {
                        pathSlots[0].DirectoryPath = oldPath;
                        pathSlots[0].IsEnabled = true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error migrating from last_path.txt: {ex.Message}");
                }
            }

            // Save the new format (whether we migrated or not)
            SaveToConfig();
        }
    }
}
