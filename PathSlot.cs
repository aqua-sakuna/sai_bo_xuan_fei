using System;
using System.IO;

namespace Cyber​​ConcubineSelection
{
    /// <summary>
    /// Represents a single path slot in the multi-path configuration system.
    /// Each slot can hold a directory path and has an enabled/disabled state.
    /// Validates: Requirements 1.1, 1.5
    /// </summary>
    public class PathSlot
    {
        /// <summary>
        /// The index of this path slot (0-4).
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// The full directory path for this slot, or empty string if not configured.
        /// </summary>
        public string DirectoryPath { get; set; }

        /// <summary>
        /// Whether this path slot is enabled for video selection.
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Creates a new PathSlot with the specified index.
        /// Initializes with empty path and disabled state.
        /// </summary>
        /// <param name="index">The slot index (0-4)</param>
        public PathSlot(int index)
        {
            Index = index;
            DirectoryPath = string.Empty;
            IsEnabled = false;
        }

        /// <summary>
        /// Checks if this path slot is valid (has a non-empty path that exists on disk).
        /// </summary>
        /// <returns>True if the path is non-empty and the directory exists</returns>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(DirectoryPath) && 
                   Directory.Exists(DirectoryPath);
        }
    }
}
