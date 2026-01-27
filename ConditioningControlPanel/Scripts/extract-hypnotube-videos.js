// =============================================================================
// HypnoTube Video Extractor
// =============================================================================
// HOW TO USE:
// 1. Go to https://hypnotube.com/channels/89/bambi-hypnotube/rating/
// 2. Open browser DevTools (F12) -> Console tab
// 3. Paste this entire script and press Enter
// 4. Copy the output (C# code) and paste into BambiSprite.cs
//
// To get more videos, run on page 2, 3 etc. and combine the results
// =============================================================================

(function() {
    const videos = [];

    // Find all video links - they have href starting with /video/
    const videoLinks = document.querySelectorAll('a[href^="/video/"]');

    // Track seen URLs to avoid duplicates (thumbnails and titles link to same video)
    const seenUrls = new Set();

    videoLinks.forEach(link => {
        const href = link.getAttribute('href');
        if (seenUrls.has(href)) return;
        seenUrls.add(href);

        // Get the parent container to find metadata
        const container = link.closest('.thumb-bl, .video-item, .item, [class*="thumb"], [class*="video"]') || link.parentElement;

        // Extract title - try multiple approaches
        let title = link.getAttribute('title') ||
                    link.querySelector('img')?.getAttribute('alt') ||
                    container?.querySelector('.title, .video-title, [class*="title"]')?.textContent?.trim() ||
                    link.textContent?.trim();

        // Clean up title
        if (title) {
            title = title.replace(/\s+/g, ' ').trim();
            // Skip if title is just a number or very short
            if (title.length < 3 || /^\d+$/.test(title)) {
                // Try to extract from URL
                const urlMatch = href.match(/\/video\/(.+)-\d+\.html/);
                if (urlMatch) {
                    title = urlMatch[1].replace(/-/g, ' ').replace(/\b\w/g, c => c.toUpperCase());
                }
            }
        }

        // Extract views - look for number patterns
        const containerText = container?.textContent || '';
        const viewsMatch = containerText.match(/(\d[\d,\s]*)\s*(?:views?|👁)/i) ||
                          containerText.match(/(\d{3,}[\d,\s]*)/);
        const views = viewsMatch ? parseInt(viewsMatch[1].replace(/[,\s]/g, '')) : 0;

        // Extract rating
        const ratingMatch = containerText.match(/(\d{1,3})%/);
        const rating = ratingMatch ? parseInt(ratingMatch[1]) : 0;

        // Extract duration
        const durationMatch = containerText.match(/(\d{1,2}:\d{2}(?::\d{2})?)/);
        const duration = durationMatch ? durationMatch[1] : '';

        // Build full URL
        const fullUrl = 'https://hypnotube.com' + href;

        if (title && title.length > 2) {
            videos.push({
                title: title,
                url: fullUrl,
                views: views,
                rating: rating,
                duration: duration
            });
        }
    });

    // Sort by views (highest first) and take top 30
    videos.sort((a, b) => b.views - a.views);
    const top30 = videos.slice(0, 30);

    // Generate description based on title, views, and rating
    function generateDescription(video) {
        const parts = [];

        // Analyze title for keywords
        const titleLower = video.title.toLowerCase();

        if (titleLower.includes('tiktok')) {
            parts.push('Viral TikTok-style');
        } else if (titleLower.includes('pmv') || titleLower.includes('compilation')) {
            parts.push('PMV compilation');
        } else if (titleLower.includes('hypno') || titleLower.includes('trance')) {
            parts.push('Deep hypnotic');
        } else if (titleLower.includes('sissy')) {
            parts.push('Sissy training');
        } else if (titleLower.includes('bimbo')) {
            parts.push('Bimbo conditioning');
        } else if (titleLower.includes('cock') || titleLower.includes('slut')) {
            parts.push('Intense');
        } else {
            parts.push('Popular');
        }

        // Add popularity indicator
        if (video.views > 500000) {
            parts.push('viral classic');
        } else if (video.views > 200000) {
            parts.push('highly popular');
        } else if (video.views > 100000) {
            parts.push('well-known');
        } else {
            parts.push('community favorite');
        }

        // Add rating if high
        if (video.rating >= 95) {
            parts.push('(top rated)');
        } else if (video.rating >= 90) {
            parts.push('(highly rated)');
        }

        return parts.join(' ');
    }

    // Generate C# code
    let csharpCode = '// === HYPNOTUBE VIDEOS (Auto-extracted) ===\n';
    csharpCode += '// Run date: ' + new Date().toISOString().split('T')[0] + '\n';
    csharpCode += '// Source: https://hypnotube.com/channels/89/bambi-hypnotube/rating/\n\n';

    top30.forEach((video, index) => {
        const escapedTitle = video.title.replace(/"/g, '\\"');
        const description = generateDescription(video);
        const escapedDesc = description.replace(/"/g, '\\"');

        csharpCode += `new("${escapedTitle}", "${escapedDesc}",\n`;
        csharpCode += `    "${video.url}"),\n`;

        if ((index + 1) % 5 === 0) {
            csharpCode += '\n'; // Add spacing every 5 entries
        }
    });

    // Also generate JSON for reference
    const jsonOutput = JSON.stringify(top30, null, 2);

    // Output results
    console.log('='.repeat(80));
    console.log('EXTRACTED ' + top30.length + ' VIDEOS');
    console.log('='.repeat(80));
    console.log('\n📋 C# CODE (copy this into BambiSprite.cs _clickableContent list):\n');
    console.log(csharpCode);
    console.log('\n' + '='.repeat(80));
    console.log('📊 JSON DATA (for reference):\n');
    console.log(jsonOutput);
    console.log('\n' + '='.repeat(80));

    // Copy to clipboard
    if (navigator.clipboard) {
        navigator.clipboard.writeText(csharpCode).then(() => {
            console.log('✅ C# code copied to clipboard!');
        }).catch(() => {
            console.log('⚠️ Could not copy to clipboard - please select and copy manually');
        });
    }

    return { videos: top30, csharpCode, jsonOutput };
})();
