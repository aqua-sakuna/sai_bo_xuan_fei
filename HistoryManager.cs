using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cyber​​ConcubineSelection
{
    /// <summary>
    /// Manages video playback history, including recording plays and querying history.
    /// </summary>
    public class HistoryManager
    {
        private readonly string historyFilePath;
        
        /// <summary>
        /// Event that is raised when a new history entry is recorded.
        /// </summary>
        public event Action? OnHistoryRecorded;

        /// <summary>
        /// Initializes a new instance of the HistoryManager class.
        /// </summary>
        /// <param name="configDirectory">The directory where config files are stored. Defaults to "config".</param>
        public HistoryManager(string configDirectory = "config")
        {
            historyFilePath = Path.Combine(configDirectory, "History.log");
        }

        /// <summary>
        /// Records a video play event to the history log.
        /// </summary>
        /// <param name="folderName">The folder name containing the video.</param>
        /// <param name="fileName">The file name of the video.</param>
        /// <param name="type">The type of playback: 0=冷宫, 1=宠幸, 2=重温, 3=回味</param>
        /// <param name="fullPath">The full path to the video file.</param>
        public void RecordPlay(string folderName, string fileName, int type, string fullPath)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string entry = $"{timestamp}|{folderName}|{fileName}|{type}|{fullPath}";

            try
            {
                // Ensure the directory exists
                string? directory = Path.GetDirectoryName(historyFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Append the entry to the history file
                File.AppendAllText(historyFilePath, entry + Environment.NewLine);
                
                // Notify listeners that history was recorded
                OnHistoryRecorded?.Invoke();
            }
            catch (Exception ex)
            {
                // Log error but don't crash the application
                Console.WriteLine($"Error recording history: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all history entries for a specific date.
        /// </summary>
        /// <param name="date">The date to query.</param>
        /// <returns>A list of history entries for the specified date.</returns>
        public List<HistoryEntry> GetHistoryForDate(DateTime date)
        {
            if (!File.Exists(historyFilePath))
            {
                return new List<HistoryEntry>();
            }

            try
            {
                string dateString = date.ToString("yyyy-MM-dd");
                return File.ReadAllLines(historyFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line) && line.StartsWith(dateString))
                    .Select(ParseHistoryLine)
                    .Where(entry => entry != null)
                    .Cast<HistoryEntry>()
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading history for date: {ex.Message}");
                return new List<HistoryEntry>();
            }
        }

        /// <summary>
        /// Gets the most recent N videos from the history.
        /// </summary>
        /// <param name="count">The number of recent videos to retrieve.</param>
        /// <returns>A list of full paths to the most recent videos.</returns>
        public List<string> GetRecentVideos(int count)
        {
            if (!File.Exists(historyFilePath))
            {
                return new List<string>();
            }

            try
            {
                return File.ReadAllLines(historyFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(ParseHistoryLine)
                    .Where(entry => entry != null)
                    .Cast<HistoryEntry>()
                    .Reverse()
                    .Take(count)
                    .Select(entry => entry.FullPath)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting recent videos: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Gets all videos played within a specific date range.
        /// </summary>
        /// <param name="start">The start date (inclusive).</param>
        /// <param name="end">The end date (inclusive).</param>
        /// <returns>A list of full paths to videos played in the date range.</returns>
        public List<string> GetVideosInDateRange(DateTime start, DateTime end)
        {
            if (!File.Exists(historyFilePath))
            {
                return new List<string>();
            }

            try
            {
                return File.ReadAllLines(historyFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(ParseHistoryLine)
                    .Where(entry => entry != null && entry.Timestamp >= start && entry.Timestamp <= end.AddDays(1).AddSeconds(-1))
                    .Cast<HistoryEntry>()
                    .Select(entry => entry.FullPath)
                    .Distinct()
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting videos in date range: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Checks if a video has been watched before.
        /// </summary>
        /// <param name="videoPath">The full path to the video file.</param>
        /// <returns>True if the video has been watched, false otherwise.</returns>
        public bool HasBeenWatched(string videoPath)
        {
            if (!File.Exists(historyFilePath))
            {
                return false;
            }

            try
            {
                return File.ReadAllLines(historyFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Any(line => line.EndsWith("|" + videoPath));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking if video has been watched: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Parses a history log line into a HistoryEntry object.
        /// </summary>
        /// <param name="line">The line to parse.</param>
        /// <returns>A HistoryEntry object, or null if parsing fails.</returns>
        private HistoryEntry? ParseHistoryLine(string line)
        {
            try
            {
                string[] parts = line.Split('|');
                if (parts.Length < 5)
                {
                    return null;
                }

                DateTime timestamp;
                if (!DateTime.TryParse(parts[0], out timestamp))
                {
                    return null;
                }

                int type;
                if (!int.TryParse(parts[3], out type))
                {
                    return null;
                }

                return new HistoryEntry(
                    timestamp,
                    parts[1],
                    parts[2],
                    type,
                    parts[4]
                );
            }
            catch
            {
                return null;
            }
        }
    }
}
