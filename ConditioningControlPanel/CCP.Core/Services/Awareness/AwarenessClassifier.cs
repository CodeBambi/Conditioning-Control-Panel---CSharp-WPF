using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Core.Services.Awareness;

/// <summary>
/// Pure window-title classification for the awareness engine, ported verbatim from the WPF
/// head (Services/UI/WindowAwarenessService.cs:93-330 dictionaries, :563-641 CategorizeWindow,
/// :645-692 ExtractBrowserTabName, :695-720 ExtractPageNameWithService). Stateless and
/// side-effect free so the classification contract is unit-testable without timers.
/// Privacy: input titles are transient parameters — never stored, never logged.
/// </summary>
public static class AwarenessClassifier
{
    // DETECTION DICTIONARIES - Maps keywords to display names
    // (WPF Services/UI/WindowAwarenessService.cs:93-167)
    private static readonly Dictionary<string, string> GamingApps = new(StringComparer.OrdinalIgnoreCase)
    {
        // MOBAs
        { "league of legends", "League of Legends" },
        { "leagueclient", "League of Legends" },
        { "dota 2", "Dota 2" },
        { "dota2", "Dota 2" },
        { "heroes of the storm", "Heroes of the Storm" },
        { "smite", "Smite" },

        // FPS/Shooters
        { "valorant", "Valorant" },
        { "counter-strike", "Counter-Strike" },
        { "cs2", "Counter-Strike 2" },
        { "csgo", "CS:GO" },
        { "overwatch", "Overwatch" },
        { "apex legends", "Apex Legends" },
        { "call of duty", "Call of Duty" },
        { "fortnite", "Fortnite" },
        { "rainbow six", "Rainbow Six Siege" },
        { "pubg", "PUBG" },
        { "battlefield", "Battlefield" },
        { "destiny 2", "Destiny 2" },
        { "warzone", "Warzone" },
        { "halo infinite", "Halo Infinite" },

        // RPGs/Adventure
        { "elden ring", "Elden Ring" },
        { "dark souls", "Dark Souls" },
        { "skyrim", "Skyrim" },
        { "fallout", "Fallout" },
        { "the witcher", "The Witcher" },
        { "cyberpunk", "Cyberpunk 2077" },
        { "baldur's gate", "Baldur's Gate 3" },
        { "diablo", "Diablo" },
        { "path of exile", "Path of Exile" },
        { "final fantasy", "Final Fantasy" },
        { "genshin impact", "Genshin Impact" },
        { "honkai", "Honkai" },

        // MMOs
        { "world of warcraft", "World of Warcraft" },
        { "ffxiv", "Final Fantasy XIV" },
        { "guild wars", "Guild Wars 2" },
        { "lost ark", "Lost Ark" },
        { "new world", "New World" },

        // Strategy
        { "starcraft", "StarCraft" },
        { "civilization", "Civilization" },
        { "age of empires", "Age of Empires" },
        { "total war", "Total War" },

        // Other Popular
        { "minecraft", "Minecraft" },
        { "roblox", "Roblox" },
        { "among us", "Among Us" },
        { "rocket league", "Rocket League" },
        { "dead by daylight", "Dead by Daylight" },
        { "phasmophobia", "Phasmophobia" },
        { "stardew valley", "Stardew Valley" },
        { "terraria", "Terraria" },
        { "hearthstone", "Hearthstone" },
        { "sims", "The Sims" },

        // Launchers (fallback)
        { "steam", "Steam games" },
        { "epic games", "Epic Games" },
        { "battle.net", "Battle.net games" },
        { "origin", "EA games" },
        { "ubisoft connect", "Ubisoft games" },
        { "riot client", "Riot games" },
        { "xbox app", "Xbox games" },
        { "geforce now", "GeForce Now" },
    };

    // (WPF Services/UI/WindowAwarenessService.cs:169-190)
    private static readonly Dictionary<string, string> SocialApps = new(StringComparer.OrdinalIgnoreCase)
    {
        { "discord", "Discord" },
        { "twitter", "Twitter" },
        { "x.com", "Twitter/X" },
        { "/ x", "Twitter/X" },
        { "reddit", "Reddit" },
        { "facebook", "Facebook" },
        { "instagram", "Instagram" },
        { "tiktok", "TikTok" },
        { "snapchat", "Snapchat" },
        { "whatsapp", "WhatsApp" },
        { "telegram", "Telegram" },
        { "messenger", "Messenger" },
        { "slack", "Slack" },
        { "teams", "Microsoft Teams" },
        { "zoom", "Zoom" },
        { "skype", "Skype" },
        { "tumblr", "Tumblr" },
        { "pinterest", "Pinterest" },
        { "linkedin", "LinkedIn" },
    };

