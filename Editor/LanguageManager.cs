using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace UnityAIAgent.Editor
{
    /// <summary>
    /// 语言管理器，根据Unity的当前语言自动选择界面语言
    /// Language Manager that automatically selects interface language based on Unity's current language
    /// </summary>
    public static class LanguageManager
    {
        public enum Language
        {
            Chinese,
            English
        }
        
        private static Language? _currentLanguage;
        
        /// <summary>
        /// 获取当前语言设置
        /// Get current language setting
        /// </summary>
        public static Language CurrentLanguage
        {
            get
            {
                if (_currentLanguage == null)
                {
                    _currentLanguage = DetectUnityLanguage();
                }
                return _currentLanguage.Value;
            }
        }
        
        /// <summary>
        /// 检测Unity当前使用的语言
        /// Detect Unity's current language
        /// </summary>
        private static Language DetectUnityLanguage()
        {
            // 检查Unity编辑器的语言设置
            var systemLanguage = Application.systemLanguage;
            var cultureInfo = CultureInfo.CurrentUICulture;
            
            // 检查Unity的系统语言设置
            if (systemLanguage == SystemLanguage.Chinese || 
                systemLanguage == SystemLanguage.ChineseSimplified ||
                systemLanguage == SystemLanguage.ChineseTraditional)
            {
                return Language.Chinese;
            }
            
            // 检查系统区域设置
            if (cultureInfo.Name.StartsWith("zh"))
            {
                return Language.Chinese;
            }
            
            // 默认使用英文
            return Language.English;
        }
        
        /// <summary>
        /// 获取本地化文本
        /// Get localized text
        /// </summary>
        public static string GetText(string chineseText, string englishText)
        {
            return CurrentLanguage == Language.Chinese ? chineseText : englishText;
        }
        
        /// <summary>
        /// 重置语言检测（用于刷新语言设置）
        /// Reset language detection (for refreshing language settings)
        /// </summary>
        public static void RefreshLanguage()
        {
            _currentLanguage = null;
        }
    }
}