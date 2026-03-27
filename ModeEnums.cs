using System;

namespace Cyber​​ConcubineSelection
{
    /// <summary>
    /// Represents the two mode groups in the application.
    /// Group A: Original modes (视频模式/文件夹模式) with black/white list filtering
    /// Group B: New matching modes with time-based and preference-based selection
    /// </summary>
    [Obsolete("Use PrimaryMode instead. This enum is kept for backward compatibility during migration.")]
    public enum ModeGroup
    {
        GroupA,  // Original modes (视频模式/文件夹模式)
        GroupB   // New matching modes
    }

    /// <summary>
    /// Represents the modes available in Mode Group A (original modes).
    /// </summary>
    [Obsolete("Use PrimaryMode instead. This enum is kept for backward compatibility during migration.")]
    public enum GroupAMode
    {
        VideoMode,    // 视频模式 - Video mode
        FolderMode    // 文件夹模式 - Folder mode
    }

    /// <summary>
    /// Represents the modes available in Mode Group B (new matching modes).
    /// </summary>
    [Obsolete("Use FilterOptions instead. This enum is kept for backward compatibility during migration.")]
    public enum GroupBMode
    {
        NewestToOldest,  // 从新到旧 - Newest to oldest
        OldestToNewest,  // 从旧到新 - Oldest to newest
        NeverWatched,    // 从未看过 - Never watched
        FavoritesOnly    // 只看最爱 - Favorites only
    }

    /// <summary>
    /// Represents the primary mode selection (formerly Group A).
    /// Primary mode determines how black/white list filtering is applied.
    /// </summary>
    public enum PrimaryMode
    {
        /// <summary>
        /// 视频模式 - Video mode: Black/white lists affect folder name display only
        /// </summary>
        VideoMode,

        /// <summary>
        /// 文件夹模式 - Folder mode: Black/white lists act as true filters
        /// </summary>
        FolderMode
    }

    /// <summary>
    /// Represents individual filter options (formerly Group B modes).
    /// These can be combined using bitwise flags to apply multiple filters simultaneously.
    /// All filters work in conjunction with the selected PrimaryMode.
    /// </summary>
    [Flags]
    public enum FilterOptions
    {
        /// <summary>
        /// No filters applied
        /// </summary>
        None = 0,

        /// <summary>
        /// 从新到旧 - Sort by newest first: Prioritizes recently created/modified videos
        /// </summary>
        NewestToOldest = 1 << 0,  // 1

        /// <summary>
        /// 从旧到新 - Sort by oldest first: Prioritizes older videos
        /// </summary>
        OldestToNewest = 1 << 1,  // 2

        /// <summary>
        /// 从未看过 - Never watched: Only shows videos not in history log
        /// </summary>
        NeverWatched = 1 << 2,    // 4

        /// <summary>
        /// 只看最爱 - Favorites only: Only shows videos in favorites list
        /// </summary>
        FavoritesOnly = 1 << 3,   // 8

        /// <summary>
        /// 超分 - Super resolution: Only shows videos with upscaling keywords (iris, prob, apo)
        /// </summary>
        SuperResolution = 1 << 4,  // 16

        /// <summary>
        /// 无码破解 - Uncensored: Only shows videos with uncensored keywords (wm, 无码, 破解, restored)
        /// </summary>
        Uncensored = 1 << 5,  // 32

        /// <summary>
        /// 自定义关键词 - Custom keywords: Only shows videos matching user-defined keywords
        /// </summary>
        CustomKeywords = 1 << 6  // 64
    }
}
