using System;
using System.IO;

namespace CyberConcubineSelection
{
    /// <summary>
    /// Represents metadata for a video file including path information, timestamps, and user preferences.
    /// </summary>
    public class VideoMetadata
    {
        public string FullPath { get; set; }
        public string FileName { get; set; }
        public string FolderName { get; set; }
        public DateTime CreationTime { get; set; }
        public DateTime ModificationTime { get; set; }
        public DateTime EarliestTime { get; set; }  // Min of creation and modification
        public bool IsFavorite { get; set; }
        public bool HasBeenWatched { get; set; }
        public double PriorityWeight { get; set; }

        /// <summary>
        /// Initializes a new instance of VideoMetadata from a file path.
        /// Extracts file information and calculates the earliest timestamp.
        /// </summary>
        /// <param name="fullPath">The full path to the video file</param>
        public VideoMetadata(string fullPath)
        {
            FullPath = fullPath;
            FileName = Path.GetFileName(fullPath);
            FolderName = Path.GetFileName(Path.GetDirectoryName(fullPath)) ?? string.Empty;

            FileInfo fileInfo = new FileInfo(fullPath);
            CreationTime = fileInfo.CreationTime;
            ModificationTime = fileInfo.LastWriteTime;
            EarliestTime = CreationTime < ModificationTime ? CreationTime : ModificationTime;

            PriorityWeight = 1.0;
            IsFavorite = false;
            HasBeenWatched = false;
        }
    }
}
