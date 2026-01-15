using System;
using System.Windows.Threading;
using DiscordRPC;
using DiscordRPC.Logging;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Service for Discord Rich Presence integration.
/// Shows user's activity status in Discord.
/// </summary>
public class DiscordRichPresenceService : IDisposable
{
    // Discord Application ID - Create at https://discord.com/developers/applications
    private const string ApplicationId = "1461012135982403696";

    private DiscordRpcClient? _client;
    private bool _disposed;
    private bool _isEnabled;
    private DateTime _sessionStartTime;
    private string _currentState = "Idle";
    private string _currentDetails = "In the app";
    private readonly DispatcherTimer _updateTimer;

    public bool IsConnected => _client?.IsInitialized == true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                if (value)
                    Connect();
                else
                    Disconnect();
            }
        }
    }

    public DiscordRichPresenceService()
    {
        _sessionStartTime = DateTime.UtcNow;

        // Update presence every 15 seconds
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _updateTimer.Tick += (s, e) => UpdatePresence();
    }

    /// <summary>
    /// Connect to Discord RPC
    /// </summary>
    public void Connect()
    {
        if (_client != null && _client.IsInitialized)
            return;

        try
        {
            _client = new DiscordRpcClient(ApplicationId)
            {
                Logger = new ConsoleLogger { Level = LogLevel.Warning }
            };

            _client.OnReady += (sender, e) =>
            {
                App.Logger?.Information("Discord RPC connected: {Username}", e.User.Username);
            };

            _client.OnError += (sender, e) =>
            {
                App.Logger?.Warning("Discord RPC error: {Message}", e.Message);
            };

            _client.OnConnectionFailed += (sender, e) =>
            {
                App.Logger?.Warning("Discord RPC connection failed on pipe {Pipe}. Make sure Discord is running.", e.FailedPipe);
            };

            _client.OnConnectionEstablished += (sender, e) =>
            {
                App.Logger?.Information("Discord RPC connection established on pipe {Pipe}", e.ConnectedPipe);
            };

            _client.Initialize();
            _sessionStartTime = DateTime.UtcNow;
            _updateTimer.Start();

            // Give Discord a moment to connect, then set initial presence
            System.Threading.Tasks.Task.Delay(1000).ContinueWith(_ =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    if (_client?.IsInitialized == true)
                    {
                        UpdatePresence();
                        App.Logger?.Information("Discord Rich Presence connected and presence set");
                    }
                });
            });

            App.Logger?.Information("Discord Rich Presence initializing...");
        }
        catch (Exception ex)
        {
            App.Logger?.Error(ex, "Failed to initialize Discord RPC");
        }
    }

    /// <summary>
    /// Disconnect from Discord RPC
    /// </summary>
    public void Disconnect()
    {
        _updateTimer.Stop();

        if (_client != null)
        {
            try
            {
                _client.ClearPresence();
                _client.Dispose();
            }
            catch { }
            _client = null;
        }

        App.Logger?.Information("Discord Rich Presence disabled");
    }

    /// <summary>
    /// Update the current activity state
    /// </summary>
    public void SetActivity(string state, string? details = null)
    {
        _currentState = state;
        if (details != null)
            _currentDetails = details;

        UpdatePresence();
    }

    /// <summary>
    /// Set activity for session mode
    /// </summary>
    public void SetSessionActivity(string sessionName)
    {
        _currentState = "In Session";
        _currentDetails = sessionName;
        _sessionStartTime = DateTime.UtcNow;
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for idle/browsing
    /// </summary>
    public void SetIdleActivity()
    {
        _currentState = "Browsing";
        _currentDetails = "Exploring the app";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for watching video
    /// </summary>
    public void SetVideoActivity()
    {
        _currentState = "Watching";
        _currentDetails = "Mandatory viewing";
        UpdatePresence();
    }

    /// <summary>
    /// Set activity for flash/conditioning
    /// </summary>
    public void SetFlashActivity()
    {
        _currentState = "Conditioning";
        _currentDetails = "Flash training";
        UpdatePresence();
    }

    private void UpdatePresence()
    {
        if (_client == null || !_client.IsInitialized || !_isEnabled)
            return;

        try
        {
            var presence = new RichPresence
            {
                Details = _currentDetails,
                State = _currentState,
                Timestamps = new Timestamps
                {
                    Start = _sessionStartTime
                }
            };

            // Only add assets if images are uploaded to Discord Developer Portal
            // To add images: Discord Developer Portal > Your App > Rich Presence > Art Assets
            // Upload images with keys: "app_icon", "session", "video", "flash", "idle"
            // Uncomment below once images are uploaded:
            // presence.Assets = new Assets
            // {
            //     LargeImageKey = "app_icon",
            //     LargeImageText = "Conditioning Control Panel",
            //     SmallImageKey = GetSmallImageKey(),
            //     SmallImageText = _currentState
            // };

            _client.SetPresence(presence);
            App.Logger?.Debug("Discord presence updated: {Details} - {State}", _currentDetails, _currentState);
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("Failed to update Discord presence: {Error}", ex.Message);
        }
    }

    private string GetSmallImageKey()
    {
        return _currentState.ToLower() switch
        {
            "in session" => "session",
            "watching" => "video",
            "conditioning" => "flash",
            _ => "idle"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Disconnect();
        GC.SuppressFinalize(this);
    }
}