    // (WPF Services/UI/WindowAwarenessService.cs:192-213)
    private static readonly Dictionary<string, string> ShoppingSites = new(StringComparer.OrdinalIgnoreCase)
    {
        { "amazon", "Amazon" },
        { "ebay", "eBay" },
        { "etsy", "Etsy" },
        { "aliexpress", "AliExpress" },
        { "wish.com", "Wish" },
        { "walmart", "Walmart" },
        { "target", "Target" },
        { "best buy", "Best Buy" },
        { "newegg", "Newegg" },
        { "shein", "Shein" },
        { "asos", "ASOS" },
        { "zara", "Zara" },
        { "h&m", "H&M" },
        { "sephora", "Sephora" },
        { "ulta", "Ulta Beauty" },
        { "shopping cart", "online shopping" },
        { "checkout", "online shopping" },
        { "throne", "Throne" },
        { "wishtender", "Wishtender" },
    };

    // (WPF Services/UI/WindowAwarenessService.cs:215-236)
    private static readonly Dictionary<string, string> MediaSites = new(StringComparer.OrdinalIgnoreCase)
    {
        { "youtube", "YouTube" },
        { "netflix", "Netflix" },
        { "hulu", "Hulu" },
        { "disney+", "Disney+" },
        { "hbo max", "HBO Max" },
        { "prime video", "Prime Video" },
        { "twitch", "Twitch" },
        { "spotify", "Spotify" },
        { "apple music", "Apple Music" },
        { "soundcloud", "SoundCloud" },
        { "crunchyroll", "Crunchyroll" },
        { "funimation", "Funimation" },
        { "plex", "Plex" },
        { "vlc", "VLC" },
        { "pornhub", "adult content" },
        { "xvideos", "adult content" },
        { "xhamster", "adult content" },
        { "bambicloud", "BambiCloud" },
        { "hypnotube", "Hypnotube" },
    };

    // (WPF Services/UI/WindowAwarenessService.cs:238-257)
    private static readonly Dictionary<string, string> LearningSites = new(StringComparer.OrdinalIgnoreCase)
    {
        { "wikipedia", "Wikipedia" },
        { "stack overflow", "Stack Overflow" },
        { "stackoverflow", "Stack Overflow" },
        { "github", "GitHub" },
        { "gitlab", "GitLab" },
        { "udemy", "Udemy" },
        { "coursera", "Coursera" },
        { "khan academy", "Khan Academy" },
        { "duolingo", "Duolingo" },
        { "quora", "Quora" },
        { "medium", "Medium" },
        { "dev.to", "Dev.to" },
        { "w3schools", "W3Schools" },
        { "mdn web docs", "MDN" },
        { "geeksforgeeks", "GeeksforGeeks" },
        { "leetcode", "LeetCode" },
        { "hackerrank", "HackerRank" },
    };

    // (WPF Services/UI/WindowAwarenessService.cs:259-299)
    private static readonly Dictionary<string, string> WorkingApps = new(StringComparer.OrdinalIgnoreCase)
    {
        { "visual studio code", "VS Code" },
        { "vs code", "VS Code" },
        { "vscode", "VS Code" },
        { "- visual studio", "Visual Studio" },  // Avoid matching VS Code
        { "intellij", "IntelliJ" },
        { "pycharm", "PyCharm" },
        { "webstorm", "WebStorm" },
        { "rider", "Rider" },
        { "sublime text", "Sublime Text" },
        { "notepad++", "Notepad++" },
        { "atom editor", "Atom" },
        { "word", "Microsoft Word" },
        { "excel", "Microsoft Excel" },
        { "powerpoint", "PowerPoint" },
        { "google docs", "Google Docs" },
        { "google sheets", "Google Sheets" },
        { "notion", "Notion" },
        { "trello", "Trello" },
        { "jira", "Jira" },
        { "asana", "Asana" },
        { "figma", "Figma" },
        { "photoshop", "Photoshop" },
        { "illustrator", "Illustrator" },
        { "premiere", "Premiere Pro" },
        { "after effects", "After Effects" },
        { "blender", "Blender" },
        { "unity", "Unity" },
        { "unreal engine", "Unreal Engine" },
        { "terminal", "Terminal" },
        { "powershell", "PowerShell" },
        { "cmd.exe", "Command Prompt" },
        { "windows terminal", "Terminal" },
        { "outlook", "Outlook" },
        { "gmail", "Gmail" },
        { "cursor", "Cursor" },
        { "zed", "Zed Editor" },
    };

    /// <summary>
    /// Categorize a window based on its title and detect the specific app/service.
    /// Substring match against the keyword dictionaries, first match wins, in the STRICT
    /// priority order Gaming > Learning > Shopping > Social > Media > Working > browser
    /// fallback > Unknown (WPF Services/UI/WindowAwarenessService.cs:563-641).
    /// Returns: (Category, DetectedName for display, ServiceName, PageTitle).
    /// </summary>
    public static (ActivityCategory Category, string DetectedName, string ServiceName, string PageTitle) Categorize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return (ActivityCategory.Unknown, "something", "", "");

        var lowerTitle = title.ToLowerInvariant();

        // Check each category in priority order

