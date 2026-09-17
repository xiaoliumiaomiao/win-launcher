namespace WinLauncher.Services;

/// <summary>
/// 关键词匹配。分类器和噪声过滤共用。
///
/// 必须区分两种匹配方式，用错会大面积误伤 —— 这是实测踩出来的：
///
///   **纯拉丁字母数字词 → 词边界匹配。**
///   子串匹配会出人命：关键词 "msi"（微星）会命中 "msinfo32.exe"，
///   把「系统信息」分进硬件驱动；"code" 会命中 "CodeBuddy"。
///
///   **中文词 / 含符号的词 → 子串匹配。**
///   中文没有词边界概念；"notepad++" 这种带符号的也没法按词匹配。
/// </summary>
public static class KeywordMatcher
{
    /// <param name="allowPlural">允许词尾多一个 s。用于噪声过滤的 "release note" 要匹配 "Release Notes"。</param>
    public static bool Matches(string haystack, string keyword, bool allowPlural = false)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(keyword))
            return false;

        // 含非 ASCII（中文）或含符号 → 子串匹配
        if (keyword.Any(c => c > 0x7F || (!char.IsAsciiLetterOrDigit(c) && c != ' ')))
            return haystack.Contains(keyword, StringComparison.Ordinal);

        var index = haystack.IndexOf(keyword, StringComparison.Ordinal);

        while (index >= 0)
        {
            var beforeOk = index == 0 || !char.IsAsciiLetterOrDigit(haystack[index - 1]);
            var after = index + keyword.Length;

            var afterOk = after >= haystack.Length
                          || !char.IsAsciiLetterOrDigit(haystack[after])
                          || (allowPlural
                              && haystack[after] == 's'
                              && (after + 1 >= haystack.Length || !char.IsAsciiLetterOrDigit(haystack[after + 1])));

            if (beforeOk && afterOk)
                return true;

            index = haystack.IndexOf(keyword, index + 1, StringComparison.Ordinal);
        }

        return false;
    }

    public static bool MatchesAny(string haystack, IEnumerable<string> keywords, bool allowPlural = false)
        => keywords.Any(keyword => Matches(haystack, keyword, allowPlural));
}
