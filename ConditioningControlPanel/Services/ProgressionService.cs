using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// Handles XP, leveling, and unlockables.
    /// Will be expanded in future sessions.
    /// </summary>
    public class ProgressionService
    {
        public event EventHandler<int>? LevelUp;
        public event EventHandler<double>? XPChanged;

        public void AddXP(double amount)
        {
            var settings = App.Settings.Current;
            settings.PlayerXP += amount;
            
            // Check for level up
            var xpNeeded = GetXPForLevel(settings.PlayerLevel);
            while (settings.PlayerXP >= xpNeeded)
            {
                settings.PlayerXP -= xpNeeded;
                settings.PlayerLevel++;
                xpNeeded = GetXPForLevel(settings.PlayerLevel);
                LevelUp?.Invoke(this, settings.PlayerLevel);
                App.Logger.Information("Level up! Now level {Level}", settings.PlayerLevel);
            }
            
            XPChanged?.Invoke(this, settings.PlayerXP);
        }

        public double GetXPForLevel(int level)
        {
            return 50 + (level * 20);
        }

        public string GetTitle(int level)
        {
            return level switch
            {
                < 5 => "Beginner Bimbo",
                < 10 => "Training Bimbo",
                < 20 => "Eager Bimbo",
                < 30 => "Devoted Bimbo",
                < 50 => "Advanced Bimbo",
                _ => "Perfect Bimbo"
            };
        }

        public bool IsUnlocked(string feature, int currentLevel)
        {
            return feature switch
            {
                "spiral" => currentLevel >= 10,
                "pink_filter" => currentLevel >= 10,
                "bubbles" => currentLevel >= 20,
                _ => true
            };
        }
    }
}
