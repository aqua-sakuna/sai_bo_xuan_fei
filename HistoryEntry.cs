using System;

namespace Cyber​​ConcubineSelection
{
    /// <summary>
    /// Represents a single entry in the video playback history.
    /// </summary>
    public class HistoryEntry
    {
        /// <summary>
        /// The timestamp when the video was played.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// The folder name containing the video.
        /// </summary>
        public string FolderName { get; set; }

        /// <summary>
        /// The file name of the video.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// The type of playback: 0=冷宫, 1=宠幸, 2=重温, 3=回味
        /// </summary>
        public int Type { get; set; }

        /// <summary>
        /// The full path to the video file.
        /// </summary>
        public string FullPath { get; set; }

        public HistoryEntry()
        {
            FolderName = string.Empty;
            FileName = string.Empty;
            FullPath = string.Empty;
        }

        public HistoryEntry(DateTime timestamp, string folderName, string fileName, int type, string fullPath)
        {
            Timestamp = timestamp;
            FolderName = folderName ?? string.Empty;
            FileName = fileName ?? string.Empty;
            Type = type;
            FullPath = fullPath ?? string.Empty;
        }
    }
}
