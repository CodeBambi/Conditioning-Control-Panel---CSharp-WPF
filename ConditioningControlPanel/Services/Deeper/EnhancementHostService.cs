using System;
using System.IO;
using ConditioningControlPanel.Models.Deeper;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// End-user runtime orchestrator. Holds the currently loaded Enhancement
    /// (one at a time) and binds an EnhancementEngine to whichever playback
    /// surface is active when <see cref="Bind"/> is called.
    ///
    /// In v1 the host is "manual loading" only — the user picks a .ccpenh.json
    /// in the player UI, then drives playback themselves. Auto-discovery from
    /// HT video descriptions is Phase 9.
    /// </summary>
    public sealed class EnhancementHostService : IDisposable
    {
        public Enhancement? LoadedEnhancement { get; private set; }
        public string? LoadedFilePath { get; private set; }

        private EnhancementEngine? _engine;
        private IPlaybackTimeSource? _activeSource;
        private Action? _detachActiveSource;

        public bool IsRunning => _engine?.IsRunning ?? false;

        public event Action<Enhancement?, string?>? Loaded;   // null = unloaded
        public event Action<string>? LoadFailed;              // human-readable reason

        // -- Load / unload -----------------------------------------------------

        public bool LoadFromFile(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    LoadFailed?.Invoke($"File not found: {path}");
                    return false;
                }

                var json = File.ReadAllText(path);
                var enh = EnhancementSerializer.Load(json);
                var issues = EnhancementValidator.Validate(enh);
                // Hard errors block loading; warnings (unknown types etc) are
                // surfaced by the validator but don't fail load - they just
                // never fire at runtime. Creators see warnings in the editor.
                var firstError = issues.Find(i => i.Severity == ValidationSeverity.Error);
                if (firstError != null)
                {
                    LoadFailed?.Invoke($"Validation failed: {firstError.Message}");
                    return false;
                }

                Unload();
                LoadedEnhancement = enh;
                LoadedFilePath = path;
                Loaded?.Invoke(enh, path);
                App.Logger?.Information("Deeper host loaded: {Name} ({Path})",
                    enh.Metadata?.Name ?? "(untitled)", path);
                return true;
            }
            catch (EnhancementLoadException ex)
            {
                LoadFailed?.Invoke(ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementHostService.LoadFromFile failed");
                LoadFailed?.Invoke(ex.Message);
                return false;
            }
        }

        public void Unload()
        {
            UnbindEngine();
            if (LoadedEnhancement != null)
            {
                LoadedEnhancement = null;
                LoadedFilePath = null;
                try { Loaded?.Invoke(null, null); } catch { }
                App.Logger?.Information("Deeper host unloaded");
            }
        }

        // -- Engine binding ----------------------------------------------------

        /// <summary>
        /// Bind the loaded enhancement to a playback time source and start the
        /// engine. Caller owns the source; host stops the engine but does not
        /// dispose the source. <paramref name="attach"/> runs after the source
        /// is registered; <paramref name="detach"/> runs on Unbind/Stop.
        ///
        /// No-op if no enhancement is loaded.
        /// </summary>
        public bool Bind(IPlaybackTimeSource source, Action? attach = null, Action? detach = null)
        {
            if (LoadedEnhancement == null) return false;
            UnbindEngine();

            try
            {
                attach?.Invoke();
                _activeSource = source;
                _detachActiveSource = detach;

                var dispatcher = new RealActionDispatcher();
                var webcam = (App.Webcam?.IsRunning ?? false) ? App.Webcam : null;
                _engine = new EnhancementEngine(LoadedEnhancement, source, dispatcher, webcam);
                _engine.Start();
                App.Logger?.Information("Deeper engine bound and started");
                return true;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "EnhancementHostService.Bind failed");
                UnbindEngine();
                return false;
            }
        }

        public void UnbindEngine()
        {
            try
            {
                _engine?.Stop();
                _engine?.Dispose();
            }
            catch { }
            _engine = null;
            try { _detachActiveSource?.Invoke(); } catch { }
            _detachActiveSource = null;
            _activeSource = null;
        }

        public void Dispose()
        {
            UnbindEngine();
            LoadedEnhancement = null;
        }

        // -- Source matching helper -------------------------------------------

        /// <summary>
        /// Returns true if the loaded enhancement's media_source pattern
        /// matches the given path/url. "*" matches anything; otherwise a
        /// case-insensitive substring match (lightweight v1 — globbing can
        /// come later if creators ask for it).
        /// </summary>
        public bool MatchesCurrentSource(string? path)
        {
            var pattern = LoadedEnhancement?.MediaSource;
            if (string.IsNullOrEmpty(pattern)) return false;
            if (pattern == "*") return true;
            if (string.IsNullOrEmpty(path)) return false;
            // Strip a trailing wildcard if present.
            var p = pattern.TrimEnd('*');
            return path.Contains(p, StringComparison.OrdinalIgnoreCase);
        }
    }
}
