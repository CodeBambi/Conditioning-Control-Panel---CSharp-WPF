using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// A HyperlinkButton whose NavigateUri opens through <see cref="ExternalOpener"/> (so a sandbox never opens a
    /// real site) instead of HyperlinkButton's own launcher call. Looks identical: it keeps HyperlinkButton's theme.
    /// </summary>
    public class SafeHyperlinkButton : HyperlinkButton
    {
        protected override Type StyleKeyOverride => typeof(HyperlinkButton);

        protected override void OnClick()
        {
            // Hide the Uri from the base class for the duration of its OnClick: Click/Command still fire, but its
            // own launch sees nothing to open whatever order it runs in.
            var uri = NavigateUri;
            NavigateUri = null;
            try { base.OnClick(); }
            finally { NavigateUri = uri; }
            if (uri is not null) _ = OpenAsync(uri);
        }

        private async Task OpenAsync(Uri uri)
        {
            // IsVisited as the base sets it: only once the link actually opened.
            if (await ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.OriginalString))
                SetCurrentValue(IsVisitedProperty, true);
        }
    }
}