        // Gaming (highest priority) (WPF :572-577)
        foreach (var kvp in GamingApps)
        {
            if (lowerTitle.Contains(kvp.Key))
                return (ActivityCategory.Gaming, kvp.Value, kvp.Value, "");
        }

        // Learning (before general browsing) (WPF :580-587)
        foreach (var kvp in LearningSites)
        {
            if (lowerTitle.Contains(kvp.Key))
            {
                var (displayName, pageTitle) = ExtractPageNameWithService(title, kvp.Value);
                return (ActivityCategory.Learning, displayName, kvp.Value, pageTitle);
            }
        }

        // Shopping (WPF :590-597)
        foreach (var kvp in ShoppingSites)
        {
            if (lowerTitle.Contains(kvp.Key))
            {
                var (displayName, pageTitle) = ExtractPageNameWithService(title, kvp.Value);
                return (ActivityCategory.Shopping, displayName, kvp.Value, pageTitle);
            }
        }

        // Social (WPF :600-607)
        foreach (var kvp in SocialApps)
        {
            if (lowerTitle.Contains(kvp.Key))
            {
                var (displayName, pageTitle) = ExtractPageNameWithService(title, kvp.Value);
                return (ActivityCategory.Social, displayName, kvp.Value, pageTitle);
            }
        }

        // Media (WPF :610-617)
        foreach (var kvp in MediaSites)
        {
            if (lowerTitle.Contains(kvp.Key))
            {
                var (displayName, pageTitle) = ExtractPageNameWithService(title, kvp.Value);
                return (ActivityCategory.Media, displayName, kvp.Value, pageTitle);
            }
        }

        // Working (WPF :620-627)
        foreach (var kvp in WorkingApps)
        {
            if (lowerTitle.Contains(kvp.Key))
            {
                var (displayName, pageTitle) = ExtractPageNameWithService(title, kvp.Value);
                return (ActivityCategory.Working, displayName, kvp.Value, pageTitle);
            }
        }

        // Generic browser detection - extract the tab title (WPF :630-637)
        if (lowerTitle.Contains("chrome") || lowerTitle.Contains("firefox") ||
            lowerTitle.Contains("edge") || lowerTitle.Contains("safari") ||
            lowerTitle.Contains("opera") || lowerTitle.Contains("brave"))
        {
            var tabName = ExtractBrowserTabName(title);
            return (ActivityCategory.Browsing, tabName, "browser", tabName);
        }

        return (ActivityCategory.Unknown, "something", "", "");
    }

    /// <summary>
    /// Extract the page/tab name from a window title.
    /// Browser titles are usually: "Page Title - Browser Name" or "Page Title — Browser Name"
    /// (WPF Services/UI/WindowAwarenessService.cs:645-692).
    /// </summary>
    public static string ExtractBrowserTabName(string windowTitle)
    {
        // Common browser suffixes to remove
        var browserSuffixes = new[] {
            " - Google Chrome", " - Chrome", " — Google Chrome",
            " - Mozilla Firefox", " - Firefox", " — Mozilla Firefox",
            " - Microsoft Edge", " - Edge", " — Microsoft Edge",
            " - Opera", " — Opera",
            " - Brave", " — Brave",
            " - Safari", " — Safari"
        };

        var result = windowTitle;
        foreach (var suffix in browserSuffixes)
        {
            if (result.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(0, result.Length - suffix.Length);
                break;
            }
        }

        // If still has a dash separator, take the first part (usually the page title)
        var dashIndex = result.LastIndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex > 0 && dashIndex < result.Length - 3)
        {
            result = result.Substring(0, dashIndex);
        }

        // Clean up and limit length
        result = result.Trim();
        if (result.Length > 50)
            result = result.Substring(0, 47) + "...";

        return string.IsNullOrEmpty(result) ? "a webpage" : result;
    }

    /// <summary>
    /// Extract both the display name and the raw page title from a window title.
    /// Splits on " - " / " — " / " | ", first segment = page title, display
    /// "{firstPart} on {serviceName}" (WPF Services/UI/WindowAwarenessService.cs:695-720).
    /// Returns: (DisplayName like "CodeBambi on Throne", PageTitle like "CodeBambi").
    /// </summary>
    public static (string DisplayName, string PageTitle) ExtractPageNameWithService(string windowTitle, string serviceName)
    {
        // For apps like VS Code: "filename.cs - ProjectName - Visual Studio Code"
        // For browsers: "Page Title - Site Name - Browser"

        var parts = windowTitle.Split(new[] { " - ", " — ", " | " }, StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length >= 2)
        {
            // Usually first part is the specific content (file name, page title)
            var firstPart = parts[0].Trim();
            if (!string.IsNullOrEmpty(firstPart) && firstPart.Length > 2)
            {
                // Store raw page title
                var pageTitle = firstPart;

                // Truncate for display if needed
                if (firstPart.Length > 40)
                    firstPart = firstPart.Substring(0, 37) + "...";

                return ($"{firstPart} on {serviceName}", pageTitle);
            }
        }

        return (serviceName, "");
    }
}
