// =============================================================================
// BambiCloud Playlist Extractor
// =============================================================================
// HOW TO USE:
// 1. Go to https://bambicloud.com/ (or the playlists section)
// 2. Open browser DevTools (F12) -> Console tab
// 3. Paste this entire script and press Enter
// 4. Copy the output (C# code) and paste into BambiSprite.cs
// =============================================================================

(function() {
    const playlists = [];

    // BambiCloud uses various structures - try multiple selectors
    // Look for playlist links (typically /playlist/ in URL)
    const playlistLinks = document.querySelectorAll('a[href*="/playlist/"]');
    const seenUrls = new Set();

    playlistLinks.forEach(link => {
        const href = link.getAttribute('href');
        if (!href) return;

        // Normalize URL
        const fullUrl = href.startsWith('http') ? href : 'https://bambicloud.com' + href;
        if (seenUrls.has(fullUrl)) return;
        seenUrls.add(fullUrl);

        // Get title from link text, title attribute, or nearby elements
        const container = link.closest('.playlist-item, .card, .item, [class*="playlist"]') || link.parentElement;
        let title = link.getAttribute('title') ||
                    link.textContent?.trim() ||
                    container?.querySelector('.title, .name, h3, h4')?.textContent?.trim();

        if (!title || title.length < 2) return;

        // Clean title
        title = title.replace(/\s+/g, ' ').trim();

        // Try to get description or file count
        const containerText = container?.textContent || '';
        const fileCountMatch = containerText.match(/(\d+)\s*(?:files?|tracks?|sessions?)/i);
        const fileCount = fileCountMatch ? parseInt(fileCountMatch[1]) : 0;

        playlists.push({
            title: title,
            url: fullUrl,
            fileCount: fileCount
        });
    });

    // If no playlist links found, try finding any content cards
    if (playlists.length === 0) {
        console.log('No /playlist/ links found. Looking for general content cards...');

        // Try generic card selectors
        const cards = document.querySelectorAll('.card, .playlist, .item, [class*="card"], [class*="playlist"]');
        cards.forEach(card => {
            const link = card.querySelector('a');
            if (!link) return;

            const href = link.getAttribute('href');
            if (!href || seenUrls.has(href)) return;
            seenUrls.add(href);

            const title = card.querySelector('.title, h3, h4, .name')?.textContent?.trim() ||
                         link.textContent?.trim();

            if (title && title.length > 2) {
                const fullUrl = href.startsWith('http') ? href : 'https://bambicloud.com' + href;
                playlists.push({
                    title: title,
                    url: fullUrl,
                    fileCount: 0
                });
            }
        });
    }

    // Generate description based on title
    function generateDescription(playlist) {
        const titleLower = playlist.title.toLowerCase();

        if (titleLower.includes('20 day') || titleLower.includes('challenge')) {
            return 'Complete transformation journey playlist';
        } else if (titleLower.includes('rapid') || titleLower.includes('quick')) {
            return 'Quick conditioning sessions';
        } else if (titleLower.includes('deep') || titleLower.includes('sleep')) {
            return 'Deep relaxation and conditioning';
        } else if (titleLower.includes('beginner') || titleLower.includes('basic') || titleLower.includes('starter')) {
            return 'Perfect for beginners';
        } else if (titleLower.includes('advanced') || titleLower.includes('intense')) {
            return 'Advanced conditioning sessions';
        } else if (titleLower.includes('loop') || titleLower.includes('continuous')) {
            return 'Continuous loop playlist';
        } else if (titleLower.includes('cock') || titleLower.includes('slut')) {
            return 'Intense conditioning content';
        } else if (titleLower.includes('iq') || titleLower.includes('bimbo')) {
            return 'Bimbo transformation content';
        } else if (playlist.fileCount > 0) {
            return `Collection of ${playlist.fileCount} conditioning files`;
        } else {
            return 'Curated conditioning playlist';
        }
    }

    // Take top 15 playlists
    const topPlaylists = playlists.slice(0, 15);

    // Generate C# code
    let csharpCode = '// === BAMBICLOUD PLAYLISTS (Auto-extracted) ===\n';
    csharpCode += '// Run date: ' + new Date().toISOString().split('T')[0] + '\n';
    csharpCode += '// Source: https://bambicloud.com/\n\n';

    topPlaylists.forEach((playlist, index) => {
        const escapedTitle = playlist.title.replace(/"/g, '\\"');
        const description = generateDescription(playlist);
        const escapedDesc = description.replace(/"/g, '\\"');

        csharpCode += `new("${escapedTitle}", "${escapedDesc}",\n`;
        csharpCode += `    "${playlist.url}"),\n`;

        if ((index + 1) % 5 === 0) {
            csharpCode += '\n';
        }
    });

    // Output results
    console.log('='.repeat(80));
    console.log('EXTRACTED ' + topPlaylists.length + ' PLAYLISTS');
    console.log('='.repeat(80));
    console.log('\n📋 C# CODE (copy this into BambiSprite.cs _clickableContent list):\n');
    console.log(csharpCode);
    console.log('\n' + '='.repeat(80));

    // Copy to clipboard
    if (navigator.clipboard) {
        navigator.clipboard.writeText(csharpCode).then(() => {
            console.log('✅ C# code copied to clipboard!');
        }).catch(() => {
            console.log('⚠️ Could not copy to clipboard - please select and copy manually');
        });
    }

    // Debug: show what we found
    console.log('\n📊 Found playlists:', topPlaylists);

    return { playlists: topPlaylists, csharpCode };
})();
