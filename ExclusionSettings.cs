namespace Cyber​​ConcubineSelection
{
    /// <summary>
    /// Represents exclusion settings for filtering recently played videos.
    /// Each mode group (A and B) has independent exclusion settings.
    /// </summary>
    public class ExclusionSettings
    {
        /// <summary>
        /// Number of days to exclude recently played videos.
        /// Videos played within this many days will be excluded from selection.
        /// </summary>
        public int ExcludeRecentDays { get; set; }

        /// <summary>
        /// Number of recent videos to exclude.
        /// The most recently played N videos will be excluded from selection.
        /// </summary>
        public int ExcludeRecentCount { get; set; }

        /// <summary>
        /// Minimum number of videos required before exclusion logic is applied.
        /// Only applies to Group A folder mode.
        /// If a folder has fewer videos than this threshold, exclusion is skipped for that folder.
        /// </summary>
        public int MinVideosForExclusion { get; set; }

        /// <summary>
        /// Creates a new ExclusionSettings instance with default values.
        /// </summary>
        public ExclusionSettings()
        {
            ExcludeRecentDays = 0;
            ExcludeRecentCount = 0;
            MinVideosForExclusion = 0;
        }

        /// <summary>
        /// Creates a new ExclusionSettings instance with specified values.
        /// </summary>
        public ExclusionSettings(int excludeRecentDays, int excludeRecentCount, int minVideosForExclusion = 0)
        {
            ExcludeRecentDays = excludeRecentDays;
            ExcludeRecentCount = excludeRecentCount;
            MinVideosForExclusion = minVideosForExclusion;
        }

        /// <summary>
        /// Creates a copy of this ExclusionSettings instance.
        /// </summary>
        public ExclusionSettings Clone()
        {
            return new ExclusionSettings(ExcludeRecentDays, ExcludeRecentCount, MinVideosForExclusion);
        }
    }
}
