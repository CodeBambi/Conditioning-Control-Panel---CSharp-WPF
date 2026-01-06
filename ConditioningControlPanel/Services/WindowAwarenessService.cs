using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Activity categories detected by window awareness
    /// </summary>
    public enum ActivityCategory
    {
        Unknown,
        Gaming,
        Browsing,
        Social,
        Shopping,
        Working,
        Media,
        Learning,
        Idle
    }

    /// <summary>
    /// Event args for activity change events - includes specific detected service/app
    /// </summary>
    public class ActivityChangedEventArgs : EventArgs
    {
        public ActivityCategory Category { get; }
        public ActivityCategory PreviousCategory { get; }
        public string DetectedName { get; } // e.g., "League of Legends", "Wikipedia", "Discord"
        public string ServiceName { get; } // e.g., "Throne", "YouTube", "VS Code" - the actual service/app
        public string PageTitle { get; } // e.g., "CodeBambi's wishlist", "How to code" - the specific page/content
        public bool IsNewService { get; } // True when switching to a different service/app (not just a different page)
        public string PreviousServiceName { get; } // For context on what they switched from

        public ActivityChangedEventArgs(ActivityCategory category, ActivityCategory previousCategory, string detectedName, string serviceName = "", string pageTitle = "", bool isNewService = false, string previousServiceName = "")
        {
            Category = category;
            PreviousCategory = previousCategory;
            DetectedName = detectedName;
            ServiceName = string.IsNullOrEmpty(serviceName) ? detectedName : serviceName;
            PageTitle = pageTitle;
            IsNewService = isNewService;
            PreviousServiceName = previousServiceName;
        }
    }

    /// <summary>
    /// Service that monitors the user's active window and categorizes their activity.
    /// Privacy-focused: only categorizes, never logs or stores window titles.
    /// </summary>
    public class WindowAwarenessService : IDisposable
    {
        // P/Invoke declarations
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        // Events
        public event EventHandler<ActivityChangedEventArgs>? ActivityChanged;
        public event EventHandler<ActivityChangedEventArgs>? StillOnActivity;

        // State
        private DispatcherTimer? _pollTimer;
        private DispatcherTimer? _stillOnTimer;
        private ActivityCategory _currentCategory = ActivityCategory.Unknown;
        private string _currentDetectedName = "";
        private string _currentServiceName = "";
        private string _currentPageTitle = "";
        private string _lastWindowTitle = "";
        private DateTime _lastActivityChange = DateTime.Now;
        private DateTime _lastReactionTime = DateTime.MinValue;
        private DateTime _lastStillOnTime = DateTime.MinValue;
        private bool _isRunning;
        private bool _isDisposed;

        // Constants
        private const int IdleThresholdMinutes = 5;

        // ============================================================
        // DETECTION DICTIONARIES - Maps keywords to display names
        // ============================================================

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
        /// Current detected activity category
        /// </summary>
        public ActivityCategory CurrentActivity => _currentCategory;

        /// <summary>
        /// Current detected app/service name
        /// </summary>
        public string CurrentDetectedName => _currentDetectedName;

        /// <summary>
        /// Current detected service/platform name (e.g., "Throne", "YouTube", "VS Code")
        /// </summary>
        public string CurrentServiceName => _currentServiceName;

        /// <summary>
        /// Current page/content title (e.g., "CodeBambi's wishlist", "How to code tutorial")
        /// </summary>
        public string CurrentPageTitle => _currentPageTitle;

        /// <summary>
        /// How long the user has been on the current activity
        /// </summary>
        public TimeSpan CurrentActivityDuration => DateTime.Now - _lastActivityChange;

        /// <summary>
        /// Whether the service is actively monitoring
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// Start monitoring window activity
        /// </summary>
        public void Start()
        {
            if (_isRunning || _isDisposed) return;

            // Check Patreon access (Tier 1+ or whitelisted)
            if (App.Patreon?.HasPremiumAccess != true)
            {
                App.Logger?.Debug("WindowAwareness: Not starting - Patreon Level 1+ or whitelist required");
                return;
            }

            // Check if feature is enabled and consent given
            if (App.Settings?.Current?.AwarenessModeEnabled != true ||
                App.Settings?.Current?.AwarenessConsentGiven != true)
            {
                App.Logger?.Debug("WindowAwareness: Not starting - feature disabled or no consent");
                return;
            }

            _pollTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.5) // Fast polling for quick tab/app detection
            };
            _pollTimer.Tick += OnPollTick;
            _pollTimer.Start();
            _isRunning = true;

            App.Logger?.Information("WindowAwareness: Started monitoring");
        }

        /// <summary>
        /// Stop monitoring window activity
        /// </summary>
        public void Stop()
        {
            if (!_isRunning) return;

            _pollTimer?.Stop();
            _pollTimer = null;
            _stillOnTimer?.Stop();
            _stillOnTimer = null;
            _isRunning = false;
            _currentCategory = ActivityCategory.Unknown;
            _currentDetectedName = "";

            App.Logger?.Information("WindowAwareness: Stopped monitoring");
        }

        /// <summary>
        /// Check if enough time has passed since last reaction (cooldown)
        /// </summary>
        public bool CanReact()
        {
            var cooldownSeconds = App.Settings?.Current?.AwarenessReactionCooldownSeconds ?? 90;
            return (DateTime.Now - _lastReactionTime).TotalSeconds >= cooldownSeconds;
        }

        /// <summary>
        /// Check if enough time has passed since last "still on" comment
        /// </summary>
        public bool CanStillOnReact()
        {
            var cooldownSeconds = App.Settings?.Current?.AwarenessReactionCooldownSeconds ?? 90;
            return (DateTime.Now - _lastStillOnTime).TotalSeconds >= cooldownSeconds;
        }

        /// <summary>
        /// Mark that a reaction was just shown (reset cooldown)
        /// </summary>
        public void MarkReaction()
        {
            _lastReactionTime = DateTime.Now;
        }

        /// <summary>
        /// Mark that a "still on" comment was just shown
        /// </summary>
        public void MarkStillOnReaction()
        {
            _lastStillOnTime = DateTime.Now;
        }

        // Still-on milestone tracking: 1 min, 5 min, 10 min
        private static readonly int[] StillOnMilestonesMinutes = { 1, 5, 10 };
        private int _currentMilestoneIndex = 0;

        /// <summary>
        /// Start or restart the "still on" timer for milestone-based comments (1min, 5min, 10min)
        /// </summary>
        private void RestartStillOnTimer()
        {
            _stillOnTimer?.Stop();
            _currentMilestoneIndex = 0; // Reset milestones when activity changes

            // Only start if we have a recognized activity (not Unknown or Idle)
            if (_currentCategory == ActivityCategory.Unknown || _currentCategory == ActivityCategory.Idle)
                return;

            // Start timer for first milestone (1 minute)
            StartNextMilestoneTimer();
        }

        private void StartNextMilestoneTimer()
        {
            if (_currentMilestoneIndex >= StillOnMilestonesMinutes.Length)
                return; // No more milestones

            var minutesUntilMilestone = StillOnMilestonesMinutes[_currentMilestoneIndex];
            var elapsedMinutes = (DateTime.Now - _lastActivityChange).TotalMinutes;
            var waitMinutes = minutesUntilMilestone - elapsedMinutes;

            if (waitMinutes <= 0)
            {
                // Already past this milestone, move to next
                _currentMilestoneIndex++;
                StartNextMilestoneTimer();
                return;
            }

            _stillOnTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(waitMinutes)
            };
            _stillOnTimer.Tick += OnStillOnMilestoneTick;
            _stillOnTimer.Start();

            App.Logger?.Debug("WindowAwareness: Still-on timer set for {Minutes}min milestone", minutesUntilMilestone);
        }

        private void OnStillOnMilestoneTick(object? sender, EventArgs e)
        {
            _stillOnTimer?.Stop();

            // Fire the StillOnActivity event if we're still on the same activity
            if (_currentCategory != ActivityCategory.Unknown && _currentCategory != ActivityCategory.Idle)
            {
                if (IsCategoryEnabled(_currentCategory))
                {
                    var milestone = _currentMilestoneIndex < StillOnMilestonesMinutes.Length
                        ? StillOnMilestonesMinutes[_currentMilestoneIndex]
                        : 10;
                    App.Logger?.Debug("WindowAwareness: Still on {Name} for {Minutes} minutes", _currentDetectedName, milestone);
                    StillOnActivity?.Invoke(this, new ActivityChangedEventArgs(
                        _currentCategory, _currentCategory, _currentDetectedName, _currentServiceName, _currentPageTitle));
                }
            }

            // Move to next milestone
            _currentMilestoneIndex++;
            StartNextMilestoneTimer();
        }

        private void OnPollTick(object? sender, EventArgs e)
        {
            try
            {
                var windowTitle = GetActiveWindowTitle();

                // Debug: Log what we're seeing
                if (!string.IsNullOrEmpty(windowTitle) && windowTitle != _lastWindowTitle)
                {
                    App.Logger?.Debug("WindowAwareness: Active window = '{Title}'", windowTitle);
                }

                // Check for idle (same window for too long)
                if (windowTitle == _lastWindowTitle)
                {
                    var idleMinutes = (DateTime.Now - _lastActivityChange).TotalMinutes;
                    if (idleMinutes >= IdleThresholdMinutes && _currentCategory != ActivityCategory.Idle)
                    {
                        SetActivity(ActivityCategory.Idle, "being idle", "", "");
                    }
                    return;
                }

                // Window changed - reset activity timer
                _lastWindowTitle = windowTitle;
                _lastActivityChange = DateTime.Now;

                // First try to categorize by window title
                var (category, detectedName, serviceName, pageTitle) = CategorizeWindow(windowTitle);

                // If unknown, check running processes for games
                if (category == ActivityCategory.Unknown)
                {
                    var processResult = CheckRunningProcesses();
                    if (processResult.HasValue)
                    {
                        category = processResult.Value.Item1;
                        detectedName = processResult.Value.Item2;
                        serviceName = processResult.Value.Item2; // For games, service = detected name
                        pageTitle = "";
                        App.Logger?.Debug("WindowAwareness: Detected via process: {Name}", detectedName);
                    }
                }

                if (category != _currentCategory || detectedName != _currentDetectedName)
                {
                    SetActivity(category, detectedName, serviceName, pageTitle);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning("WindowAwareness: Poll error - {Error}", ex.Message);
            }
        }

        private void SetActivity(ActivityCategory newCategory, string detectedName, string serviceName, string pageTitle)
        {
            var previousCategory = _currentCategory;
            var previousServiceName = _currentServiceName;

            // Determine if this is a new service/app (not just a different page)
            var isNewService = !string.Equals(serviceName, previousServiceName, StringComparison.OrdinalIgnoreCase)
                               && !string.IsNullOrEmpty(serviceName)
                               && !string.IsNullOrEmpty(previousServiceName);

            _currentCategory = newCategory;
            _currentDetectedName = detectedName;
            _currentServiceName = serviceName;
            _currentPageTitle = pageTitle;
            _lastActivityChange = DateTime.Now; // Track when this activity started

            // Fire event (don't log the window title for privacy, only the detected name)
            App.Logger?.Debug("WindowAwareness: Detected {Name} ({Category}) - Service: {Service}, IsNew: {IsNew}",
                detectedName, newCategory, serviceName, isNewService);

            ActivityChanged?.Invoke(this, new ActivityChangedEventArgs(
                newCategory, previousCategory, detectedName, serviceName, pageTitle, isNewService, previousServiceName));

            // Restart the still-on timer for periodic comments
            RestartStillOnTimer();
        }

        private string GetActiveWindowTitle()
        {
            var handle = GetForegroundWindow();
            if (handle == IntPtr.Zero) return "";

            var sb = new StringBuilder(512);
            GetWindowText(handle, sb, 512);
            return sb.ToString();
        }

        /// <summary>
        /// Check running processes for known games/apps (catches things even when not focused)
        /// </summary>
        private (ActivityCategory, string)? CheckRunningProcesses()
        {
            try
            {
                // Process names to look for (without .exe)
                var gameProcesses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "RiotClientServices", "Riot Client" },
                    { "RiotClientUx", "Riot Client" },
                    { "LeagueClient", "League of Legends" },
                    { "League of Legends", "League of Legends" },
                    { "VALORANT-Win64-Shipping", "Valorant" },
                    { "VALORANT", "Valorant" },
                    { "cs2", "Counter-Strike 2" },
                    { "csgo", "CS:GO" },
                    { "dota2", "Dota 2" },
                    { "GenshinImpact", "Genshin Impact" },
                    { "steam", "Steam" },
                    { "EpicGamesLauncher", "Epic Games" },
                    { "Battle.net", "Battle.net" },
                };

                var runningProcesses = Process.GetProcesses()
                    .Select(p => p.ProcessName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in gameProcesses)
                {
                    if (runningProcesses.Contains(kvp.Key))
                    {
                        return (ActivityCategory.Gaming, kvp.Value);
                    }
                }
            }
            catch
            {
                // Ignore errors from process enumeration
            }

            return null;
        }

        /// <summary>
        /// Categorize a window based on its title and detect specific app/service.
        /// Returns: (Category, DetectedName for display, ServiceName, PageTitle)
        /// </summary>
        private (ActivityCategory Category, string DetectedName, string ServiceName, string PageTitle) CategorizeWindow(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return (ActivityCategory.Unknown, "something", "", "");

            var lowerTitle = title.ToLowerInvariant();

            // Check each category in priority order

            // Gaming (highest priority)
            foreach (var kvp in GamingApps)
            {
                if (lowerTitle.Contains(kvp.Key))
                    return (ActivityCategory.Gaming, kvp.Value, kvp.Value, "");
            }

            // Learning (before general browsing)
            foreach (var kvp in LearningSites)
            {
                if (lowerTitle.Contains(kvp.Key))
                {
                    var (displayName, pageTitle) = ExtractPageNameWithService(title, kvp.Value);
                    return (ActivityCategory.Learning, displayName, kvp.Value, pageTitle);
                }
            }

            // Shopping
            foreach (var kvp in ShoppingSites)
            {
                if (lowerTitle.Contains(kvp.Key))
                {
                    var (displayName, pageTitle) = ExtractPageNameWithService(title, kvp.Value);
                    return (ActivityCategory.Shopping, displayName, kvp.Value, pageTitle);
                }
            }

            // Social
            foreach (var kvp in SocialApps)
            {
                if (lowerTitle.Contains(kvp.Key))
                {
                    var (displayName, pageTitle) = ExtractPageNameWithService(title, kvp.Value);
                    return (ActivityCategory.Social, displayName, kvp.Value, pageTitle);
                }
            }

            // Media
            foreach (var kvp in MediaSites)
            {
                if (lowerTitle.Contains(kvp.Key))
                {
                    var (displayName, pageTitle) = ExtractPageNameWithService(title, kvp.Value);
                    return (ActivityCategory.Media, displayName, kvp.Value, pageTitle);
                }
            }

            // Working
            foreach (var kvp in WorkingApps)
            {
                if (lowerTitle.Contains(kvp.Key))
                {
                    var (displayName, pageTitle) = ExtractPageNameWithService(title, kvp.Value);
                    return (ActivityCategory.Working, displayName, kvp.Value, pageTitle);
                }
            }

            // Generic browser detection - extract the tab title
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
        /// Extract the page/tab name from a window title
        /// Browser titles are usually: "Page Title - Browser Name" or "Page Title — Browser Name"
        /// </summary>
        private string ExtractBrowserTabName(string windowTitle)
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
            var dashIndex = result.LastIndexOf(" - ");
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
        /// Extract a meaningful name from window title, with fallback to known app name
        /// </summary>
        private string ExtractPageName(string windowTitle, string fallbackName)
        {
            var (displayName, _) = ExtractPageNameWithService(windowTitle, fallbackName);
            return displayName;
        }

        /// <summary>
        /// Extract both the display name and the raw page title from a window title.
        /// Returns: (DisplayName like "CodeBambi on Throne", PageTitle like "CodeBambi")
        /// </summary>
        private (string DisplayName, string PageTitle) ExtractPageNameWithService(string windowTitle, string serviceName)
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

        /// <summary>
        /// Check if reactions are enabled for a specific category
        /// </summary>
        public bool IsCategoryEnabled(ActivityCategory category)
        {
            // All categories enabled - AI handles context appropriately
            return true;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
