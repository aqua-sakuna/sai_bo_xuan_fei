using System;

namespace Cyber​​ConcubineSelection
{
    /// <summary>
    /// Represents a single entry in the image viewing history.
    /// </summary>
    public class ImageHistoryEntry
    {
        /// <summary>
        /// The timestamp when the image was viewed.
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// The folder name containing the image.
        /// </summary>
        public string FolderName { get; set; }

        /// <summary>
        /// The file name of the image.
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        /// The type of viewing: 0=冷宫, 1=宠幸, 2=重温, 3=回味
        /// </summary>
        public int Type { get; set; }

        /// <summary>
        /// The full path to the image file.
        /// </summary>
        public string FullPath { get; set; }

        public ImageHistoryEntry()
        {
            FolderName = string.Empty;
            FileName = string.Empty;
            FullPath = string.Empty;
        }

        public ImageHistoryEntry(DateTime timestamp, string folderName, string fileName, int type, string fullPath)
        {
            Timestamp = timestamp;
            FolderName = folderName ?? string.Empty;
            FileName = fileName ?? string.Empty;
            Type = type;
            FullPath = fullPath ?? string.Empty;
        }
    }
}
