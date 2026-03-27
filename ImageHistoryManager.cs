using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cyber​​ConcubineSelection
{
    /// <summary>
    /// Manages image viewing history, including recording views and querying history.
    /// </summary>
    public class ImageHistoryManager
    {
        private readonly string historyFilePath;
        
        /// <summary>
        /// Event that is raised when a new image history entry is recorded.
        /// </summary>
        public event Action? OnHistoryRecorded;

        /// <summary>
        /// Initializes a new instance of the ImageHistoryManager class.
        /// </summary>
        /// <param name="configDirectory">The directory where config files are stored. Defaults to "config".</param>
        public ImageHistoryManager(string configDirectory = "config")
        {
            // 使用与视频相同的历史文件
            historyFilePath = Path.Combine(configDirectory, "History.log");
        }

        /// <summary>
        /// Records an image view event to the history log.
        /// </summary>
        /// <param name="folderName">The folder name containing the image.</param>
        /// <param name="fileName">The file name of the image.</param>
        /// <param name="type">The type of viewing: 0=冷宫, 1=宠幸, 2=重温, 3=回味</param>
        /// <param name="fullPath">The full path to the image file.</param>
        public void RecordView(string folderName, string fileName, int type, string fullPath)
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

                if (File.Exists(historyFilePath))
                {
                    var lines = File.ReadAllLines(historyFilePath).ToList();
                    // 查找同一图片的最后一条记录
                    var lastEntry = lines.Where(l => l.Contains($"|{fileName}|")).LastOrDefault();

                    int finalType = type;
                    if (lastEntry != null)
                    {
                        string[] p = lastEntry.Split('|');
                        int lastType = p.Length > 3 ? int.Parse(p[3]) : 0;

                        // 核心覆盖逻辑：
                        // 1. 如果当前是"宠幸"(1)，且上一条是"冷宫"(0)，则覆盖。
                        // 2. 如果当前是"宠幸"(1)，且上一条是"重温"(2)，则升级为"回味"(3)。
                        if (type == 1 && lastType == 0) { lines.Remove(lastEntry); finalType = 1; }
                        else if (type == 1 && lastType == 2) { lines.Remove(lastEntry); finalType = 3; }
                        else if (type == 2) { /* 重温不覆盖任何东西，直接添加 */ }
                    }

                    lines.Add($"{timestamp}|{folderName}|{fileName}|{finalType}|{fullPath}");
                    File.WriteAllLines(historyFilePath, lines);
                }
                else
                {
                    File.WriteAllText(historyFilePath, entry + Environment.NewLine);
                }
                
                // Notify listeners that history was recorded
                OnHistoryRecorded?.Invoke();
            }
            catch (Exception ex)
            {
                // Log error but don't crash the application
                Console.WriteLine($"Error recording image history: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets all history entries for a specific date.
        /// </summary>
        /// <param name="date">The date to query.</param>
        /// <returns>A list of image history entries for the specified date.</returns>
        public List<ImageHistoryEntry> GetHistoryForDate(DateTime date)
        {
            if (!File.Exists(historyFilePath))
            {
                return new List<ImageHistoryEntry>();
            }

            try
            {
                string dateString = date.ToString("yyyy-MM-dd");
                return File.ReadAllLines(historyFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line) && line.StartsWith(dateString))
                    .Select(ParseHistoryLine)
                    .Where(entry => entry != null)
                    .Cast<ImageHistoryEntry>()
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading image history for date: {ex.Message}");
                return new List<ImageHistoryEntry>();
            }
        }

        /// <summary>
        /// Gets the most recent N images from the history.
        /// </summary>
        /// <param name="count">The number of recent images to retrieve.</param>
        /// <returns>A list of full paths to the most recent images.</returns>
        public List<string> GetRecentImages(int count)
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
                    .Cast<ImageHistoryEntry>()
                    .Reverse()
                    .Take(count)
                    .Select(entry => entry.FullPath)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting recent images: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Gets all images viewed within a specific date range.
        /// </summary>
        /// <param name="start">The start date (inclusive).</param>
        /// <param name="end">The end date (inclusive).</param>
        /// <returns>A list of full paths to images viewed in the date range.</returns>
        public List<string> GetImagesInDateRange(DateTime start, DateTime end)
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
                    .Cast<ImageHistoryEntry>()
                    .Select(entry => entry.FullPath)
                    .Distinct()
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting images in date range: {ex.Message}");
                return new List<string>();
            }
        }

        /// <summary>
        /// Checks if an image has been viewed before.
        /// </summary>
        /// <param name="imagePath">The full path to the image file.</param>
        /// <returns>True if the image has been viewed, false otherwise.</returns>
        public bool HasBeenViewed(string imagePath)
        {
            if (!File.Exists(historyFilePath))
            {
                return false;
            }

            try
            {
                return File.ReadAllLines(historyFilePath)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Any(line => line.EndsWith("|" + imagePath));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking if image has been viewed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Parses a history log line into an ImageHistoryEntry object.
        /// </summary>
        /// <param name="line">The line to parse.</param>
        /// <returns>An ImageHistoryEntry object, or null if parsing fails.</returns>
        private ImageHistoryEntry? ParseHistoryLine(string line)
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

                return new ImageHistoryEntry(
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
