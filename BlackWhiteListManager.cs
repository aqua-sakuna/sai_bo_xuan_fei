using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cyber​​ConcubineSelection
{
    /// <summary>
    /// 统一管理黑名单和白名单的加载、保存和访问
    /// </summary>
    public class BlackWhiteListManager
    {   
        private List<string> stopWords = new List<string>();  // 白名单（精确匹配）
        private List<string> blackList = new List<string>();  // 黑名单（包含匹配）

        private readonly string configDirectory;
        private readonly string stopWordsPath;
        private readonly string blackListPath;

        /// <summary>
        /// 获取白名单（只读）
        /// </summary>
        public IReadOnlyList<string> StopWords => stopWords.AsReadOnly();

        /// <summary>
        /// 获取黑名单（只读）
        /// </summary>
        public IReadOnlyList<string> BlackList => blackList.AsReadOnly();

        public BlackWhiteListManager(string configDirectory = "config")
        {
            this.configDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configDirectory);
            this.stopWordsPath = Path.Combine(this.configDirectory, "stop_words.txt");
            this.blackListPath = Path.Combine(this.configDirectory, "black_list.txt");

            // 加载黑白名单
            LoadFromConfig();
        }

        /// <summary>
        /// 从配置文件加载黑白名单
        /// </summary>
        public void LoadFromConfig()
        {
            // 默认白名单
            //stopWords = new List<string> { "FC2", "一本道", "东京热", "S1", "MOODYZ", "PRESTIGE", "hot", "pondo", "VR" };
            
            // 默认黑名单
            //blackList = new List<string> { "新建文件夹", "视频", "VIDEO", "OUTPUT", "TEMP", "下载", "字幕" };

            try
            {
                // 加载白名单
                if (File.Exists(stopWordsPath))
                {
                    stopWords = File.ReadAllLines(stopWordsPath)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .ToList();
                }

                // 加载黑名单
                if (File.Exists(blackListPath))
                {
                    blackList = File.ReadAllLines(blackListPath)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading black/white lists: {ex.Message}");
                // 继续使用默认值
            }
        }

        /// <summary>
        /// 保存白名单到配置文件
        /// </summary>
        public void SaveStopWords(List<string> words)
        {
            try
            {
                if (!Directory.Exists(configDirectory))
                {
                    Directory.CreateDirectory(configDirectory);
                }

                stopWords = words ?? new List<string>();
                File.WriteAllLines(stopWordsPath, stopWords);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving stop words: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 保存黑名单到配置文件
        /// </summary>
        public void SaveBlackList(List<string> words)
        {
            try
            {
                if (!Directory.Exists(configDirectory))
                {
                    Directory.CreateDirectory(configDirectory);
                }

                blackList = words ?? new List<string>();
                File.WriteAllLines(blackListPath, blackList);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving black list: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// 检查文件夹名是否在白名单中（精确匹配）
        /// </summary>
        public bool IsInWhiteList(string folderName)
        {
            if (string.IsNullOrEmpty(folderName))
                return false;

            string nameUpper = folderName.ToUpper();
            return stopWords.Any(sw => nameUpper.Equals(sw.ToUpper()));
        }

        /// <summary>
        /// 检查文件夹名是否在黑名单中（包含匹配）
        /// </summary>
        public bool IsInBlackList(string folderName)
        {
            if (string.IsNullOrEmpty(folderName))
                return false;

            string nameUpper = folderName.ToUpper();
            return blackList.Any(bl => nameUpper.Contains(bl.ToUpper()));
        }

        /// <summary>
        /// 检查文件夹名是否在黑名单中（包含匹配），支持自定义关键词
        /// </summary>
        public bool IsInBlackList(string folderName, List<string> customKeywords)
        {
            if (string.IsNullOrEmpty(folderName))
                return false;

            string nameUpper = folderName.ToUpper();
            
            // 检查黑名单
            if (blackList.Any(bl => nameUpper.Contains(bl.ToUpper())))
                return true;

            // 检查自定义关键词
            if (customKeywords != null && customKeywords.Count > 0)
            {
                return customKeywords.Any(kw => nameUpper.Contains(kw.ToUpper()));
            }

            return false;
        }
    }
}
