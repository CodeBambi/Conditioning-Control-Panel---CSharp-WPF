using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    public static class HelpContentService
    {
        private static readonly Dictionary<string, HelpContent> _content = new()
        {
            ["FlashImages"] = new HelpContent
            {
                SectionId = "FlashImages",
                Icon = "\u26A1",
                Title = "Flash Images",
                WhatItDoes = "Displays random images from your assets/images folder on screen at set intervals. " +
                             "Perfect for conditioning triggers, affirmations, or visual reinforcement during sessions. " +
                             "Supports PNG, JPG, and animated GIF formats.",
                Tips = new List<string>
                {
                    "Start with 10-20 flashes per hour and adjust based on your comfort level",
                    "Enable 'Click' mode to dismiss images manually for more interaction",
                    "Hydra mode creates an escalating challenge - each click spawns 2 more images!",
                    "Lower 'Max On Screen' if multiple simultaneous images feel overwhelming",
                    "Use high-contrast or eye-catching images for maximum impact"
                },
                HowItWorks = "A timer runs based on your 'Per Hour' setting. Each trigger event randomly selects " +
                             "images from your assets/images folder and displays them at random screen positions. " +
                             "Images auto-dismiss after the duration setting (or audio length if 'Link to Audio' is enabled)."
            },

            ["Visuals"] = new HelpContent
            {
                SectionId = "Visuals",
                Icon = "\uD83D\uDC41",
                Title = "Visual Settings",
                WhatItDoes = "Controls how flash images appear on screen - their size, transparency, " +
                             "fade animations, and how long they stay visible. Fine-tune these settings " +
                             "for either subtle subliminal flashes or prominent, attention-grabbing displays.",
                Tips = new List<string>
                {
                    "70-80% opacity creates subtle, subliminal flashes that don't fully block your view",
                    "Higher fade values create smoother, dreamier transitions between appear/disappear",
                    "Enable 'Link to Audio' to sync image duration with trigger sound files",
                    "100% opacity + 0% fade creates sharp, impactful flashes",
                    "Smaller sizes (50-70%) work well for peripheral vision conditioning"
                },
                HowItWorks = "Size scales images from their original resolution (100% = original, 200% = double). " +
                             "Opacity affects the alpha transparency channel. Fade controls the animation curve " +
                             "for appear/disappear transitions using smooth easing. When 'Link to Audio' is enabled, " +
                             "the Duration slider is ignored and images stay visible for the audio file's length."
            },

            ["Video"] = new HelpContent
            {
                SectionId = "Video",
                Icon = "\uD83C\uDFAC",
                Title = "Mandatory Video",
                WhatItDoes = "Plays fullscreen videos from your assets/videos folder on a schedule. " +
                             "These demand your attention and can include mini-game attention checks " +
                             "to ensure you're actually watching. Supports MP4, WebM, and AVI formats.",
                Tips = new List<string>
                {
                    "Add your videos to the assets/videos folder - the app will pick randomly",
                    "Use the 'Test' button to preview a random video before starting a session",
                    "1-2 videos per hour works well for longer sessions without interruption fatigue",
                    "Strict mode prevents skipping - only enable when you're ready for commitment!",
                    "Videos work best in Borderless Windowed mode for other games/apps"
                },
                HowItWorks = "Videos play in a dedicated fullscreen window that appears above other apps. " +
                             "In Strict mode, the window captures keyboard focus and cannot be closed until " +
                             "the video completes (or attention checks are passed). Audio ducking automatically " +
                             "reduces other application volumes during playback for immersion."
            },

            ["MiniGame"] = new HelpContent
            {
                SectionId = "MiniGame",
                Icon = "\uD83C\uDFAF",
                Title = "Attention Check Mini-Game",
                WhatItDoes = "During mandatory videos, target buttons appear that you must click to prove " +
                             "you're paying attention. Each target displays a phrase and counts down. " +
                             "Miss a target in Strict mode and the video restarts from the beginning!",
                Tips = new List<string>
                {
                    "Start with 3-5 targets per video and increase as you get comfortable",
                    "Longer target duration (8-10 seconds) is more forgiving for beginners",
                    "Enable 'Randomize' for unpredictable target counts each video",
                    "Use the 'Style' button to customize target colors and appearance",
                    "Edit phrases with the 'Phrases' button to personalize your experience"
                },
                HowItWorks = "Targets spawn at random screen positions during video playback based on your " +
                             "settings. Each target shows a customizable phrase with a countdown timer. " +
                             "Clicking the target in time awards XP and continues the video. Missing a target " +
                             "in Strict mode triggers an immediate video restart as a 'penalty'."
            },

            ["Subliminals"] = new HelpContent
            {
                SectionId = "Subliminals",
                Icon = "\uD83D\uDCAD",
                Title = "Subliminal Messages",
                WhatItDoes = "Brief text messages that flash on screen for fractions of a second, " +
                             "optionally accompanied by whispered audio. Designed to deliver suggestions " +
                             "that bypass conscious awareness for deeper conditioning effect.",
                Tips = new List<string>
                {
                    "1-3 frames creates nearly invisible flashes (true subliminal, ~16-48ms)",
                    "5-10 frames creates noticeable but quick flashes you can consciously read",
                    "Enable Audio Whispers for reinforcement through multiple senses",
                    "Use the 'Messages' button to customize text for your personal goals",
                    "Higher frequency (20-30/min) works well during focused activities"
                },
                HowItWorks = "Messages display for the number of 'Frames' you set (1 frame is approximately " +
                             "16 milliseconds at 60fps). Position, font size, and colors can be customized " +
                             "in the Settings popup. When Audio Whispers are enabled, a synthesized voice " +
                             "speaks the message simultaneously with the visual flash."
            },

            ["System"] = new HelpContent
            {
                SectionId = "System",
                Icon = "\u2699",
                Title = "System Settings",
                WhatItDoes = "Controls how the application behaves - startup options, which monitors to use, " +
                             "the panic key for emergency stops, and where your asset files are located. " +
                             "Essential settings for customizing your experience.",
                Tips = new List<string>
                {
                    "Enable 'Win Start' + 'Start Hidden' for automatic background running on boot",
                    "'Multi Monitor' displays effects on all connected screens simultaneously",
                    "The Panic Key (default: Escape) instantly stops ALL effects - learn it!",
                    "Set a custom assets folder to keep your content organized separately",
                    "'No Panic' completely disables the emergency stop - use with EXTREME caution!"
                },
                HowItWorks = "Settings are automatically saved to your AppData folder and persist between " +
                             "sessions. The Panic Key registers a global hotkey that works even when the app " +
                             "isn't focused. Offline Mode disables all network features including update checks, " +
                             "AI chat, and Patreon validation - the app runs completely locally."
            },

            ["Browser"] = new HelpContent
            {
                SectionId = "Browser",
                Icon = "\uD83C\uDF10",
                Title = "Embedded Browser",
                WhatItDoes = "A built-in web browser for accessing BambiCloud and HypnoTube directly " +
                             "within the application. Watch content without leaving the app, with " +
                             "optional integration for audio ducking and session features.",
                Tips = new List<string>
                {
                    "Use 'Pop Out' for a larger, resizable browser window",
                    "Your login sessions are preserved between app restarts",
                    "Browser audio can trigger ducking of system sounds during playback",
                    "Switch between BambiCloud and HypnoTube with the radio buttons"
                },
                HowItWorks = "Uses Microsoft WebView2 (Chromium-based engine) for modern web compatibility. " +
                             "Cookies, login sessions, and browsing data are stored locally in your AppData " +
                             "folder. The browser can detect audio playback to trigger the ducking system " +
                             "if that integration is enabled in Audio settings."
            },

            ["Audio"] = new HelpContent
            {
                SectionId = "Audio",
                Icon = "\uD83D\uDD0A",
                Title = "Audio Settings",
                WhatItDoes = "Controls volume levels for the application and the 'ducking' feature. " +
                             "Ducking automatically lowers the volume of other running applications " +
                             "when videos or audio triggers play, helping you focus on the content.",
                Tips = new List<string>
                {
                    "Duck at 30-50% for subtle background reduction that's less jarring",
                    "Duck at 70-80% to really focus attention on video/trigger audio",
                    "Enable 'Don't duck browser' to keep BambiCloud audio during ducking",
                    "Master volume affects all sounds the app produces",
                    "Video volume is separate so you can balance it against other audio"
                },
                HowItWorks = "Uses the Windows Audio Sessions API to detect running applications and " +
                             "temporarily reduce their volume during video playback or audio triggers. " +
                             "When the video/trigger ends, other application volumes are smoothly restored " +
                             "to their original levels. The browser exclusion prevents double-ducking."
            },

            ["QuickLinks"] = new HelpContent
            {
                SectionId = "QuickLinks",
                Icon = "\uD83D\uDD17",
                Title = "Quick Links & Integrations",
                WhatItDoes = "Fast access to Patreon and Discord authentication, plus community links. " +
                             "Logging in unlocks premium features, progress tracking, and the AI companion. " +
                             "Discord Rich Presence can show your activity to friends.",
                Tips = new List<string>
                {
                    "Patreon login unlocks premium features including AI chat and exclusive content",
                    "Discord login enables XP tracking and leaderboard participation",
                    "Rich Presence (RP) shows what you're doing in your Discord status",
                    "Disable RP if you want to keep your activity private",
                    "Join the Discord community for support, updates, and shared content"
                },
                HowItWorks = "OAuth 2.0 handles secure authentication without the app ever seeing your " +
                             "password - you log in directly through Patreon/Discord's official pages. " +
                             "Tokens are stored encrypted locally. Discord Rich Presence uses Discord's " +
                             "official API to display your current level and session time to friends."
            },

            // ==================== PRESETS TAB ====================

            ["Presets"] = new HelpContent
            {
                SectionId = "Presets",
                Icon = "\uD83D\uDCCB",
                Title = "Presets",
                WhatItDoes = "Save your current settings as a preset for quick recall later. Presets store " +
                             "all your Flash, Video, Subliminal, Audio, and Overlay configurations. " +
                             "Drag and drop to reorder your favorite presets.",
                Tips = new List<string>
                {
                    "Click 'New Preset' to save your current settings with a custom name",
                    "Drag presets to reorder them - your favorite at the front!",
                    "Click a preset card to instantly load those settings",
                    "Built-in presets give you a great starting point",
                    "Export presets to share with friends or backup"
                },
                HowItWorks = "Presets capture a snapshot of all configurable settings at the moment of saving. " +
                             "Loading a preset restores all those values instantly. Custom presets are stored " +
                             "in your settings file and persist between sessions. Built-in presets cannot be " +
                             "modified but can be used as templates for your own."
            },

            ["Sessions"] = new HelpContent
            {
                SectionId = "Sessions",
                Icon = "\uD83C\uDFAF",
                Title = "Sessions",
                WhatItDoes = "Pre-built conditioning sessions with specific goals, durations, and XP rewards. " +
                             "Sessions are more structured than presets - they're designed experiences with " +
                             "difficulty ratings and progression rewards.",
                Tips = new List<string>
                {
                    "Start with 'Easy' sessions and work your way up",
                    "Sessions award bonus XP on completion based on difficulty",
                    "Import custom sessions from the community via .session.json files",
                    "Use the Edit button to customize built-in sessions",
                    "Export your favorite sessions to share with others"
                },
                HowItWorks = "Sessions define a complete conditioning experience including settings, duration, " +
                             "and reward structure. When you start a session, settings are applied and a timer " +
                             "tracks your progress. Completing the full duration awards the listed XP bonus. " +
                             "Sessions can include corner GIFs and special effects."
            },

            ["SessionDetails"] = new HelpContent
            {
                SectionId = "SessionDetails",
                Icon = "\uD83D\uDCDD",
                Title = "Details Panel",
                WhatItDoes = "Shows detailed information about the selected preset or session. For sessions, " +
                             "details are hidden by default to avoid spoilers - click 'Reveal Details' to see " +
                             "the full configuration.",
                Tips = new List<string>
                {
                    "Spoiler protection keeps session surprises hidden until you're ready",
                    "Corner GIF options let you add visual flair during sessions",
                    "Duration shows how long the session is designed to run",
                    "XP Reward shows what you'll earn for completion",
                    "Difficulty badge indicates the intensity level"
                },
                HowItWorks = "The details panel dynamically updates based on your selection. Preset details " +
                             "show all configuration values. Session details use spoiler protection - settings " +
                             "are hidden until you explicitly reveal them. Corner GIFs are small animated " +
                             "images that appear in screen corners during the session."
            },

            // ==================== PROGRESSION TAB ====================

            ["Unlockables"] = new HelpContent
            {
                SectionId = "Unlockables",
                Icon = "\uD83C\uDF81",
                Title = "Unlockables",
                WhatItDoes = "Special features that unlock as you level up! Each feature has a level requirement - " +
                             "keep conditioning to earn XP and unlock new effects like Spiral Overlay, Pink Filter, " +
                             "Bubble Pop, and more advanced features.",
                Tips = new List<string>
                {
                    "Check the level requirement on each locked feature",
                    "Higher-level features are more intense and immersive",
                    "Use the Test button to preview unlocked features",
                    "Each feature has its own settings once unlocked",
                    "Some features require Patreon for full access"
                },
                HowItWorks = "Unlockables are gated by your player level. As you earn XP through conditioning " +
                             "activities, you level up and gain access to new features. Level 10 unlocks basic " +
                             "overlays, higher levels unlock more advanced effects. Once unlocked, features " +
                             "remain available and can be enabled/disabled as desired."
            },

            ["Scheduler"] = new HelpContent
            {
                SectionId = "Scheduler",
                Icon = "\uD83D\uDCC5",
                Title = "Scheduler",
                WhatItDoes = "Automatically start conditioning during specific hours and days of the week. " +
                             "Set your preferred time window and the app will activate when you're ready. " +
                             "Perfect for building consistent habits.",
                Tips = new List<string>
                {
                    "Set times when you're typically free and relaxed",
                    "Select only the days that work for your schedule",
                    "The app must be running for scheduler to work",
                    "Combine with 'Start Hidden' for seamless automation",
                    "Great for establishing a regular conditioning routine"
                },
                HowItWorks = "The scheduler checks every 30 seconds if the current time falls within your " +
                             "configured window. If so, and you're not already in a session, it automatically " +
                             "starts the conditioning engine. The scheduler respects your day-of-week selections " +
                             "and won't activate outside your chosen hours."
            },

            ["IntensityRamp"] = new HelpContent
            {
                SectionId = "IntensityRamp",
                Icon = "\u26A1",
                Title = "Intensity Ramp",
                WhatItDoes = "Gradually increases conditioning intensity over time. Start gentle and build up " +
                             "to your configured maximum over the ramp duration. Link specific features to " +
                             "the ramp for coordinated escalation.",
                Tips = new List<string>
                {
                    "Start with a 30-60 minute ramp to ease into sessions",
                    "The multiplier determines how intense the peak will be",
                    "Link Flash, Spiral, and other features for coordinated buildup",
                    "'End at Complete' stops ramp when duration is reached",
                    "Great for longer, more immersive sessions"
                },
                HowItWorks = "The ramp calculates a multiplier that starts at 1.0 and increases linearly " +
                             "to your target multiplier over the ramp duration. Linked features have their " +
                             "frequency/intensity scaled by this multiplier. For example, if Flash is linked " +
                             "and you set 2x multiplier over 60 minutes, flash rate doubles by the end."
            },

            ["Community"] = new HelpContent
            {
                SectionId = "Community",
                Icon = "\uD83D\uDCAC",
                Title = "Community",
                WhatItDoes = "Quick access to the Discord community server and Discord Rich Presence settings. " +
                             "Join the community for support, updates, shared content, and connecting with " +
                             "other users.",
                Tips = new List<string>
                {
                    "Discord Rich Presence shows your activity to friends",
                    "Disable RP if you want complete privacy",
                    "The Discord server has support channels and announcements",
                    "Share and discover community-created content",
                    "Report bugs and suggest features in Discord"
                },
                HowItWorks = "Rich Presence uses Discord's official API to display that you're using the app. " +
                             "It shows your current level and session time but no sensitive details. " +
                             "The feature only works if Discord is running and you're logged in to both apps."
            },

            ["AppInfo"] = new HelpContent
            {
                SectionId = "AppInfo",
                Icon = "\u2139",
                Title = "App Info",
                WhatItDoes = "Shows the current application version and provides access to update checking. " +
                             "Stay up to date with the latest features, bug fixes, and improvements.",
                Tips = new List<string>
                {
                    "Click 'Check for Updates' to manually check for new versions",
                    "Updates are applied automatically when you restart the app",
                    "The version number helps when reporting bugs",
                    "Major updates may include new features and unlockables"
                },
                HowItWorks = "The app uses Velopack for automatic updates. When an update is available, " +
                             "it downloads in the background and applies when you restart. The version " +
                             "displayed follows semantic versioning (major.minor.patch). Release notes " +
                             "are shown in the update dialog."
            },

            // ==================== QUESTS TAB ====================

            ["Quests"] = new HelpContent
            {
                SectionId = "Quests",
                Icon = "\uD83D\uDCDC",
                Title = "Daily & Weekly Quests",
                WhatItDoes = "Complete challenges to earn bonus XP! Daily quests reset every 24 hours, " +
                             "weekly quests reset every 7 days. Each quest has specific objectives like " +
                             "watching videos, clicking targets, or running sessions.",
                Tips = new List<string>
                {
                    "Daily quests are quick wins - try to complete them each day",
                    "Weekly quests are larger goals with bigger rewards",
                    "Use the Reroll button to get a different quest (limited uses)",
                    "Quest progress is tracked automatically as you use the app",
                    "Completing quests contributes to your overall progression"
                },
                HowItWorks = "Quests track specific activities: videos watched, targets clicked, session time, " +
                             "and more. Progress updates in real-time as you use the app. When you complete " +
                             "the objective, the quest turns green and awards XP immediately. Quests reset " +
                             "at midnight (daily) or Sunday midnight (weekly) in your local timezone."
            },

            ["QuestStats"] = new HelpContent
            {
                SectionId = "QuestStats",
                Icon = "\uD83D\uDCCA",
                Title = "Quest Statistics",
                WhatItDoes = "Track your quest completion history. See how many daily and weekly quests " +
                             "you've completed and the total XP earned from quest rewards.",
                Tips = new List<string>
                {
                    "Daily completions show your consistency",
                    "Weekly completions show your commitment",
                    "Total XP shows your cumulative quest earnings",
                    "Stats persist across sessions and updates"
                },
                HowItWorks = "Statistics are saved to your local settings and sync with your cloud profile " +
                             "if you're logged in. They track lifetime totals, not just current period. " +
                             "The counters increment when quests are marked complete, not when they reset."
            },

            ["Roadmap"] = new HelpContent
            {
                SectionId = "Roadmap",
                Icon = "\uD83D\uDDFA",
                Title = "Transformation Roadmap",
                WhatItDoes = "Long-term transformation tracks with multiple steps and milestones. Each track " +
                             "represents a journey with specific goals. Complete steps to progress and earn " +
                             "badges when you finish a track.",
                Tips = new List<string>
                {
                    "Choose a track that aligns with your goals",
                    "Steps must be completed in order",
                    "Some tracks are locked until you reach certain levels",
                    "Badges show your completed transformations",
                    "Take your time - these are long-term journeys"
                },
                HowItWorks = "Each track contains multiple steps that must be completed sequentially. " +
                             "Steps may require specific activities, session completions, or time commitments. " +
                             "Progress is saved automatically. When you complete all steps in a track, " +
                             "you earn a badge displayed on your profile."
            },

            ["RoadmapStats"] = new HelpContent
            {
                SectionId = "RoadmapStats",
                Icon = "\uD83D\uDCCA",
                Title = "Transformation Progress",
                WhatItDoes = "Track your overall transformation journey statistics. See steps completed, " +
                             "photos submitted, and how long you've been on your transformation journey.",
                Tips = new List<string>
                {
                    "Steps completed shows your overall progress",
                    "Journey days counts from when you started your first track",
                    "Stats motivate continued progress",
                    "Share your achievements in the community"
                },
                HowItWorks = "Statistics aggregate data across all transformation tracks. Journey days " +
                             "are calculated from your first roadmap activity. These stats are tied to " +
                             "your account and sync across devices if logged in."
            },

            // ==================== ASSETS TAB ====================

            ["Assets"] = new HelpContent
            {
                SectionId = "Assets",
                Icon = "\uD83D\uDCE6",
                Title = "Assets & Content Packs",
                WhatItDoes = "Manage your conditioning content - images, videos, and audio files. Download " +
                             "community content packs or organize your own files. The assets folder is where " +
                             "all your conditioning media lives.",
                Tips = new List<string>
                {
                    "Click 'Open Folder' to access your assets directory",
                    "Supported formats: PNG, JPG, GIF for images; MP4, WebM, AVI for videos",
                    "Organize files into subfolders for better management",
                    "Refresh to detect newly added files",
                    "Content packs provide curated collections"
                },
                HowItWorks = "The assets folder is located in AppData by default but can be customized. " +
                             "The app scans this folder for supported media files. Subfolders are supported - " +
                             "the browser shows the folder structure. Files are selected randomly during " +
                             "conditioning based on your active asset preset."
            },

            ["ContentPacks"] = new HelpContent
            {
                SectionId = "ContentPacks",
                Icon = "\uD83C\uDF81",
                Title = "Community Content Packs",
                WhatItDoes = "Download curated content packs created by the community. Packs contain themed " +
                             "collections of images and videos ready to use. Install, activate, and enjoy " +
                             "professionally assembled conditioning content.",
                Tips = new List<string>
                {
                    "Click Install to download a pack (uses bandwidth quota)",
                    "Activate/Deactivate packs to include them in your rotation",
                    "Installed packs show in your asset browser",
                    "Bandwidth resets monthly - check your usage",
                    "Create your own packs and share them!"
                },
                HowItWorks = "Packs are downloaded from the community server and extracted to your assets " +
                             "folder. Each pack has a size shown before download. Your monthly bandwidth " +
                             "quota (shown in the bar) limits total downloads. Deactivating a pack excludes " +
                             "its files from random selection without deleting them."
            },

            ["AssetBrowser"] = new HelpContent
            {
                SectionId = "AssetBrowser",
                Icon = "\uD83D\uDCC2",
                Title = "Asset Browser",
                WhatItDoes = "Browse and select which files to include in your conditioning. The folder tree " +
                             "shows your assets structure, thumbnails preview the content. Create presets to " +
                             "quickly switch between different file selections.",
                Tips = new List<string>
                {
                    "Check/uncheck folders to include/exclude entire directories",
                    "Click thumbnails to preview individual files",
                    "Save your selection as an Asset Preset for quick recall",
                    "Use Select All / Deselect All for bulk operations",
                    "Right-click thumbnails for more options"
                },
                HowItWorks = "The tree view reflects your assets folder structure. Checkboxes control which " +
                             "files are 'active' - only active files are used during conditioning. Asset " +
                             "Presets save your checkbox states for different moods or sessions. The thumbnail " +
                             "grid shows previews of files in the selected folder."
            },

            // ==================== SIDE PANELS ====================

            ["Achievements"] = new HelpContent
            {
                SectionId = "Achievements",
                Icon = "\uD83C\uDFC6",
                Title = "Achievements",
                WhatItDoes = "Complete challenges to unlock achievements! Each achievement represents a " +
                             "milestone - from beginner accomplishments to advanced challenges. Locked " +
                             "achievements are blurred until you earn them.",
                Tips = new List<string>
                {
                    "Hover over locked achievements to see requirements",
                    "Some achievements have secret requirements",
                    "Achievements award XP when unlocked",
                    "Track your progress toward completion",
                    "Show off your achievements in the community"
                },
                HowItWorks = "Achievements track various activities and milestones automatically. When you " +
                             "meet the criteria, the achievement unlocks and awards XP. Progress is saved " +
                             "to your profile. Locked achievements show blurred icons until earned, adding " +
                             "mystery to what you're working toward."
            },

            ["Companions"] = new HelpContent
            {
                SectionId = "Companions",
                Icon = "\uD83D\uDC64",
                Title = "Companions",
                WhatItDoes = "Unlock AI companions as you level up! Each companion has unique personality " +
                             "traits and unlocks at different levels. Assign custom AI personalities to " +
                             "make your companion unique.",
                Tips = new List<string>
                {
                    "Companions unlock at levels 50, 100, 125, and 150",
                    "Click a companion card to select it as active",
                    "Use the personality button to assign custom prompts",
                    "Higher-level companions have more features",
                    "Companions appear in the avatar tube window"
                },
                HowItWorks = "Companions are AI-driven characters that interact with you during sessions. " +
                             "Each has a base personality that can be customized with community prompts. " +
                             "Your active companion appears in the avatar tube and responds to events, " +
                             "provides encouragement, and reacts to your progress."
            },

            ["CommunityPrompts"] = new HelpContent
            {
                SectionId = "CommunityPrompts",
                Icon = "\uD83C\uDFAD",
                Title = "Community Prompts",
                WhatItDoes = "Browse, import, and apply community-created AI personality prompts. Prompts " +
                             "define how your companion speaks, what they say, and their overall personality. " +
                             "Create your own prompts to share with others.",
                Tips = new List<string>
                {
                    "Browse the community library for popular prompts",
                    "Import .prompt files shared by others",
                    "Export your custom prompts to share",
                    "Reset to Default to restore the built-in personality",
                    "Prompts can dramatically change the experience"
                },
                HowItWorks = "Prompts are text instructions that guide the AI companion's responses. " +
                             "They define personality traits, speaking style, and behavior patterns. " +
                             "When you activate a prompt, it's applied to your current companion. " +
                             "The AI uses these instructions to generate contextual responses."
            },

            ["CompanionSettings"] = new HelpContent
            {
                SectionId = "CompanionSettings",
                Icon = "\u2699",
                Title = "Companion Settings",
                WhatItDoes = "Configure how your AI companion behaves. Control visibility, window mode, " +
                             "idle message frequency, and access the AI customization panel for advanced " +
                             "personality tweaking.",
                Tips = new List<string>
                {
                    "Detach the companion window to position it anywhere",
                    "Adjust idle interval to control message frequency",
                    "Longer intervals mean fewer unprompted messages",
                    "'Customize AI' opens advanced personality settings",
                    "Hide companion when you want to focus"
                },
                HowItWorks = "The companion runs in the avatar tube window, either attached to the main " +
                             "window or detached as a separate floating window. The idle interval controls " +
                             "how often the companion sends messages when you're not actively interacting. " +
                             "Customization options let you fine-tune the AI's behavior."
            },

            ["QuickControls"] = new HelpContent
            {
                SectionId = "QuickControls",
                Icon = "\uD83C\uDFAE",
                Title = "Quick Controls",
                WhatItDoes = "Fast toggles for common companion settings. Mute the avatar's speech or " +
                             "whispers without navigating to other settings panels.",
                Tips = new List<string>
                {
                    "Mute Avatar silences all companion speech",
                    "Mute Whispers silences only whispered messages",
                    "Quick toggles don't affect other settings",
                    "Useful during meetings or when you need silence"
                },
                HowItWorks = "Quick controls are shortcuts to settings that exist elsewhere in the app. " +
                             "They provide fast access to frequently-toggled options. Changes made here " +
                             "sync with the main settings and persist between sessions."
            },

            ["PatreonExclusives"] = new HelpContent
            {
                SectionId = "PatreonExclusives",
                Icon = "\u2B50",
                Title = "Patreon Exclusives",
                WhatItDoes = "Premium features available to Patreon supporters. Unlock AI chat, window " +
                             "awareness, haptic device integration, and more. Login with Patreon to " +
                             "activate your benefits.",
                Tips = new List<string>
                {
                    "Login with Patreon to unlock your tier benefits",
                    "Different tiers unlock different features",
                    "AI features require an active Patreon subscription",
                    "Discord login enables additional community features",
                    "Benefits activate immediately after login"
                },
                HowItWorks = "Patreon integration uses OAuth to verify your subscription status. " +
                             "Features are unlocked based on your tier level. The app checks your status " +
                             "periodically and caches it locally. Premium features remain active as long " +
                             "as your subscription is current."
            },

            ["AiChat"] = new HelpContent
            {
                SectionId = "AiChat",
                Icon = "\uD83D\uDCAC",
                Title = "AI Chatbot",
                WhatItDoes = "Have conversations with your AI companion! Double-click the avatar to open " +
                             "a chat window. The AI responds based on its personality and your conversation " +
                             "history. A premium Patreon feature.",
                Tips = new List<string>
                {
                    "Double-click the avatar to start chatting",
                    "The AI remembers your conversation context",
                    "Personality prompts affect chat responses",
                    "Chat works alongside other conditioning features",
                    "Conversations are private and stored locally"
                },
                HowItWorks = "AI Chat uses a language model to generate contextual responses based on " +
                             "your companion's personality prompt and conversation history. Messages are " +
                             "processed through a secure API. Your conversations are stored locally and " +
                             "not shared. The AI can reference your level, session state, and events."
            },

            ["WindowAwareness"] = new HelpContent
            {
                SectionId = "WindowAwareness",
                Icon = "\uD83D\uDC41",
                Title = "Window Awareness",
                WhatItDoes = "Your companion reacts to what you're doing on your computer! It reads the " +
                             "active window title and can comment on your activities. A premium Patreon " +
                             "feature with privacy controls.",
                Tips = new List<string>
                {
                    "The companion only sees window titles, not content",
                    "Set a cooldown to prevent too-frequent comments",
                    "Disable anytime for complete privacy",
                    "Great for immersive, contextual interactions",
                    "Read the privacy notice for full details"
                },
                HowItWorks = "Window Awareness monitors the title of your active window and sends it to " +
                             "the AI companion periodically. The AI generates contextual comments based on " +
                             "what it appears you're doing. Only window titles are read - no screen content, " +
                             "keystrokes, or personal data. Cooldown prevents excessive messages."
            },

            ["Haptics"] = new HelpContent
            {
                SectionId = "Haptics",
                Icon = "\uD83D\uDCF3",
                Title = "Haptic Connection",
                WhatItDoes = "Connect compatible haptic devices for physical feedback during conditioning. " +
                             "Devices can respond to videos, flashes, and other events. A premium Patreon " +
                             "feature supporting multiple device providers.",
                Tips = new List<string>
                {
                    "Check compatibility with your device provider",
                    "Follow setup guide for your specific device",
                    "Intensity can be adjusted per-device",
                    "Test connection before starting a session",
                    "Ensure device is charged and connected"
                },
                HowItWorks = "Haptic integration connects to device APIs through various providers. " +
                             "Events in the app (videos, flashes, etc.) trigger haptic patterns. " +
                             "Intensity and patterns are customizable. The connection runs alongside " +
                             "normal app functions and can be enabled/disabled without affecting other features."
            },

            ["DiscordProfile"] = new HelpContent
            {
                SectionId = "DiscordProfile",
                Icon = "\uD83D\uDC64",
                Title = "Profile Viewer",
                WhatItDoes = "Search and view profiles of other community members. See their level, XP, " +
                             "achievements, and avatar. View your own profile or look up friends by their " +
                             "display name.",
                Tips = new List<string>
                {
                    "Search by Discord display name",
                    "'My Profile' quickly shows your own stats",
                    "Profiles show level, XP, and achievements",
                    "Must be logged in to view profiles",
                    "Respect others' privacy"
                },
                HowItWorks = "Profiles are stored on the community server and linked to Discord accounts. " +
                             "Search queries find matching display names. Profile data includes public " +
                             "stats like level and achievements but no private information. Data syncs " +
                             "when users are online and logged in."
            },

            ["Leaderboard"] = new HelpContent
            {
                SectionId = "Leaderboard",
                Icon = "\uD83C\uDFC6",
                Title = "Leaderboard",
                WhatItDoes = "See how you rank against other community members! The leaderboard shows " +
                             "top players by XP and level. Climb the ranks by earning more XP through " +
                             "conditioning activities.",
                Tips = new List<string>
                {
                    "Click Refresh to update rankings",
                    "Your position is highlighted if you're logged in",
                    "Complete quests and sessions for faster climbing",
                    "Leaderboard updates periodically",
                    "Compete with friends or aim for the top!"
                },
                HowItWorks = "The leaderboard aggregates XP totals from all logged-in users. Rankings " +
                             "are calculated server-side and cached for performance. Your XP syncs when " +
                             "you're online. The board shows top players globally - aim for a spot at " +
                             "the top through consistent conditioning!"
            }
        };

        public static HelpContent GetContent(string sectionId)
        {
            return _content.TryGetValue(sectionId, out var content)
                ? content
                : new HelpContent
                {
                    SectionId = sectionId,
                    Title = "Help",
                    WhatItDoes = "No help content available for this section yet."
                };
        }

        public static bool HasContent(string sectionId) => _content.ContainsKey(sectionId);

        public static IEnumerable<string> GetAllSectionIds() => _content.Keys;
    }
}
