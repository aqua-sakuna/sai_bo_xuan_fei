using System;
using System.IO;

namespace Cyber​​ConcubineSelection
{
    /// <summary>
    /// Handles migration of configuration files from application root to config/ and audio/ directories.
    /// Validates: Requirements 13.1, 13.11, 13.12
    /// </summary>
    public class ConfigMigration
    {
        private readonly string configDirectory;
        private readonly string audioDirectory;
        private readonly string applicationRoot;

        public ConfigMigration()
        {
            applicationRoot = AppDomain.CurrentDomain.BaseDirectory;
            configDirectory = Path.Combine(applicationRoot, "config");
            audioDirectory = Path.Combine(applicationRoot, "audio");
        }

        /// <summary>
        /// Migrates old configuration files from application root to config/ and audio/ directories.
        /// Creates config/ and audio/ directories if they don't exist.
        /// </summary>
        public void MigrateOldConfig()
        {
            // Create config/ directory if it doesn't exist (Requirement 13.1)
            if (!Directory.Exists(configDirectory))
            {
                try
                {
                    Directory.CreateDirectory(configDirectory);
                }
                catch (Exception ex)
                {
                    // Log error but don't crash the application
                    Console.WriteLine($"Failed to create config directory: {ex.Message}");
                }
            }
            
            // Create audio/ directory if it doesn't exist
            if (!Directory.Exists(audioDirectory))
            {
                try
                {
                    Directory.CreateDirectory(audioDirectory);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to create audio directory: {ex.Message}");
                }
            }

            // Move configuration files from root to config/ (Requirements 13.11, 13.12)
            MoveToConfig("last_path.txt");
            MoveToConfig("History.log");
            MoveToConfig("black_list.txt");
            MoveToConfig("stop_words.txt");
            MoveToConfig("exclude_count.txt");
            
            // Move audio-related files from root to audio/
            MoveToAudio("mute.txt");
            MoveToAudio("mute.png");
            MoveToAudio("unmute.png");
        }

        /// <summary>
        /// Moves a file from application root to config/ directory if it exists.
        /// Handles errors gracefully: tries move, falls back to copy, logs errors.
        /// </summary>
        /// <param name="fileName">Name of the file to move</param>
        private void MoveToConfig(string fileName)
        {
            MoveIfExists(fileName, configDirectory);
        }
        
        /// <summary>
        /// Moves a file from application root to audio/ directory if it exists.
        /// </summary>
        /// <param name="fileName">Name of the file to move</param>
        private void MoveToAudio(string fileName)
        {
            MoveIfExists(fileName, audioDirectory);
        }

        /// <summary>
        /// Moves a file from application root to target directory if it exists.
        /// Handles errors gracefully: tries move, falls back to copy, logs errors.
        /// </summary>
        /// <param name="fileName">Name of the file to move</param>
        /// <param name="targetDirectory">Target directory path</param>
        private void MoveIfExists(string fileName, string targetDirectory)
        {
            string oldPath = Path.Combine(applicationRoot, fileName);
            string newPath = Path.Combine(targetDirectory, fileName);

            // Skip if old file doesn't exist
            if (!File.Exists(oldPath))
            {
                return;
            }

            // Skip if new file already exists (prefer existing target file)
            if (File.Exists(newPath))
            {
                return;
            }

            try
            {
                // Try to move the file
                File.Move(oldPath, newPath);
            }
            catch (Exception moveEx)
            {
                // If move fails, try to copy instead
                try
                {
                    File.Copy(oldPath, newPath);
                    // Try to delete the old file after successful copy
                    try
                    {
                        File.Delete(oldPath);
                    }
                    catch
                    {
                        // If delete fails, leave the old file in place
                        Console.WriteLine($"Warning: Could not delete old file {oldPath} after copying");
                    }
                }
                catch (Exception copyEx)
                {
                    // Log error but don't crash
                    Console.WriteLine($"Failed to migrate {fileName}: Move error: {moveEx.Message}, Copy error: {copyEx.Message}");
                }
            }
        }
    }
}
