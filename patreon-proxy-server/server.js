/**
 * Patreon OAuth + OpenRouter Proxy Server
 *
 * This server handles:
 * 1. Patreon OAuth flow (keeps client_secret secure)
 * 2. Subscription tier validation
 * 3. Proxied AI requests via OpenRouter (keeps API key secure)
 *
 * Deploy to: Railway, Render, Vercel, or any Node.js host
 */

const express = require('express');
const cors = require('cors');
const crypto = require('crypto');
const { Redis } = require('@upstash/redis');

const EMAIL_HASH_KEY = process.env.EMAIL_HASH_KEY || (() => {
    if (process.env.VERCEL || process.env.NODE_ENV === 'production') {
        throw new Error('EMAIL_HASH_KEY must be set in production');
    }
    console.warn('[STARTUP WARNING] EMAIL_HASH_KEY not set — using dev fallback. DO NOT run in production without it.');
    return 'dev-fallback-key';
})();
function hashEmail(email) {
    if (!email) return null;
    return crypto.createHmac('sha256', EMAIL_HASH_KEY).update(email.toLowerCase()).digest('hex');
}

/**
 * Hash a password using scrypt with a random salt.
 * Returns "salt:hash" string for storage.
 */
async function hashPassword(password) {
    const salt = crypto.randomBytes(16).toString('hex');
    const hash = await new Promise((resolve, reject) => {
        crypto.scrypt(password, salt, 64, (err, key) => {
            if (err) reject(err);
            else resolve(key.toString('hex'));
        });
    });
    return `${salt}:${hash}`;
}

/**
 * Verify a password against a stored "salt:hash" string.
 * Uses timing-safe comparison to prevent timing attacks.
 */
async function verifyPassword(password, stored) {
    const [salt, hash] = stored.split(':');
    if (!salt || !hash) return false;
    const verify = await new Promise((resolve, reject) => {
        crypto.scrypt(password, salt, 64, (err, key) => {
            if (err) reject(err);
            else resolve(key.toString('hex'));
        });
    });
    return timingSafeCompare(verify, hash);
}

const app = express();

// Trust Vercel's reverse proxy so req.ip returns the real client IP (not proxy IP)
app.set('trust proxy', 1);

// Remove Express framework fingerprint
app.disable('x-powered-by');

// Security headers
app.use((req, res, next) => {
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Strict-Transport-Security', 'max-age=31536000; includeSubDomains');
    // R11/M1: Prevent caching of authenticated responses
    res.setHeader('Cache-Control', 'no-store, no-cache, must-revalidate, private');
    res.setHeader('Pragma', 'no-cache');
    // R12/M16: Additional security headers
    res.setHeader('X-Frame-Options', 'DENY');
    res.setHeader('Referrer-Policy', 'strict-origin-when-cross-origin');
    res.setHeader('Permissions-Policy', 'camera=(), microphone=(), geolocation=()');
    next();
});

app.use(cors({
    origin: ['https://codebambi.github.io'],
    methods: ['GET', 'POST', 'OPTIONS'],
    allowedHeaders: ['Content-Type', 'X-Auth-Token', 'X-Admin-Token', 'X-CCP-Timestamp', 'X-CCP-Signature', 'X-Client-Version']
}));
app.use(express.json({
    limit: '1mb',
    verify: (req, res, buf) => {
        // Save raw body for HMAC verification on sync endpoint
        if (req.url && req.url.startsWith('/v2/user/sync')) {
            req.rawBody = buf.toString('utf8');
        }
    }
}));

// =============================================================================
// SECURITY HELPERS
// =============================================================================

/**
 * Timing-safe admin token verification.
 * Prevents timing attacks that could leak token characters via response time differences.
 */
function verifyAdminToken(provided) {
    const expected = process.env.ADMIN_TOKEN;
    if (!expected || !provided) return false;
    if (typeof provided !== 'string') return false;
    try {
        // Hash both tokens to normalize length — prevents timing-based length leakage.
        // Without this, mismatched lengths return early and reveal the expected token's length.
        const a = crypto.createHash('sha256').update(provided).digest();
        const b = crypto.createHash('sha256').update(expected).digest();
        return crypto.timingSafeEqual(a, b);
    } catch {
        return false;
    }
}

/**
 * Timing-safe comparison for arbitrary strings (HMAC signatures, etc).
 */
function timingSafeCompare(a, b) {
    if (typeof a !== 'string' || typeof b !== 'string') return false;
    try {
        const bufA = Buffer.from(a, 'utf8');
        const bufB = Buffer.from(b, 'utf8');
        if (bufA.length !== bufB.length) return false;
        return crypto.timingSafeEqual(bufA, bufB);
    } catch {
        return false;
    }
}

/**
 * Validate unified_id format to prevent Redis key injection.
 */
function isValidUnifiedId(id) {
    return typeof id === 'string' && /^u_[a-z0-9]{8,24}$/.test(id);
}

/**
 * Sanitize a string for safe logging (prevent log injection via newlines/control chars).
 */
function sanitizeForLog(str) {
    if (typeof str !== 'string') return String(str);
    return str.replace(/[\r\n\t\x00-\x1f\x7f]/g, '');
}

/**
 * Validate display name characters (strict allowlist).
 * Only allows alphanumeric, spaces, hyphens, underscores, periods, and common Unicode letters.
 */
const DISPLAY_NAME_INVALID_CHARS = /[^a-zA-Z0-9\u00C0-\u024F\u0400-\u04FF\u3000-\u9FFF _.\-]/;
function isValidDisplayNameChars(name) {
    return !DISPLAY_NAME_INVALID_CHARS.test(name);
}

// =============================================================================
// SKILL CATALOG — server-authoritative skill definitions for purchase validation
// =============================================================================
const SKILL_CATALOG = {
    'pink_hours':         { cost: 2,  prereq: null },
    'ditzy_data':         { cost: 5,  prereq: 'pink_hours' },
    'sparkle_boost_1':    { cost: 8,  prereq: 'pink_hours' },
    'good_girl_streak':   { cost: 5,  prereq: 'pink_hours' },
    'hive_mind':          { cost: 10, prereq: 'ditzy_data' },
    'trophy_case':        { cost: 10, prereq: 'hive_mind' },
    'sparkle_boost_2':    { cost: 15, prereq: 'sparkle_boost_1' },
    'lucky_bimbo':        { cost: 15, prereq: 'sparkle_boost_2' },
    'milestone_rewards':  { cost: 15, prereq: 'good_girl_streak' },
    'oopsie_insurance':   { cost: 12, prereq: 'milestone_rewards' },
    'popular_girl':       { cost: 15, prereq: 'trophy_case' },
    'quest_refresh':      { cost: 15, prereq: 'popular_girl' },
    'better_quests':      { cost: 20, prereq: 'quest_refresh' },
    'sparkle_boost_3':    { cost: 25, prereq: 'lucky_bimbo' },
    'lucky_bubbles':      { cost: 20, prereq: 'sparkle_boost_3' },
    'pink_rush':          { cost: 25, prereq: 'lucky_bubbles' },
    'streak_power':       { cost: 20, prereq: 'oopsie_insurance' },
    'reroll_addict':      { cost: 15, prereq: 'streak_power' },
    'perfect_bimbo_week': { cost: 20, prereq: 'reroll_addict' },
    'night_shift':        { cost: 10, prereq: null },
    'early_bird_bimbo':   { cost: 10, prereq: null },
    'eternal_doll':       { cost: 8,  prereq: null },
};
const POINTS_PER_LEVEL = 1;

/**
 * Filter a user object for safe API response (strip auth_token_hash, email_hash, internal fields).
 * Used by V1 auth endpoints to prevent leaking sensitive server-side data.
 */
function filterUserForResponse(user) {
    if (!user || typeof user !== 'object') return user;
    return {
        unified_id: user.unified_id,
        display_name: user.display_name,
        level: user.level,
        xp: user.xp,
        current_season: user.current_season,
        highest_level_ever: user.highest_level_ever,
        is_season0_og: user.is_season0_og,
        patreon_tier: user.patreon_tier,
        patreon_is_active: user.patreon_is_active,
        patreon_is_whitelisted: user.patreon_is_whitelisted,
        achievements: user.achievements,
        unlocks: user.unlocks,
        stats: user.stats,
        discord_id: user.discord_id || null,
        patreon_id: user.patreon_id || null,
        created_at: user.created_at,
        updated_at: user.updated_at
    };
}

/**
 * Filter a user object for admin API responses — includes most fields but strips
 * password_hash, auth_token_hash, and xp_rate (security-sensitive internal data).
 */
function filterUserForAdmin(user) {
    if (!user || typeof user !== 'object') return user;
    // Whitelist approach: only include fields admins need for debugging/management.
    // New fields added to user records will NOT auto-leak.
    return {
        unified_id: user.unified_id,
        display_name: user.display_name,
        display_name_set_at: user.display_name_set_at,
        level: user.level,
        xp: user.xp,
        current_season: user.current_season,
        highest_level_ever: user.highest_level_ever,
        is_season0_og: user.is_season0_og,
        patreon_tier: user.patreon_tier,
        patreon_is_active: user.patreon_is_active,
        patreon_is_whitelisted: user.patreon_is_whitelisted,
        patreon_id: user.patreon_id || null,
        discord_id: user.discord_id || null,
        achievements: user.achievements,
        unlocked_skills: user.unlocked_skills,
        skill_points: user.skill_points,
        stats: user.stats,
        all_time_stats: user.all_time_stats,
        quest_stats: user.quest_stats,
        companion_progress: user.companion_progress,
        total_conditioning_minutes: user.total_conditioning_minutes,
        unlocks: user.unlocks,
        anti_cheat_flags: user.anti_cheat_flags,
        auth_method: user.auth_method,
        avatar_url: user.avatar_url,
        app_version: user.app_version,
        show_online_status: user.show_online_status,
        allow_discord_dm: user.allow_discord_dm,
        last_seen: user.last_seen,
        last_heartbeat: user.last_heartbeat,
        created_at: user.created_at,
        updated_at: user.updated_at,
        bonus_daily_rerolls: user.bonus_daily_rerolls,
        bonus_weekly_rerolls: user.bonus_weekly_rerolls,
        oopsie_used_season: user.oopsie_used_season,
        force_skills_reset: user.force_skills_reset,
        force_streak_override: user.force_streak_override,
        // provider_data intentionally excluded — may contain OAuth tokens/PII
    };
}

/**
 * Filter a legacy profile object for safe API response (strip email_hash, internal metadata).
 * Used by V1 profile/sync endpoints.
 */
function filterProfileForResponse(profile) {
    if (!profile || typeof profile !== 'object') return profile;
    return {
        display_name: profile.display_name || null,
        xp: profile.xp || 0,
        level: profile.level || 1,
        achievements: profile.achievements || [],
        stats: profile.stats || {},
        updated_at: profile.updated_at,
        last_session: profile.last_session,
        patreon_tier: profile.patreon_tier,
        patreon_is_active: profile.patreon_is_active,
        patreon_is_whitelisted: profile.patreon_is_whitelisted,
        discord_id: profile.discord_id || null,
        avatar_url: profile.avatar_url || null,
        allow_discord_dm: profile.allow_discord_dm || false,
        share_profile_picture: profile.share_profile_picture || false,
        show_online_status: profile.show_online_status !== false,
        reset_weekly_quest: profile.reset_weekly_quest || false,
        reset_daily_quest: profile.reset_daily_quest || false
    };
}

/**
 * Safe leaderboard score: guards against NaN/Infinity before Redis ZADD.
 * Returns 0 if value is not a valid finite number.
 */
function safeLeaderboardScore(value) {
    const num = Number(value);
    if (!isFinite(num) || isNaN(num)) return 0;
    return Math.min(Number.MAX_SAFE_INTEGER, Math.max(0, num));
}

/**
 * Track user online status via sorted set (score = timestamp).
 * Called from heartbeat endpoints. Allows O(log N) online count queries.
 * Only tracks users with show_online_status enabled (default true).
 */
async function trackUserOnline(unifiedId, showOnlineStatus) {
    if (!redis || !unifiedId) return;
    try {
        if (showOnlineStatus === false) {
            // User opted out — remove from online set
            await redis.zrem('online_users', unifiedId);
        } else {
            await redis.zadd('online_users', { score: Date.now(), member: unifiedId });
        }
    } catch (e) {
        // Non-critical — don't fail the request
    }
}

/**
 * Get count of currently online users (seen within last 3 minutes).
 * Uses sorted set for O(log N) count instead of scanning all users.
 */
async function getOnlineUserCount() {
    if (!redis) return 0;
    try {
        const threshold = Date.now() - 180000; // 3 minutes
        return await redis.zcount('online_users', threshold, '+inf');
    } catch (e) {
        return 0;
    }
}

/**
 * Get total registered user count from counter key.
 * Falls back to leaderboard zcard if counter not yet seeded.
 */
async function getTotalUserCount() {
    if (!redis) return 0;
    try {
        const count = await redis.get('global:total_users');
        if (count !== null) return parseInt(count, 10) || 0;
        return null; // Not seeded yet — caller should fall back
    } catch (e) {
        return 0;
    }
}

/**
 * Increment total user counter (called on user registration).
 */
async function incrementTotalUserCount() {
    if (!redis) return;
    try {
        await redis.incr('global:total_users');
    } catch (e) {
        // Non-critical
    }
}

/**
 * Enforce a Redis-backed cooldown on admin SCAN operations to prevent DoS.
 * Returns null if allowed, or an error response object if on cooldown.
 */
async function enforceAdminScanCooldown(res, operationName) {
    if (!redis) return null;
    const cooldownKey = `admin_scan_cooldown:${operationName}`;
    const onCooldown = await redis.get(cooldownKey);
    if (onCooldown) {
        return res.status(429).json({ error: `${operationName} cooldown active. Try again in 30 seconds.`, retry_after: 30 });
    }
    await redis.set(cooldownKey, '1', { ex: 30 });
    return null;
}

// =============================================================================
// REQUEST LOGGING MIDDLEWARE
// =============================================================================

// Track request counts per IP for rate limiting and anomaly detection
const requestCounts = new Map();
const RATE_WINDOW_MS = 60000; // 1 minute window

// Rate limit thresholds
const IP_RATE_LIMIT = 50;            // Max requests per IP per minute before blocking
const IP_AUTH_FAIL_LIMIT = 10;       // Max auth failures per IP per minute before blocking
const IP_BLOCK_DURATION_MS = 1800000; // Block IP for 30 minutes after exceeding limits

// In-memory fallback for auth-exempt rate limiting when Redis is unavailable
const authRateFallback = new Map();
const AUTH_RATE_FALLBACK_MAX = 5000;
const AUTH_RATE_FALLBACK_WINDOW = 30000; // 30s window (matches Redis TTL)
const AUTH_RATE_FALLBACK_LIMIT = 10;     // matches Redis limit
setInterval(() => {
    const cutoff = Date.now() - AUTH_RATE_FALLBACK_WINDOW * 2;
    for (const [key, entry] of authRateFallback) {
        if (entry.firstSeen < cutoff) authRateFallback.delete(key);
    }
    if (authRateFallback.size > AUTH_RATE_FALLBACK_MAX) {
        let removed = 0;
        for (const key of authRateFallback.keys()) {
            if (removed >= authRateFallback.size - AUTH_RATE_FALLBACK_MAX) break;
            authRateFallback.delete(key);
            removed++;
        }
    }
}, 60000);

// Auth endpoints exempt from global IP rate limiter (have their own per-IP cooldown instead)
// These are one-shot login/registration operations that shouldn't be collaterally blocked
// by attack traffic sharing the same IP (residential botnet / CGNAT)
const AUTH_EXEMPT_PATHS = new Set([
    '/patreon/authorize',
    '/patreon/token',
    '/patreon/refresh',
    '/patreon/validate',
    '/v2/auth/patreon',
    '/v2/auth/discord',
    '/v2/auth/restore-session',
    '/v2/auth/register',
    '/v2/auth/login',
    '/discord/validate',
    '/auth/register',
    '/auth/link-provider'
]);

// Clean up old entries every 5 minutes, cap Map size at 10,000 entries
const MAX_REQUEST_COUNT_ENTRIES = 10000;
setInterval(() => {
    const now = Date.now();
    const cutoff = now - RATE_WINDOW_MS * 5;
    for (const [key, entry] of requestCounts) {
        // Remove entries that are both expired AND not currently blocked
        if (entry.firstSeen < cutoff && (!entry.blockedUntil || entry.blockedUntil < now)) {
            requestCounts.delete(key);
        }
    }
    // Hard cap: if still over limit, evict oldest entries (R11/M4: skip currently blocked IPs)
    if (requestCounts.size > MAX_REQUEST_COUNT_ENTRIES) {
        const excess = requestCounts.size - MAX_REQUEST_COUNT_ENTRIES;
        let removed = 0;
        const now2 = Date.now();
        for (const [key, entry] of requestCounts) {
            if (removed >= excess) break;
            // Don't evict IPs that are currently blocked
            if (entry.blockedUntil && entry.blockedUntil > now2) continue;
            requestCounts.delete(key);
            removed++;
        }
    }
}, 300000);

app.use(async (req, res, next) => {
    const start = Date.now();
    // Use req.ip which respects trust proxy setting (Vercel appends real client IP)
    const ip = req.ip || 'unknown';
    const ua = sanitizeForLog((req.headers['user-agent'] || 'none'));
    const unifiedId = sanitizeForLog(req.body?.unified_id || req.query?.unified_id || '-');
    const hasAuthToken = !!req.headers['x-auth-token'];
    const clientVersion = req.headers['x-client-version'] || null;

    // Static read-only endpoints bypass rate limiter entirely — no auth, no Redis,
    // no computational cost. Safe to serve unconditionally.
    if (req.method === 'GET' && req.path === '/packs/manifest') {
        return next();
    }

    // Auth endpoints bypass global IP rate limiter — protected by their own per-IP
    // rate limit instead. This prevents legitimate users from being collaterally blocked
    // when sharing IPs with attack traffic (residential botnet / CGNAT / VPN).
    // Limit: 10 auth requests per IP per 30s window (shared across all auth endpoints).
    if (AUTH_EXEMPT_PATHS.has(req.path)) {
        let redisRateLimited = false;
        if (redis) {
            try {
                // Check if IP is globally blocked (shared with main rate limiter)
                const blocked = await redis.ttl(`blocked_ip:${ip}`);
                if (blocked > 0) {
                    return res.status(429).json({ error: 'Too many requests. Please wait.', retry_after: blocked });
                }
                const authRateKey = `auth_rate:${ip}`;
                // Ensure key has TTL even if server crashes between INCR and EXPIRE
                await redis.set(authRateKey, 0, { nx: true, ex: 30 });
                const authCount = await redis.incr(authRateKey);
                if (authCount > 10) {
                    return res.status(429).json({ error: 'Too many auth requests. Please wait and try again.', retry_after: 30 });
                }
                redisRateLimited = true;
            } catch (e) { /* Redis failure = fall through to in-memory fallback below */ }
        }
        // In-memory fallback when Redis is unavailable (per-instance, not shared, but better than nothing)
        if (!redisRateLimited) {
            const now2 = Date.now();
            if (!authRateFallback.has(ip)) {
                authRateFallback.set(ip, { count: 0, firstSeen: now2 });
            }
            const entry = authRateFallback.get(ip);
            if (now2 - entry.firstSeen > AUTH_RATE_FALLBACK_WINDOW) {
                entry.count = 0;
                entry.firstSeen = now2;
            }
            entry.count++;
            if (entry.count > AUTH_RATE_FALLBACK_LIMIT) {
                return res.status(429).json({ error: 'Too many auth requests. Please wait and try again.', retry_after: 30 });
            }
        }
        // Still log request on finish
        res.on('finish', () => {
            const duration = Date.now() - start;
            if (req.method !== 'GET' || res.statusCode >= 400) {
                console.log(`[REQ] ${req.method} ${req.path} ${res.statusCode} ${duration}ms | uid=${unifiedId} auth=${hasAuthToken}`);
            }
        });
        return next();
    }

    // Track per-IP request rate
    const ipKey = ip;
    const now = Date.now();

    // For IPs not in memory, check Redis for cross-instance blocks
    if (!requestCounts.has(ipKey) && redis) {
        try {
            const ttl = await redis.ttl(`blocked_ip:${ipKey}`);
            if (ttl > 0) {
                requestCounts.set(ipKey, { count: 0, authFailures: 0, firstSeen: now, lastPath: req.path, blockedUntil: now + ttl * 1000 });
            }
        } catch (e) { /* Redis failure = fall through to in-memory only */ }
    }

    if (!requestCounts.has(ipKey)) {
        requestCounts.set(ipKey, { count: 0, authFailures: 0, firstSeen: now, lastPath: '', blockedUntil: 0 });
    }
    const ipEntry = requestCounts.get(ipKey);

    // Reset window if older than 1 minute
    if (now - ipEntry.firstSeen > RATE_WINDOW_MS) {
        ipEntry.count = 0;
        ipEntry.authFailures = 0;
        ipEntry.firstSeen = now;
        // Clear expired blocks so IP can be re-blocked if it misbehaves again
        if (ipEntry.blockedUntil && now > ipEntry.blockedUntil) {
            ipEntry.blockedUntil = 0;
        }
    }

    ipEntry.count++;
    ipEntry.lastPath = req.path;

    // Block if IP is currently rate-limited
    if (ipEntry.blockedUntil && now < ipEntry.blockedUntil) {
        return res.status(429).set('Retry-After', String(Math.ceil((ipEntry.blockedUntil - now) / 1000))).json({ error: 'Too many requests' });
    }

    // Block if IP exceeds request rate limit
    if (ipEntry.count > IP_RATE_LIMIT) {
        ipEntry.blockedUntil = now + IP_BLOCK_DURATION_MS;
        console.warn(`[SECURITY] IP rate limited: ip=${ip} count=${ipEntry.count}/min path=${req.path} — blocked for 30min`);
        // Share block across all Vercel instances via Redis
        if (redis) redis.set(`blocked_ip:${ipKey}`, '1', { ex: Math.ceil(IP_BLOCK_DURATION_MS / 1000) }).catch(() => {});
        return res.status(429).set('Retry-After', String(IP_BLOCK_DURATION_MS / 1000)).json({ error: 'Too many requests' });
    }

    // Log on response finish
    res.on('finish', () => {
        const duration = Date.now() - start;
        const logEntry = `[REQ] ${req.method} ${req.path} ${res.statusCode} ${duration}ms | uid=${unifiedId} auth=${hasAuthToken}`;

        // Always log non-GET requests and errors
        if (req.method !== 'GET' || res.statusCode >= 400) {
            console.log(logEntry);
        }

        // Track auth failures and block IPs with too many
        if (res.statusCode === 401 || res.statusCode === 403) {
            ipEntry.authFailures++;
            console.warn(`[SECURITY] Auth failure: ${req.method} ${req.path} ${res.statusCode} | auth=${hasAuthToken} cv=${clientVersion || 'none'}`);

            if (ipEntry.authFailures >= IP_AUTH_FAIL_LIMIT && (!ipEntry.blockedUntil || now > ipEntry.blockedUntil)) {
                ipEntry.blockedUntil = Date.now() + IP_BLOCK_DURATION_MS;
                console.warn(`[SECURITY] IP blocked for auth failures: ip=${ip} failures=${ipEntry.authFailures}/min — blocked for 30min`);
                // Share block across all Vercel instances via Redis
                if (redis) redis.set(`blocked_ip:${ipKey}`, '1', { ex: Math.ceil(IP_BLOCK_DURATION_MS / 1000) }).catch(() => {});
            }
        }

        // Log missing client version on V2 endpoints (future enforcement)
        if (req.path.startsWith('/v2/') && !clientVersion) {
            console.warn(`[SECURITY] Missing X-Client-Version: ${req.method} ${req.path}`);
        }

        // Alert on high request rate from single IP (>100/min)
        if (ipEntry.count > 100 && ipEntry.count % 50 === 0) {
            console.warn(`[SECURITY] High request rate: ip=${ip} count=${ipEntry.count}/min path=${req.path}`);
        }

        // Alert on 404 scans (someone probing endpoints)
        if (res.statusCode === 404 && !req.path.startsWith('/v2/remote/') && !req.path.startsWith('/user/')) {
            console.warn(`[SECURITY] 404 probe: ${req.method} ${req.path} | ip=${ip}`);
        }
    });

    next();
});

// Validate unified_id format on V2 endpoints to prevent Redis key injection
app.use('/v2/', (req, res, next) => {
    const uid = req.body?.unified_id || req.query?.unified_id;
    if (uid && !isValidUnifiedId(uid)) {
        return res.status(400).json({ error: 'Invalid unified_id format' });
    }
    next();
});

// =============================================================================
// RATE LIMITING CONFIGURATION (using Upstash Redis)
// =============================================================================

const RATE_LIMIT = {
    DAILY_REQUESTS: 2000,  // Max requests per user per day (Patreon supporters)
    FREE_DAILY_REQUESTS: 100, // Max requests per user per day (free users)
    KEY_PREFIX: 'ratelimit:'
};

// Initialize Redis client (checks various env var names from Vercel/Upstash integrations)
let redis = null;
try {
    const redisUrl = process.env.UPSTASH_REDIS_REST_URL || process.env.KV_REST_API_URL;
    const redisToken = process.env.UPSTASH_REDIS_REST_TOKEN || process.env.KV_REST_API_TOKEN;

    if (redisUrl && redisToken) {
        redis = new Redis({
            url: redisUrl,
            token: redisToken,
        });
        console.log('Upstash Redis initialized');
    } else if (process.env.REDIS_URL) {
        // Try to parse REDIS_URL format (might be from Upstash integration)
        // Upstash REST URL format: https://xxx.upstash.io with token as password
        const url = new URL(process.env.REDIS_URL);
        if (url.hostname.includes('upstash.io')) {
            redis = new Redis({
                url: `https://${url.hostname}`,
                token: url.password,
            });
            console.log('Upstash Redis initialized from REDIS_URL');
        } else {
            console.warn('REDIS_URL found but not Upstash format - rate limiting disabled');
        }
    } else {
        console.warn('Redis not configured - rate limiting disabled. Set UPSTASH_REDIS_REST_URL and UPSTASH_REDIS_REST_TOKEN');
    }
} catch (error) {
    console.error('Failed to initialize Redis:', error.message);
}

// Whitelist loading middleware — ensures in-memory cache is populated from Redis
// before any request handler runs. Promise deduplication prevents concurrent loads.
app.use(async (req, res, next) => {
    if (!whitelistLoaded && !whitelistLoadPromise) {
        whitelistLoadPromise = loadWhitelistFromRedis();
    }
    if (whitelistLoadPromise) {
        await whitelistLoadPromise;
    }
    next();
});

// =============================================================================
// AUTH TOKEN HELPERS
// =============================================================================

/**
 * Generate a random auth token and its SHA-256 hash.
 * The plain token is returned to the client; only the hash is stored server-side.
 */
function generateAuthToken() {
    const token = crypto.randomBytes(32).toString('hex'); // 64-char hex
    const hash = crypto.createHash('sha256').update(token).digest('hex');
    return { token, hash };
}

/**
 * Validate an auth token from the X-Auth-Token header against the user's stored hash.
 * Legacy users (no auth_token_hash) are REJECTED — they must call /v2/auth/restore-session
 * first to receive a token. restore-session handles legacy users explicitly and issues tokens.
 */
function validateAuthToken(req, user) {
    const token = req.headers['x-auth-token'];
    if (!user.auth_token_hash) {
        // Legacy user — no token issued yet. Reject and force them through restore-session.
        return { valid: false, legacy: true };
    }
    if (!token) {
        return { valid: false, legacy: false };
    }
    const hash = crypto.createHash('sha256').update(token).digest('hex');
    if (!timingSafeCompare(hash, user.auth_token_hash)) {
        // Wrong token — this is the one case we always reject (actual misuse)
        return { valid: false, legacy: false };
    }
    return { valid: true, legacy: false };
}

/**
 * Get today's date key (UTC) for rate limiting
 */
function getTodayKey() {
    const now = new Date();
    return `${now.getUTCFullYear()}-${String(now.getUTCMonth() + 1).padStart(2, '0')}-${String(now.getUTCDate()).padStart(2, '0')}`;
}

/**
 * Check and increment rate limit for a user
 * Returns { allowed: boolean, remaining: number, used: number }
 * @param {string} userId
 * @param {number} [dailyLimit] - Override daily limit (defaults to RATE_LIMIT.DAILY_REQUESTS)
 */
async function checkRateLimit(userId, dailyLimit) {
    const limit = dailyLimit || RATE_LIMIT.DAILY_REQUESTS;
    // If Redis not configured, deny requests (prevents unbounded cost on OpenRouter)
    if (!redis) {
        return {
            allowed: false,
            remaining: 0,
            used: 0,
            error: true
        };
    }

    const todayKey = getTodayKey();
    const key = `${RATE_LIMIT.KEY_PREFIX}${userId}:${todayKey}`;

    try {
        // R8/M10: Atomic increment-first to prevent TOCTOU race
        await redis.set(key, 0, { nx: true, ex: 48 * 60 * 60 });
        const count = await redis.incr(key);

        if (count > limit) {
            return {
                allowed: false,
                remaining: 0,
                used: count
            };
        }

        return {
            allowed: true,
            remaining: Math.max(0, limit - count),
            used: count
        };
    } catch (error) {
        console.error('Rate limit check failed:', error.message);
        // If Redis fails, DENY the request to prevent cost amplification
        return {
            allowed: false,
            remaining: 0,
            used: limit,
            error: true
        };
    }
}

/**
 * Get current rate limit status without incrementing
 * @param {string} userId
 * @param {number} [dailyLimit] - Override daily limit (defaults to RATE_LIMIT.DAILY_REQUESTS)
 */
async function getRateLimitStatus(userId, dailyLimit) {
    const limit = dailyLimit || RATE_LIMIT.DAILY_REQUESTS;
    // If Redis not configured, DENY to match checkRateLimit() behavior
    if (!redis) {
        return {
            remaining: 0,
            used: limit,
            limit: limit
        };
    }

    const todayKey = getTodayKey();
    const key = `${RATE_LIMIT.KEY_PREFIX}${userId}:${todayKey}`;

    try {
        let count = await redis.get(key) || 0;
        count = parseInt(count) || 0;
        return {
            remaining: Math.max(0, limit - count),
            used: count,
            limit: limit
        };
    } catch (error) {
        console.error('Rate limit status check failed:', error.message);
        // DENY on Redis error to match checkRateLimit() behavior
        return {
            remaining: 0,
            used: limit,
            limit: limit
        };
    }
}

// =============================================================================
// CONFIGURATION - Set these as environment variables on your hosting platform
// =============================================================================

const CONFIG = {
    // Patreon OAuth credentials (get from https://www.patreon.com/portal/registration/register-clients)
    PATREON_CLIENT_ID: process.env.PATREON_CLIENT_ID || '',
    PATREON_CLIENT_SECRET: process.env.PATREON_CLIENT_SECRET || '',

    // Discord OAuth credentials (get from https://discord.com/developers/applications)
    DISCORD_CLIENT_ID: process.env.DISCORD_CLIENT_ID || '',
    DISCORD_CLIENT_SECRET: process.env.DISCORD_CLIENT_SECRET || '',

    // OpenRouter API key (get from https://openrouter.ai/keys)
    OPENROUTER_API_KEY: process.env.OPENROUTER_API_KEY || '',

    // AI Model - MythoMax L2 13B is gold standard for roleplay
    AI_MODEL: process.env.AI_MODEL || 'gryphe/mythomax-l2-13b',

    // Your Patreon campaign ID (found in your Patreon creator dashboard URL)
    PATREON_CAMPAIGN_ID: process.env.PATREON_CAMPAIGN_ID || '',

    // Tier IDs from your Patreon (get from Patreon API or dashboard)
    // These are the minimum pledge amounts in cents for each tier
    // Any active patron gets at least Tier 1 (see tier logic below)
    TIER_1_MIN_CENTS: parseInt(process.env.TIER_1_MIN_CENTS || '5'),     // 5 cents minimum
    TIER_2_MIN_CENTS: parseInt(process.env.TIER_2_MIN_CENTS || '1000'),  // $10.00

    // Server port
    PORT: process.env.PORT || 3000
};

// =============================================================================
// STARTUP VALIDATION - Warn about missing critical secrets
// =============================================================================
if (process.env.VERCEL || process.env.NODE_ENV === 'production') {
    const requiredSecrets = ['ADMIN_TOKEN', 'CCP_HMAC_SECRET', 'EMAIL_HASH_KEY'];
    const criticalSecrets = ['PATREON_CLIENT_SECRET', 'DISCORD_CLIENT_SECRET', 'OPENROUTER_API_KEY'];
    const missingRequired = requiredSecrets.filter(name => !process.env[name]);
    if (missingRequired.length > 0) {
        const msg = `[STARTUP FATAL] Missing required secrets: ${missingRequired.join(', ')}`;
        console.error(msg);
        throw new Error(msg);
    }
    for (const name of criticalSecrets) {
        if (!process.env[name]) {
            console.warn(`[STARTUP WARNING] Secret ${name} is not configured — related features will fail.`);
        }
    }
}

// =============================================================================
// WHITELIST - Users who get Tier 1 access regardless of subscription
// Loaded from Redis (keys: whitelist:emails, whitelist:names) into in-memory Sets.
// isWhitelisted() stays synchronous — middleware ensures cache is loaded before requests.
// =============================================================================

let whitelistEmails = new Set();
let whitelistNames = new Set();
let whitelistLoaded = false;
let whitelistLoadPromise = null;

async function loadWhitelistFromRedis() {
    try {
        if (!redis) {
            console.warn('[WHITELIST] Redis not available, skipping load');
            return;
        }
        const [emails, names] = await Promise.all([
            redis.smembers('whitelist:emails'),
            redis.smembers('whitelist:names')
        ]);
        if (emails && emails.length > 0) {
            whitelistEmails = new Set(emails);
        }
        if (names && names.length > 0) {
            whitelistNames = new Set(names.map(n => n.toLowerCase()));
        }
        whitelistLoaded = true;
        console.log(`[WHITELIST] Loaded from Redis: ${whitelistEmails.size} emails, ${whitelistNames.size} names`);
    } catch (err) {
        console.error('[WHITELIST] Failed to load from Redis:', err.message);
        whitelistLoadPromise = null; // Allow retry on next request
    }
}

function isWhitelisted(email, name, displayName = null) {
    const emailHash = email ? hashEmail(email) : null;
    const emailMatch = emailHash && whitelistEmails.has(emailHash);
    const nameMatch = name && whitelistNames.has(name.toLowerCase());
    const displayNameMatch = displayName && whitelistNames.has(displayName.toLowerCase());
    if (emailMatch || nameMatch || displayNameMatch) {
        console.log(`[WHITELIST CHECK] match found (email=${emailMatch}, name=${nameMatch}, displayName=${displayNameMatch})`);
    }
    if (emailMatch) return true;
    if (nameMatch) return true;
    if (displayNameMatch) return true;
    return false;
}

// Debug endpoint to test whitelist (admin only)
app.get('/debug/whitelist', (req, res) => {
    const adminToken = req.headers['x-admin-token'];
    if (!verifyAdminToken(adminToken)) {
        return res.status(403).json({ error: 'Unauthorized' });
    }

    const { email, name } = req.query;
    const emailHash = email ? hashEmail(email) : null;
    const nameLower = (name || '').toLowerCase();
    const emailInSet = emailHash ? whitelistEmails.has(emailHash) : false;
    const nameInSet = whitelistNames.has(nameLower);
    const result = isWhitelisted(email, name, null);
    res.json({
        email_in_whitelist: emailInSet,
        name_in_whitelist: nameInSet,
        is_whitelisted: result,
        whitelist_emails_count: whitelistEmails.size,
        whitelist_names_count: whitelistNames.size
    });
});

// =============================================================================
// ADMIN WHITELIST MANAGEMENT ENDPOINTS
// =============================================================================

// Seed Redis with whitelist data (one-time migration)
app.post('/admin/whitelist/seed', async (req, res) => {
    try {
        const { emails, names } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!emails || !names || !Array.isArray(emails) || !Array.isArray(names)) {
            return res.status(400).json({ error: 'emails and names arrays required' });
        }

        // Clear existing keys first
        await Promise.all([
            redis.del('whitelist:emails'),
            redis.del('whitelist:names')
        ]);

        // Hash emails, lowercase names
        const emailHashes = emails.map(e => hashEmail(e));
        const namesLower = names.map(n => n.toLowerCase());

        if (emailHashes.length > 0) {
            await redis.sadd('whitelist:emails', ...emailHashes);
        }
        if (namesLower.length > 0) {
            await redis.sadd('whitelist:names', ...namesLower);
        }

        // Refresh in-memory cache
        whitelistEmails = new Set(emailHashes);
        whitelistNames = new Set(namesLower);
        whitelistLoaded = true;

        console.log(`[WHITELIST] Seeded: ${emailHashes.length} emails (hashed), ${namesLower.length} names`);
        res.json({
            success: true,
            emails_count: emailHashes.length,
            names_count: namesLower.length
        });
    } catch (error) {
        console.error('Admin whitelist/seed error:', error.message);
        res.status(500).json({ error: 'Failed to seed whitelist' });
    }
});

// Add a whitelist entry
app.post('/admin/whitelist/add', async (req, res) => {
    try {
        const { type, value } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!type || !value || !['email', 'name'].includes(type)) {
            return res.status(400).json({ error: 'type ("email" or "name") and value required' });
        }

        const storeValue = type === 'email' ? hashEmail(value) : value.toLowerCase();
        const key = type === 'email' ? 'whitelist:emails' : 'whitelist:names';
        await redis.sadd(key, storeValue);

        // Update in-memory cache
        if (type === 'email') {
            whitelistEmails.add(storeValue);
        } else {
            whitelistNames.add(storeValue);
        }

        console.log(`[WHITELIST] Added ${type}: (hashed/lowered)`);
        res.json({ success: true, type });
    } catch (error) {
        console.error('Admin whitelist/add error:', error.message);
        res.status(500).json({ error: 'Failed to add whitelist entry' });
    }
});

// Remove a whitelist entry
app.post('/admin/whitelist/remove', async (req, res) => {
    try {
        const { type, value } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!type || !value || !['email', 'name'].includes(type)) {
            return res.status(400).json({ error: 'type ("email" or "name") and value required' });
        }

        const storeValue = type === 'email' ? hashEmail(value) : value.toLowerCase();
        const key = type === 'email' ? 'whitelist:emails' : 'whitelist:names';
        await redis.srem(key, storeValue);

        // Update in-memory cache
        if (type === 'email') {
            whitelistEmails.delete(storeValue);
        } else {
            whitelistNames.delete(storeValue);
        }

        console.log(`[WHITELIST] Removed ${type}: (hashed/lowered)`);
        res.json({ success: true, type });
    } catch (error) {
        console.error('Admin whitelist/remove error:', error.message);
        res.status(500).json({ error: 'Failed to remove whitelist entry' });
    }
});

// List all whitelist entries (from Redis — authoritative)
app.get('/admin/whitelist/list', async (req, res) => {
    try {
        const adminToken = req.headers['x-admin-token'];
        if (!verifyAdminToken(adminToken)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const [emails, names] = await Promise.all([
            redis.smembers('whitelist:emails'),
            redis.smembers('whitelist:names')
        ]);

        res.json({
            emails: emails || [],
            names: names || [],
            emails_count: (emails || []).length,
            names_count: (names || []).length,
            cache_loaded: whitelistLoaded,
            cache_emails_count: whitelistEmails.size,
            cache_names_count: whitelistNames.size
        });
    } catch (error) {
        console.error('Admin whitelist/list error:', error.message);
        res.status(500).json({ error: 'Failed to list whitelist' });
    }
});

// Force-refresh in-memory cache from Redis
app.post('/admin/whitelist/reload', async (req, res) => {
    try {
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Reset and reload
        whitelistLoaded = false;
        whitelistLoadPromise = null;
        await loadWhitelistFromRedis();

        res.json({
            success: true,
            emails_count: whitelistEmails.size,
            names_count: whitelistNames.size
        });
    } catch (error) {
        console.error('Admin whitelist/reload error:', error.message);
        res.status(500).json({ error: 'Failed to reload whitelist' });
    }
});

// =============================================================================
// PATREON API HELPERS
// =============================================================================

const PATREON_API_BASE = 'https://www.patreon.com';
const PATREON_API_V2 = 'https://www.patreon.com/api/oauth2/v2';

/**
 * Exchange authorization code for access token
 */
async function exchangeCodeForTokens(code, redirectUri) {
    const params = new URLSearchParams({
        code,
        grant_type: 'authorization_code',
        client_id: CONFIG.PATREON_CLIENT_ID,
        client_secret: CONFIG.PATREON_CLIENT_SECRET,
        redirect_uri: redirectUri
    });

    const response = await fetch(`${PATREON_API_BASE}/api/oauth2/token`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: params.toString()
    });

    if (!response.ok) {
        const error = await response.text();
        throw new Error(`Token exchange failed: ${error}`);
    }

    return response.json();
}

/**
 * Refresh an expired access token
 */
async function refreshAccessToken(refreshToken) {
    const params = new URLSearchParams({
        grant_type: 'refresh_token',
        refresh_token: refreshToken,
        client_id: CONFIG.PATREON_CLIENT_ID,
        client_secret: CONFIG.PATREON_CLIENT_SECRET
    });

    const response = await fetch(`${PATREON_API_BASE}/api/oauth2/token`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: params.toString()
    });

    if (!response.ok) {
        const error = await response.text();
        throw new Error(`Token refresh failed: ${error}`);
    }

    return response.json();
}

/**
 * Get user's identity and membership info from Patreon
 */
async function getPatreonIdentity(accessToken) {
    const fields = [
        'fields[user]=full_name,email',
        'fields[member]=patron_status,currently_entitled_amount_cents,pledge_relationship_start',
        'include=memberships.campaign'
    ].join('&');

    const response = await fetch(`${PATREON_API_V2}/identity?${fields}`, {
        headers: { 'Authorization': `Bearer ${accessToken}` }
    });

    if (!response.ok) {
        if (response.status === 401) {
            throw new Error('UNAUTHORIZED');
        }
        const error = await response.text();
        throw new Error(`Failed to get identity: ${error}`);
    }

    return response.json();
}

/**
 * Determine subscription tier from Patreon membership data
 */
function determineTier(identityData) {
    const user = identityData.data;
    const memberships = identityData.included?.filter(i => i.type === 'member') || [];

    // Find active membership for our campaign ONLY
    let activePledgeCents = 0;
    let isActive = false;
    let patronName = user?.attributes?.full_name || null;
    let patronEmail = user?.attributes?.email || null;

    for (const membership of memberships) {
        const attrs = membership.attributes;

        // CRITICAL: Only count memberships to OUR campaign, not other Patreon creators
        // Convert both to strings and trim for comparison (API may return number, env var is string)
        const membershipCampaignId = String(membership.relationships?.campaign?.data?.id || '').trim();
        const ourCampaignId = String(CONFIG.PATREON_CAMPAIGN_ID || '').trim();

        console.log(`Checking membership: campaign=${membershipCampaignId}, our=${ourCampaignId}, status=${attrs.patron_status}, cents=${attrs.currently_entitled_amount_cents}`);

        if (ourCampaignId && membershipCampaignId !== ourCampaignId) {
            console.log(`Skipping membership to campaign ${membershipCampaignId} (not our campaign ${ourCampaignId})`);
            continue;
        }

        console.log(`MATCH! Campaign ${membershipCampaignId} matches. Status: ${attrs.patron_status}, Cents: ${attrs.currently_entitled_amount_cents}`);

        if (attrs.patron_status === 'active_patron') {
            isActive = true;
            activePledgeCents = Math.max(activePledgeCents, attrs.currently_entitled_amount_cents || 0);
            console.log(`Active patron found! Setting isActive=true, pledge=${activePledgeCents}`);
        }
    }

    // Determine tier based on pledge amount
    // Any active patron gets at least Tier 1 (Patreon marks them active_patron)
    let tier = 0; // None
    if (isActive) {
        if (activePledgeCents >= CONFIG.TIER_2_MIN_CENTS) {
            tier = 2; // Level 2
        } else {
            tier = 1; // Level 1 - any active patron
        }
    }

    console.log(`[TIER] active=${isActive}, pledge=${activePledgeCents}c, tier=${tier}`);

    return {
        is_active: isActive,
        tier,
        patron_email: patronEmail, // kept in-memory for whitelist/index lookups, stripped from API response
        pledge_cents: activePledgeCents,
        patreon_user_id: user?.id
    };
}

/**
 * Extract tier info from the 'included' array of a Patreon identity response
 * Used by v2 auth endpoints
 */
function getTierFromMemberships(included) {
    const memberships = included?.filter(i => i.type === 'member') || [];

    let activePledgeCents = 0;
    let isActive = false;

    for (const membership of memberships) {
        const attrs = membership.attributes;

        // CRITICAL: Only count memberships to OUR campaign
        const membershipCampaignId = String(membership.relationships?.campaign?.data?.id || '').trim();
        const ourCampaignId = String(CONFIG.PATREON_CAMPAIGN_ID || '').trim();

        if (ourCampaignId && membershipCampaignId !== ourCampaignId) {
            continue;
        }

        if (attrs.patron_status === 'active_patron') {
            isActive = true;
            activePledgeCents = Math.max(activePledgeCents, attrs.currently_entitled_amount_cents || 0);
        }
    }

    // Determine tier based on pledge amount
    let tier = 0;
    if (isActive) {
        if (activePledgeCents >= CONFIG.TIER_2_MIN_CENTS) {
            tier = 2;
        } else {
            tier = 1;
        }
    }

    return {
        is_active: isActive,
        tier,
        pledge_cents: activePledgeCents
    };
}

// =============================================================================
// DISCORD API HELPERS
// =============================================================================

const DISCORD_API_BASE = 'https://discord.com/api/v10';

/**
 * Exchange Discord authorization code for access token
 */
async function exchangeDiscordCode(code, redirectUri) {
    const params = new URLSearchParams({
        code,
        grant_type: 'authorization_code',
        client_id: CONFIG.DISCORD_CLIENT_ID,
        client_secret: CONFIG.DISCORD_CLIENT_SECRET,
        redirect_uri: redirectUri
    });

    const response = await fetch(`${DISCORD_API_BASE}/oauth2/token`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: params.toString()
    });

    if (!response.ok) {
        const error = await response.text();
        throw new Error(`Discord token exchange failed: ${error}`);
    }

    return response.json();
}

/**
 * Refresh Discord access token
 */
async function refreshDiscordToken(refreshToken) {
    const params = new URLSearchParams({
        grant_type: 'refresh_token',
        refresh_token: refreshToken,
        client_id: CONFIG.DISCORD_CLIENT_ID,
        client_secret: CONFIG.DISCORD_CLIENT_SECRET
    });

    const response = await fetch(`${DISCORD_API_BASE}/oauth2/token`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
        body: params.toString()
    });

    if (!response.ok) {
        const error = await response.text();
        throw new Error(`Discord token refresh failed: ${error}`);
    }

    return response.json();
}

/**
 * Get Discord user information
 */
async function getDiscordUser(accessToken) {
    const response = await fetch(`${DISCORD_API_BASE}/users/@me`, {
        headers: {
            'Authorization': `Bearer ${accessToken}`
        }
    });

    if (!response.ok) {
        if (response.status === 401) {
            throw new Error('UNAUTHORIZED');
        }
        const error = await response.text();
        throw new Error(`Failed to get Discord user: ${error}`);
    }

    return response.json();
}

// =============================================================================
// API ENDPOINTS
// =============================================================================

// Allowed OAuth redirect URIs (localhost callbacks used by the desktop app)
const ALLOWED_REDIRECT_URIS = [
    'http://localhost:47832/callback/', // Patreon
    'http://localhost:47833/callback/', // Discord
];

function isAllowedRedirectUri(uri) {
    return ALLOWED_REDIRECT_URIS.includes(uri);
}

/**
 * GET /patreon/authorize
 * Redirects to Patreon OAuth authorization page
 */
app.get('/patreon/authorize', (req, res) => {
    const { redirect_uri, state } = req.query;

    if (!redirect_uri) {
        return res.status(400).json({ error: 'redirect_uri is required' });
    }
    if (!isAllowedRedirectUri(redirect_uri)) {
        return res.status(400).json({ error: 'Invalid redirect_uri' });
    }

    const scopes = 'identity identity.memberships';
    const authUrl = new URL(`${PATREON_API_BASE}/oauth2/authorize`);
    authUrl.searchParams.set('response_type', 'code');
    authUrl.searchParams.set('client_id', CONFIG.PATREON_CLIENT_ID);
    authUrl.searchParams.set('redirect_uri', redirect_uri);
    authUrl.searchParams.set('scope', scopes);
    if (state) {
        authUrl.searchParams.set('state', state);
    }

    res.redirect(authUrl.toString());
});

/**
 * POST /patreon/token
 * Exchanges authorization code for tokens
 */
app.post('/patreon/token', async (req, res) => {
    try {
        const { code, redirect_uri } = req.body;

        if (!code || !redirect_uri) {
            return res.status(400).json({ error: 'code and redirect_uri are required' });
        }
        if (!isAllowedRedirectUri(redirect_uri)) {
            return res.status(400).json({ error: 'Invalid redirect_uri' });
        }

        const tokens = await exchangeCodeForTokens(code, redirect_uri);

        res.json({
            access_token: tokens.access_token,
            refresh_token: tokens.refresh_token,
            expires_in: tokens.expires_in,
            token_type: tokens.token_type
        });
    } catch (error) {
        console.error('Token exchange error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /patreon/refresh
 * Refreshes an expired access token
 */
app.post('/patreon/refresh', async (req, res) => {
    try {
        const { refresh_token } = req.body;

        if (!refresh_token) {
            return res.status(400).json({ error: 'refresh_token is required' });
        }

        const tokens = await refreshAccessToken(refresh_token);

        res.json({
            access_token: tokens.access_token,
            refresh_token: tokens.refresh_token,
            expires_in: tokens.expires_in,
            token_type: tokens.token_type
        });
    } catch (error) {
        console.error('Token refresh error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * GET /patreon/validate
 * Validates subscription and returns tier
 * Requires: Authorization: Bearer <patreon_access_token>
 */
app.get('/patreon/validate', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        const accessToken = authHeader.substring(7);
        const identity = await getPatreonIdentity(accessToken);
        const tierInfo = determineTier(identity);

        // Include rate limit status
        const userId = tierInfo.patreon_user_id || 'unknown';
        const rateLimitStatus = await getRateLimitStatus(userId);
        tierInfo.rate_limit = rateLimitStatus;

        // Look up unified user (with on-the-fly migration from legacy)
        let unifiedLookup = { exists: false, needs_registration: true };
        let displayName = null;

        if (userId !== 'unknown') {
            unifiedLookup = await lookupUserByPatreon(userId);

            if (unifiedLookup.exists) {
                displayName = unifiedLookup.display_name;
                tierInfo.display_name = displayName;
                tierInfo.unified_id = unifiedLookup.unified_id;
                tierInfo.needs_registration = !unifiedLookup.has_display_name;
                tierInfo.has_linked_discord = unifiedLookup.has_discord || false;
            } else {
                // Check for email-based auto-link opportunity
                if (tierInfo.patron_email) {
                    const emailLookup = await lookupUserByEmail(tierInfo.patron_email);
                    if (emailLookup) {
                        tierInfo.can_auto_link = true;
                        tierInfo.auto_link_unified_id = emailLookup.unified_id;
                        tierInfo.auto_link_display_name = emailLookup.user.display_name;
                    }
                }
                tierInfo.needs_registration = true;
            }
        }

        // Check whitelist - if whitelisted, grant Tier 2 access
        // Checks: email, Patreon name, AND leaderboard display name
        const whitelisted = isWhitelisted(tierInfo.patron_email, null, displayName);
        tierInfo.is_whitelisted = whitelisted;
        if (whitelisted) {
            if (tierInfo.tier < 2) {
                tierInfo.tier = 2;
            }
            console.log(`Whitelisted user validated: uid=${userId}`);
        }

        // Update unified user's Patreon status if they exist
        // R8/M5: Only update if the OAuth patreon_user_id matches the unified user's stored patreon_id
        // (prevents an attacker with a different Patreon account from modifying another user's status)
        if (unifiedLookup.exists && unifiedLookup.unified_id && userId !== 'unknown') {
            await updateUnifiedUserPatreonStatus(unifiedLookup.unified_id, {
                tier: tierInfo.tier,
                is_active: tierInfo.is_active,
                is_whitelisted: whitelisted
            });
        }

        // Also update legacy profile for backward compatibility
        // BUT only if profile already exists AND has a display_name!
        // Creating empty profiles causes "already linked" conflicts when users try to link accounts.
        if (userId !== 'unknown' && redis) {
            try {
                const profileKey = `${PROFILE_KEY_PREFIX}${userId}`;
                const profileData = await redis.get(profileKey);

                // Only update if profile exists AND has display_name
                // Don't create new profiles here - let the account linking flow handle that
                if (profileData) {
                    const profile = typeof profileData === 'string' ? JSON.parse(profileData) : profileData;

                    // Skip if no display_name - this profile shouldn't exist, don't perpetuate it
                    if (!profile.display_name || profile.display_name.trim() === '') {
                        console.log(`Skipping profile update for ${userId} - no display_name (prevents linking conflicts)`);
                    } else {
                        // Update Patreon status fields
                        profile.patreon_tier = tierInfo.tier;
                        profile.patreon_is_active = tierInfo.is_active;
                        profile.patreon_is_whitelisted = whitelisted;
                        profile.patreon_status_updated_at = new Date().toISOString();

                        // Store email hash for cross-account linking (Discord users with same email get linked)
                        if (tierInfo.patron_email) {
                            profile.email_hash = hashEmail(tierInfo.patron_email);
                            delete profile.email; // Remove legacy plaintext
                            // Create email index — point to unified_id if available, else patreon_id
                            const emailIndexKey = `email_index:${hashEmail(tierInfo.patron_email)}`;
                            const emailTarget = (unifiedLookup.exists && unifiedLookup.unified_id) ? unifiedLookup.unified_id : userId;
                            await redis.set(emailIndexKey, emailTarget);
                        }

                        await redis.set(profileKey, JSON.stringify(profile));
                    }
                }
                // If no profile exists, don't create one - let account linking handle it
            } catch (profileError) {
                console.error('Failed to update profile with Patreon status:', profileError.message);
            }
        }

        // Strip plaintext email before sending to client
        const { patron_email, ...clientTierInfo } = tierInfo;
        res.json(clientTierInfo);
    } catch (error) {
        console.error('Validation error:', error.message);
        if (error.message === 'UNAUTHORIZED') {
            return res.status(401).json({ error: 'Token expired or invalid' });
        }
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * GET /patreon/debug-campaigns
 * Shows all campaign IDs from user's memberships (for finding your campaign ID)
 * Requires: admin_token query param + Authorization: Bearer <patreon_access_token>
 */
app.get('/patreon/debug-campaigns', async (req, res) => {
    try {
        // Admin-only: prevents arbitrary users from using this debug endpoint
        const admin_token = req.headers['x-admin-token'];
        if (!admin_token || !verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Admin authorization required' });
        }

        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        const accessToken = authHeader.substring(7);
        const identity = await getPatreonIdentity(accessToken);

        const memberships = identity.included?.filter(i => i.type === 'member') || [];
        const campaigns = identity.included?.filter(i => i.type === 'campaign') || [];

        const campaignInfo = memberships.map(m => ({
            campaign_id: m.relationships?.campaign?.data?.id,
            patron_status: m.attributes?.patron_status,
            pledge_cents: m.attributes?.currently_entitled_amount_cents
        }));

        // Intentionally omit full_name — PII should not appear in API responses
        res.json({
            memberships: campaignInfo,
            campaigns: campaigns.map(c => ({
                id: c.id,
                name: c.attributes?.name || c.attributes?.creation_name
            })),
            configured_campaign_id: CONFIG.PATREON_CAMPAIGN_ID || '(NOT SET)',
            hint: 'Set PATREON_CAMPAIGN_ID to the campaign_id you want to validate against'
        });
    } catch (error) {
        console.error('Debug campaigns error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

// =============================================================================
// DISCORD OAUTH ENDPOINTS
// =============================================================================

/**
 * GET /discord/authorize
 * Redirects to Discord OAuth authorization page
 */
app.get('/discord/authorize', (req, res) => {
    const { redirect_uri, state } = req.query;

    if (!redirect_uri) {
        return res.status(400).json({ error: 'redirect_uri is required' });
    }
    if (!isAllowedRedirectUri(redirect_uri)) {
        return res.status(400).json({ error: 'Invalid redirect_uri' });
    }

    if (!CONFIG.DISCORD_CLIENT_ID) {
        return res.status(500).json({ error: 'Discord OAuth not configured' });
    }

    // Request identify and email scopes
    const scopes = 'identify email';
    const authUrl = new URL('https://discord.com/api/oauth2/authorize');
    authUrl.searchParams.set('response_type', 'code');
    authUrl.searchParams.set('client_id', CONFIG.DISCORD_CLIENT_ID);
    authUrl.searchParams.set('redirect_uri', redirect_uri);
    authUrl.searchParams.set('scope', scopes);
    if (state) {
        authUrl.searchParams.set('state', state);
    }

    res.redirect(authUrl.toString());
});

/**
 * POST /discord/token
 * Exchanges authorization code for tokens
 */
app.post('/discord/token', async (req, res) => {
    try {
        const { code, redirect_uri } = req.body;

        if (!code || !redirect_uri) {
            return res.status(400).json({ error: 'code and redirect_uri are required' });
        }
        if (!isAllowedRedirectUri(redirect_uri)) {
            return res.status(400).json({ error: 'Invalid redirect_uri' });
        }

        if (!CONFIG.DISCORD_CLIENT_ID || !CONFIG.DISCORD_CLIENT_SECRET) {
            return res.status(500).json({ error: 'Discord OAuth not configured' });
        }

        const tokens = await exchangeDiscordCode(code, redirect_uri);

        res.json({
            access_token: tokens.access_token,
            refresh_token: tokens.refresh_token,
            expires_in: tokens.expires_in,
            token_type: tokens.token_type,
            scope: tokens.scope
        });
    } catch (error) {
        console.error('Discord token exchange error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /discord/refresh
 * Refreshes an expired Discord access token
 */
app.post('/discord/refresh', async (req, res) => {
    try {
        const { refresh_token } = req.body;

        if (!refresh_token) {
            return res.status(400).json({ error: 'refresh_token is required' });
        }

        if (!CONFIG.DISCORD_CLIENT_ID || !CONFIG.DISCORD_CLIENT_SECRET) {
            return res.status(500).json({ error: 'Discord OAuth not configured' });
        }

        const tokens = await refreshDiscordToken(refresh_token);

        res.json({
            access_token: tokens.access_token,
            refresh_token: tokens.refresh_token,
            expires_in: tokens.expires_in,
            token_type: tokens.token_type,
            scope: tokens.scope
        });
    } catch (error) {
        console.error('Discord token refresh error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * GET /discord/validate
 * Validates Discord token and returns user info
 * Requires: Authorization: Bearer <discord_access_token>
 */
app.get('/discord/validate', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        const accessToken = authHeader.substring(7);
        const user = await getDiscordUser(accessToken);

        // Look up unified user (with on-the-fly migration from legacy)
        const unifiedLookup = await lookupUserByDiscord(user.id);

        const response = {
            id: user.id,
            username: user.username,
            discriminator: user.discriminator,
            global_name: user.global_name,
            avatar: user.avatar,
            verified: user.verified
        };

        if (unifiedLookup.exists) {
            response.unified_id = unifiedLookup.unified_id;
            response.display_name = unifiedLookup.display_name;
            response.needs_registration = !unifiedLookup.has_display_name;
            response.has_linked_patreon = unifiedLookup.has_patreon || false;

            // Check whitelist by display name (email is hashed in storage, use name only)
            if (unifiedLookup.user) {
                const whitelisted = isWhitelisted(
                    null,
                    null,
                    unifiedLookup.display_name
                );
                response.is_whitelisted = whitelisted;
                response.patreon_tier = whitelisted ? Math.max(unifiedLookup.user.patreon_tier || 0, 2) : (unifiedLookup.user.patreon_tier || 0);
                response.patreon_is_active = unifiedLookup.user.patreon_is_active || false;
            }
        } else {
            response.needs_registration = true;

            // Check for email-based auto-link opportunity
            if (user.email) {
                const emailLookup = await lookupUserByEmail(user.email);
                if (emailLookup) {
                    response.can_auto_link = true;
                    response.auto_link_unified_id = emailLookup.unified_id;
                    response.auto_link_display_name = emailLookup.user.display_name;
                }
            }
        }

        res.json(response);
    } catch (error) {
        console.error('Discord validation error:', error.message);
        if (error.message === 'UNAUTHORIZED') {
            return res.status(401).json({ error: 'Token expired or invalid' });
        }
        res.status(500).json({ error: 'Internal server error' });
    }
});

// =============================================================================
// DISCORD COMMUNITY WEBHOOK
// =============================================================================

const COMMUNITY_WEBHOOK_URL = process.env.DISCORD_COMMUNITY_WEBHOOK_URL || '';
const ACHIEVEMENTS_BASE_URL = 'https://codebambi.github.io/Conditioning-Control-Panel---CSharp-WPF/achievements/';

// Valid achievement names — must match client Achievement.All dictionary
const VALID_ACHIEVEMENT_NAMES = new Set([
    'Plastic Initiation', 'Dumb Bimbo', 'Fully Synthetic', 'Docile Cow',
    'Perfect Plastic Puppet', 'Brainwashed Slavedoll', 'Platinum Puppet',
    'Rose-Tinted Reality', 'Deep Sleep Mode', 'Daily Maintenance',
    'Retinal Burn', 'Morning Glory', 'Player 2 Disconnected', 'Sofa Decor',
    'Look, But Don\'t Touch', 'Spiral Eyes', 'Mathematician\'s Nightmare',
    'Pop Goes The Thought', 'Typing Tutor', 'Obedience Reflex',
    'Mercy Beggar', 'Clean Slate', 'Corner Hit', 'Neon Obsession',
    'Panic Button? What Panic Button?', 'Relapse', 'Total Lockdown', 'System Overload'
]);

/**
 * POST /discord/community-webhook
 * Posts achievements and level ups to the community Discord webhook
 * Body: { type: 'achievement' | 'level_up', display_name: string, ... }
 */
app.post('/discord/community-webhook', async (req, res) => {
    try {
        if (!COMMUNITY_WEBHOOK_URL) {
            console.log('Community webhook not configured, skipping');
            return res.json({ success: false, message: 'Webhook not configured' });
        }

        // Require authenticated user to prevent fake webhook posts
        const { type, unified_id } = req.body;
        if (!unified_id || !isValidUnifiedId(unified_id)) {
            return res.status(400).json({ error: 'Valid unified_id is required' });
        }
        if (!redis) return res.status(503).json({ error: 'Service unavailable' });

        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }
        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }

        // Per-user webhook cooldown: max 1 post per 10 seconds
        // R12/L3: Atomic NX cooldown to prevent TOCTOU race
        const webhookCooldownKey = `webhook_cooldown:${unified_id}`;
        const webhookCooldownSet = await redis.set(webhookCooldownKey, '1', { nx: true, ex: 10 });
        if (!webhookCooldownSet) {
            return res.status(429).json({ error: 'Webhook cooldown active', retry_after: 10 });
        }

        // R11/M3: Global webhook rate limit (prevent mass-triggering from many users)
        const globalWebhookKey = 'webhook_global_limit';
        await redis.set(globalWebhookKey, 0, { nx: true, ex: 60 });
        const globalCount = await redis.incr(globalWebhookKey);
        if (globalCount > 30) {
            return res.status(429).json({ error: 'Community webhook rate limit reached', retry_after: 60 });
        }

        // Use display_name from stored user record, not from request body
        // Sanitize to prevent Discord markdown/mention injection (@everyone, @here, <@id>, **bold**, etc.)
        const rawName = user.display_name;
        if (!type || !rawName) {
            return res.status(400).json({ error: 'type is required and user must have a display name' });
        }
        const display_name = rawName.replace(/[@*_~`|<>\\]/g, '');

        let webhookPayload;

        if (type === 'achievement') {
            const { achievement_name: rawAchName, achievement_requirement: rawAchReq, image_name } = req.body;
            if (!rawAchName) {
                return res.status(400).json({ error: 'achievement_name is required for achievement type' });
            }
            // Validate achievement exists in known list
            const achNameStr = String(rawAchName);
            if (!VALID_ACHIEVEMENT_NAMES.has(achNameStr)) {
                return res.status(400).json({ error: 'Invalid achievement name' });
            }
            // Sanitize to prevent Discord markdown/mention injection (@everyone, @here, etc.)
            const achievement_name = achNameStr.replace(/[@*_~`|<>\\]/g, '');
            const achievement_requirement = rawAchReq ? String(rawAchReq).replace(/[@*_~`|<>\\]/g, '') : null;

            // Bambi themed achievement messages
            const achievementMessages = [
                `${display_name} is becoming such a good bambi~`,
                `${display_name}'s conditioning is working perfectly 💗`,
                `${display_name} dropped deeper and unlocked something special~`,
                `Good girl ${display_name}! The programming is taking hold 🌀`,
                `${display_name} is learning to obey so well~`,
                `${display_name}'s mind is becoming so empty and obedient 💕`,
            ];
            const randomMessage = achievementMessages[Math.floor(Math.random() * achievementMessages.length)];

            webhookPayload = {
                embeds: [{
                    title: '🌀 Achievement Unlocked 💗',
                    description: `**${randomMessage}**\n\n✨ *${achievement_name}*`,
                    color: 0xFF69B4, // Pink
                    fields: achievement_requirement ? [{
                        name: '📋 Requirement',
                        value: achievement_requirement,
                        inline: false
                    }] : [],
                    thumbnail: image_name ? { url: ACHIEVEMENTS_BASE_URL + encodeURIComponent(image_name) } : undefined,
                    footer: {
                        text: '🎀 Bambi Sleep Conditioning 🎀'
                    },
                    timestamp: new Date().toISOString()
                }]
            };
        } else if (type === 'level_up') {
            const { level, image_name } = req.body;
            if (typeof level !== 'number') {
                return res.status(400).json({ error: 'level is required for level_up type' });
            }
            // Validate level against user's actual stored level
            const userLevel = user.highest_level_ever || user.level || 0;
            if (level < 1 || level > 999 || level > userLevel) {
                return res.status(400).json({ error: 'Invalid level' });
            }

            // Bambi themed level up messages based on milestone
            let title, description, color;

            if (level % 100 === 0) {
                title = '🌟💗 PERFECT BAMBI 💗🌟';
                description = `**${display_name}** has reached **Level ${level}**!\n\n*Such deep conditioning... such a perfect empty doll~ 🎀*`;
                color = 0xFFD700; // Gold
            } else if (level % 50 === 0) {
                title = '✨🌀 Deep Drop ✨';
                description = `**${display_name}** sank to **Level ${level}**!\n\n*Going so deep... thoughts melting away~ 💕*`;
                color = 0x9B59B6; // Purple
            } else if (level % 25 === 0) {
                title = '💗 Good Girl Level Up 💗';
                description = `**${display_name}** reached **Level ${level}**!\n\n*The conditioning grows stronger~ 🌀*`;
                color = 0xFF69B4; // Pink
            } else if (level % 10 === 0) {
                title = '🎀 Dropping Deeper~';
                description = `**${display_name}** floated to **Level ${level}**!\n\n*Such a good bambi~*`;
                color = 0xFFB6C1; // Light pink
            } else {
                title = '💕 Level Up~';
                description = `**${display_name}** reached **Level ${level}**!`;
                color = 0xFF69B4; // Pink
            }

            webhookPayload = {
                embeds: [{
                    title: title,
                    description: description,
                    color: color,
                    thumbnail: image_name ? { url: ACHIEVEMENTS_BASE_URL + encodeURIComponent(image_name) } : undefined,
                    footer: {
                        text: '🎀 Bambi Sleep Conditioning 🎀'
                    },
                    timestamp: new Date().toISOString()
                }]
            };
        } else {
            return res.status(400).json({ error: 'Invalid type. Must be "achievement" or "level_up"' });
        }

        // Send to Discord webhook
        const response = await fetch(COMMUNITY_WEBHOOK_URL, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(webhookPayload)
        });

        if (!response.ok) {
            const errorText = await response.text();
            console.error('Discord webhook error:', response.status, errorText);
            return res.status(502).json({ error: 'Failed to post to Discord webhook' });
        }

        console.log(`Community webhook sent: ${type} for uid=${req.headers['x-unified-id'] || 'unknown'}`);
        res.json({ success: true });

    } catch (error) {
        console.error('Community webhook error:', error.message);
        res.status(500).json({ error: 'Failed to send webhook' });
    }
});

// =============================================================================
// AI CHAT ENDPOINTS
// =============================================================================

/**
 * POST /v2/ai/chat
 * Proxies chat requests to OpenRouter using V2 unified auth (any user with cloud identity).
 * No Patreon tier check — free for all authenticated users.
 * Requires: unified_id in body, X-Auth-Token header
 */
app.post('/v2/ai/chat', async (req, res) => {
    try {
        const { unified_id, messages, max_tokens: rawMaxTokens = 100, temperature: rawTemp = 0.95 } = req.body;
        const max_tokens = Math.min(Math.max(parseInt(rawMaxTokens) || 100, 1), 500);
        const temperature = Math.min(Math.max(parseFloat(rawTemp) || 0.95, 0), 2.0);

        if (!unified_id) {
            return res.status(400).json({ error: 'unified_id required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Look up user
        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }
        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Auth token validation
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }
        if (authCheck.legacy) {
            console.log(`[AUTH] ${authCheck.missing ? 'Old client (token not sent)' : 'Legacy user (no token hash)'} for ${unified_id} on /v2/ai/chat`);
        }

        // Determine daily limit based on user tier
        const hasPremium = (user.patreon_tier >= 1) || user.patreon_is_whitelisted;
        const userDailyLimit = hasPremium ? RATE_LIMIT.DAILY_REQUESTS : RATE_LIMIT.FREE_DAILY_REQUESTS;

        // Rate limit by unified_id
        const rateLimit = await checkRateLimit(unified_id, userDailyLimit);
        if (!rateLimit.allowed) {
            console.log(`Rate limit exceeded for V2 AI chat user ${unified_id}: ${rateLimit.used}/${userDailyLimit} (premium=${hasPremium})`);
            return res.status(429).json({
                error: 'Daily request limit exceeded',
                requests_remaining: 0,
                requests_used: rateLimit.used,
                limit: userDailyLimit
            });
        }

        // Validate OpenRouter is configured
        if (!CONFIG.OPENROUTER_API_KEY) {
            return res.status(503).json({ error: 'AI service not configured' });
        }

        if (!messages || !Array.isArray(messages)) {
            return res.status(400).json({ error: 'messages array is required' });
        }

        // Validate messages array
        if (messages.length > 50) {
            return res.status(400).json({ error: 'Too many messages (max 50)' });
        }
        const validRoles = ['system', 'user', 'assistant'];
        for (const msg of messages) {
            if (!msg.role || !validRoles.includes(msg.role) || typeof msg.content !== 'string') {
                return res.status(400).json({ error: 'Invalid message format' });
            }
            if (msg.content.length > 10000) {
                return res.status(400).json({ error: 'Message content too long (max 10000 chars)' });
            }
        }

        // Call OpenRouter (same as legacy endpoint)
        const openRouterResponse = await fetch('https://openrouter.ai/api/v1/chat/completions', {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${CONFIG.OPENROUTER_API_KEY}`,
                'Content-Type': 'application/json',
                'HTTP-Referer': 'https://github.com/ConditioningControlPanel',
                'X-Title': 'Conditioning Control Panel'
            },
            body: JSON.stringify({
                model: CONFIG.AI_MODEL,
                messages,
                max_tokens,
                temperature
            })
        });

        if (!openRouterResponse.ok) {
            const errorText = await openRouterResponse.text();
            console.error('OpenRouter error (V2):', errorText);
            return res.status(502).json({ error: 'AI service error' });
        }

        const completion = await openRouterResponse.json();
        const content = completion.choices?.[0]?.message?.content || '';

        res.json({
            content,
            requests_remaining: rateLimit.remaining,
            requests_used: rateLimit.used,
            limit: userDailyLimit
        });
    } catch (error) {
        console.error('V2 AI chat error:', error.message);
        res.status(500).json({ error: 'AI request failed' });
    }
});

/**
 * POST /ai/chat
 * Legacy: Proxies chat requests to OpenRouter after validating Patreon subscription
 * Requires: Authorization: Bearer <patreon_access_token>
 */
app.post('/ai/chat', async (req, res) => {
    try {
        // Validate Patreon token first
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        // Per-IP cooldown to prevent Patreon API amplification (5s between V1 AI calls)
        // R12/M6: Atomic NX cooldown to prevent TOCTOU race
        const chatIp = req.ip || 'unknown';
        if (redis) {
            const v1ChatCooldownKey = `v1_chat_cooldown:${chatIp}`;
            const cooldownSet = await redis.set(v1ChatCooldownKey, '1', { nx: true, ex: 5 });
            if (!cooldownSet) {
                return res.status(429).json({ error: 'V1 chat cooldown active', retry_after: 5 });
            }
        }

        const accessToken = authHeader.substring(7);

        // Verify subscription tier
        let tierInfo;
        try {
            const identity = await getPatreonIdentity(accessToken);
            tierInfo = determineTier(identity);
        } catch (error) {
            if (error.message === 'UNAUTHORIZED') {
                return res.status(401).json({ error: 'Patreon token expired' });
            }
            throw error;
        }

        // Fetch display_name for whitelist check (check unified user first, then legacy profile)
        const userId = tierInfo.patreon_user_id || 'unknown';
        let displayName = null;
        if (userId !== 'unknown') {
            try {
                // Check unified user first (canonical source)
                const unifiedLookup = await lookupUserByPatreon(userId);
                if (unifiedLookup.exists && unifiedLookup.display_name) {
                    displayName = unifiedLookup.display_name;
                } else {
                    // Fallback to legacy profile
                    const profileKey = `${PROFILE_KEY_PREFIX}${userId}`;
                    const profileData = await redis.get(profileKey);
                    if (profileData) {
                        const profile = typeof profileData === 'string' ? JSON.parse(profileData) : profileData;
                        displayName = profile.display_name || null;
                    }
                }
            } catch (e) { /* ignore */ }
        }

        // Check tier OR whitelist OR active patron status
        // (Active patrons with tier 0 may have payment processing delays - trust Patreon's active status)
        const whitelisted = isWhitelisted(tierInfo.patron_email, null, displayName);
        const effectiveTier = tierInfo.is_active && tierInfo.tier < 1 ? 1 : tierInfo.tier;

        if (effectiveTier < 1 && !whitelisted) {
            console.log(`Access denied for user ${userId}: active=${tierInfo.is_active}, tier=${tierInfo.tier}`);
            return res.status(403).json({
                error: 'Patreon Level 1 subscription required',
                tier: tierInfo.tier
            });
        }

        // Log for debugging active patrons with tier issues
        if (tierInfo.is_active && tierInfo.tier < 1) {
            console.log(`Active patron ${userId} has tier=${tierInfo.tier} - granting Level 1 access`);
        }

        // Check rate limit using Patreon user ID (userId already defined above)
        const rateLimit = await checkRateLimit(userId);

        if (!rateLimit.allowed) {
            console.log(`Rate limit exceeded for user ${userId}: ${rateLimit.used}/${RATE_LIMIT.DAILY_REQUESTS}`);
            return res.status(429).json({
                error: 'Daily request limit exceeded',
                requests_remaining: 0,
                requests_used: rateLimit.used,
                limit: RATE_LIMIT.DAILY_REQUESTS
            });
        }

        if (whitelisted) {
            console.log(`Whitelisted user: ${userId} - ${rateLimit.used}/${RATE_LIMIT.DAILY_REQUESTS} requests`);
        }

        // Validate OpenRouter is configured
        if (!CONFIG.OPENROUTER_API_KEY) {
            return res.status(503).json({ error: 'AI service not configured' });
        }

        // Parse request
        const { messages, max_tokens: rawMaxTokensLegacy = 100, temperature: rawTempLegacy = 0.95 } = req.body;
        const max_tokens = Math.min(Math.max(parseInt(rawMaxTokensLegacy) || 100, 1), 500);
        const temperature = Math.min(Math.max(parseFloat(rawTempLegacy) || 0.95, 0), 2.0);

        if (!messages || !Array.isArray(messages)) {
            return res.status(400).json({ error: 'messages array is required' });
        }

        // Validate messages array
        if (messages.length > 50) {
            return res.status(400).json({ error: 'Too many messages (max 50)' });
        }
        const validRolesLegacy = ['system', 'user', 'assistant'];
        for (const msg of messages) {
            if (!msg.role || !validRolesLegacy.includes(msg.role) || typeof msg.content !== 'string') {
                return res.status(400).json({ error: 'Invalid message format' });
            }
            if (msg.content.length > 10000) {
                return res.status(400).json({ error: 'Message content too long (max 10000 chars)' });
            }
        }

        // Call OpenRouter
        const openRouterResponse = await fetch('https://openrouter.ai/api/v1/chat/completions', {
            method: 'POST',
            headers: {
                'Authorization': `Bearer ${CONFIG.OPENROUTER_API_KEY}`,
                'Content-Type': 'application/json',
                'HTTP-Referer': 'https://github.com/ConditioningControlPanel',
                'X-Title': 'Conditioning Control Panel'
            },
            body: JSON.stringify({
                model: CONFIG.AI_MODEL,
                messages,
                max_tokens,
                temperature
            })
        });

        if (!openRouterResponse.ok) {
            const errorText = await openRouterResponse.text();
            console.error('OpenRouter error:', errorText);
            return res.status(502).json({ error: 'AI service error' });
        }

        const completion = await openRouterResponse.json();
        const content = completion.choices?.[0]?.message?.content || '';

        res.json({
            content,
            requests_remaining: rateLimit.remaining,
            requests_used: rateLimit.used,
            limit: RATE_LIMIT.DAILY_REQUESTS
        });
    } catch (error) {
        console.error('AI chat error:', error.message);
        res.status(500).json({ error: 'AI request failed' });
    }
});

// =============================================================================
// USER PROFILE / PROGRESSION SYNC
// =============================================================================

const PROFILE_KEY_PREFIX = 'profile:';

// =============================================================================
// UNIFIED USER SYSTEM - Links Patreon and Discord accounts under one identity
// =============================================================================

const UNIFIED_USER_PREFIX = 'user:';           // user:<unified_id> -> full profile
const PATREON_USER_INDEX = 'patreon_user:';    // patreon_user:<patreon_id> -> unified_id
const DISCORD_USER_INDEX = 'discord_user:';    // discord_user:<discord_id> -> unified_id

/**
 * Generate a unique unified ID using cryptographically secure randomness
 */
function generateUnifiedId() {
    const timestamp = Date.now().toString(36);
    const random = crypto.randomBytes(6).toString('hex');
    return `u_${timestamp}${random}`;
}

/**
 * Look up unified user by Patreon ID
 * Returns: { exists, unified_id, user, needs_registration }
 */
async function lookupUserByPatreon(patreonId) {
    if (!redis) return { exists: false, needs_registration: true };

    try {
        // Check index first
        const unifiedId = await redis.get(`${PATREON_USER_INDEX}${patreonId}`);
        if (unifiedId) {
            const user = await redis.get(`${UNIFIED_USER_PREFIX}${unifiedId}`);
            if (user) {
                const userData = typeof user === 'string' ? JSON.parse(user) : user;

                // IMPORTANT: If unified user has no display_name, return exists: false
                // This allows the Patreon to be properly linked to an existing Discord account
                if (!userData.display_name || userData.display_name.trim() === '') {
                    console.log(`lookupUserByPatreon: User ${unifiedId} has no display_name - returning exists: false (allows linking)`);
                    return { exists: false, needs_registration: true, has_unified_user: true };
                }

                return {
                    exists: true,
                    unified_id: unifiedId,
                    user: userData,
                    has_display_name: !!userData.display_name,
                    display_name: userData.display_name,
                    has_discord: !!userData.discord_id,
                    needs_registration: false
                };
            }
        }

        // Fall back to legacy profile lookup for migration
        const legacyKey = `${PROFILE_KEY_PREFIX}${patreonId}`;
        const legacyProfile = await redis.get(legacyKey);
        if (legacyProfile) {
            const profileData = typeof legacyProfile === 'string' ? JSON.parse(legacyProfile) : legacyProfile;

            // IMPORTANT: If legacy profile has no display_name, don't auto-migrate!
            // This allows the Patreon account to be properly linked to an existing Discord account
            // instead of creating a conflicting new unified user.
            if (!profileData.display_name || profileData.display_name.trim() === '') {
                console.log(`lookupUserByPatreon: Skipping migration for ${patreonId} - no display_name (allows linking to existing account)`);
                return { exists: false, needs_registration: true, has_legacy_profile: true };
            }

            // Migrate on-the-fly (only if has display_name)
            const migratedUser = await migratePatreonToUnified(patreonId, profileData);
            return {
                exists: true,
                unified_id: migratedUser.unified_id,
                user: migratedUser,
                has_display_name: !!migratedUser.display_name,
                display_name: migratedUser.display_name,
                has_discord: !!migratedUser.discord_id,
                needs_registration: !migratedUser.display_name,
                migrated: true
            };
        }

        return { exists: false, needs_registration: true };
    } catch (error) {
        console.error('lookupUserByPatreon error:', error);
        return { exists: false, needs_registration: true };
    }
}

/**
 * Look up unified user by Discord ID
 * Returns: { exists, unified_id, user, needs_registration }
 */
async function lookupUserByDiscord(discordId) {
    if (!redis) return { exists: false, needs_registration: true };

    try {
        // Check index first
        const unifiedId = await redis.get(`${DISCORD_USER_INDEX}${discordId}`);
        if (unifiedId) {
            const user = await redis.get(`${UNIFIED_USER_PREFIX}${unifiedId}`);
            if (user) {
                const userData = typeof user === 'string' ? JSON.parse(user) : user;

                // IMPORTANT: If unified user has no display_name, return exists: false
                // This allows proper account setup flow
                if (!userData.display_name || userData.display_name.trim() === '') {
                    console.log(`lookupUserByDiscord: User ${unifiedId} has no display_name - returning exists: false`);
                    return { exists: false, needs_registration: true, has_unified_user: true };
                }

                return {
                    exists: true,
                    unified_id: unifiedId,
                    user: userData,
                    has_display_name: !!userData.display_name,
                    display_name: userData.display_name,
                    has_patreon: !!userData.patreon_id,
                    needs_registration: false
                };
            }
        }

        // Fall back to legacy Discord profile lookup for migration
        const legacyKey = `discord_profile:${discordId}`;
        const legacyProfile = await redis.get(legacyKey);
        if (legacyProfile) {
            const profileData = typeof legacyProfile === 'string' ? JSON.parse(legacyProfile) : legacyProfile;
            // Migrate on-the-fly
            const migratedUser = await migrateDiscordToUnified(discordId, profileData);
            return {
                exists: true,
                unified_id: migratedUser.unified_id,
                user: migratedUser,
                has_display_name: !!migratedUser.display_name,
                display_name: migratedUser.display_name,
                has_patreon: !!migratedUser.patreon_id,
                needs_registration: !migratedUser.display_name,
                migrated: true
            };
        }

        return { exists: false, needs_registration: true };
    } catch (error) {
        console.error('lookupUserByDiscord error:', error);
        return { exists: false, needs_registration: true };
    }
}

/**
 * Look up unified user by email (for auto-linking)
 */
async function lookupUserByEmail(email) {
    if (!redis || !email) return null;

    try {
        const emailHash = hashEmail(email);
        const unifiedId = await redis.get(`email_index:${emailHash}`);
        if (unifiedId) {
            const user = await redis.get(`${UNIFIED_USER_PREFIX}${unifiedId}`);
            if (user) {
                return {
                    unified_id: unifiedId,
                    user: typeof user === 'string' ? JSON.parse(user) : user
                };
            }
        }
        return null;
    } catch (error) {
        console.error('lookupUserByEmail error:', error);
        return null;
    }
}

/**
 * Migrate a legacy Patreon profile to unified user system
 */
async function migratePatreonToUnified(patreonId, legacyProfile) {
    const unifiedId = generateUnifiedId();

    // Archive old progression into all_time_stats (V1 data is from season 0)
    const oldStats = legacyProfile.stats || {};
    const oldAllTime = legacyProfile.all_time_stats || {};
    const allTimeStats = {
        total_flashes: (oldAllTime.total_flashes || 0) + (oldStats.total_flashes || 0),
        total_bubbles_popped: (oldAllTime.total_bubbles_popped || 0) + (oldStats.total_bubbles_popped || 0),
        total_video_minutes: (oldAllTime.total_video_minutes || 0) + (oldStats.total_video_minutes || 0),
        total_lock_cards_completed: (oldAllTime.total_lock_cards_completed || 0) + (oldStats.total_lock_cards_completed || 0),
    };

    const highestLevel = Math.max(legacyProfile.level || 1, legacyProfile.highest_level_ever || 0);

    const unifiedUser = {
        unified_id: unifiedId,
        display_name: legacyProfile.display_name || null,
        display_name_set_at: legacyProfile.display_name_set_at || null,

        // Linked providers
        patreon_id: patreonId,
        discord_id: legacyProfile.discord_id || null,
        email_hash: hashEmail(legacyProfile.email || legacyProfile.patron_email) || null,

        // Progression — reset for current season
        xp: 0,
        level: 1,
        achievements: legacyProfile.achievements || [],
        stats: {},
        last_session: legacyProfile.last_session || null,

        // Patreon-specific
        patreon_tier: legacyProfile.patreon_tier || 0,
        patreon_is_active: legacyProfile.patreon_is_active || false,
        patreon_is_whitelisted: legacyProfile.patreon_is_whitelisted || false,

        // Discord-specific (discord_username intentionally not stored — PII)
        allow_discord_dm: legacyProfile.allow_discord_dm || false,

        // Season
        current_season: getCurrentSeason(),
        highest_level_ever: highestLevel,
        unlocks: calculateUnlocks(highestLevel),
        all_time_stats: allTimeStats,
        is_season0_og: (legacyProfile.achievements?.length > 0) || (highestLevel > 1),
        level_reset_at: new Date().toISOString(),

        // Metadata
        created_at: legacyProfile.created_at || new Date().toISOString(),
        updated_at: new Date().toISOString(),
        migrated_from: 'patreon',
        migrated_at: new Date().toISOString()
    };

    // Save unified user
    await redis.set(`${UNIFIED_USER_PREFIX}${unifiedId}`, JSON.stringify(unifiedUser));

    // Create indexes
    await redis.set(`${PATREON_USER_INDEX}${patreonId}`, unifiedId);
    if (unifiedUser.discord_id) {
        await redis.set(`${DISCORD_USER_INDEX}${unifiedUser.discord_id}`, unifiedId);
    }
    if (unifiedUser.email_hash) {
        await redis.set(`email_index:${unifiedUser.email_hash}`, unifiedId);
    }
    if (unifiedUser.display_name) {
        await redis.set(`display_name_index:${unifiedUser.display_name.toLowerCase()}`, unifiedId);
    }

    console.log(`Migrated Patreon user ${patreonId} to unified user ${unifiedId}`);
    return unifiedUser;
}

/**
 * Migrate a legacy Discord profile to unified user system
 */
async function migrateDiscordToUnified(discordId, legacyProfile) {
    // Check if there's a linked Patreon account we should merge with
    if (legacyProfile.linked_patreon_id) {
        const patreonLookup = await lookupUserByPatreon(legacyProfile.linked_patreon_id);
        if (patreonLookup.exists) {
            // Add Discord to existing unified user
            const user = patreonLookup.user;
            user.discord_id = discordId;
            // discord_username intentionally not stored — PII
            user.allow_discord_dm = legacyProfile.allow_discord_dm || false;
            user.updated_at = new Date().toISOString();

            // Track highest level from legacy data for unlocks (but don't inflate current season XP/level)
            const legacyHighest = Math.max(legacyProfile.level || 1, legacyProfile.highest_level_ever || 0);
            user.highest_level_ever = Math.max(user.highest_level_ever || 0, legacyHighest);
            user.unlocks = calculateUnlocks(user.highest_level_ever);

            await redis.set(`${UNIFIED_USER_PREFIX}${patreonLookup.unified_id}`, JSON.stringify(user));
            await redis.set(`${DISCORD_USER_INDEX}${discordId}`, patreonLookup.unified_id);

            console.log(`Merged Discord ${discordId} into unified user ${patreonLookup.unified_id}`);
            return user;
        }
    }

    // Create new unified user from Discord
    const unifiedId = generateUnifiedId();

    // Archive old progression into all_time_stats (V1 data is from season 0)
    const oldStats = legacyProfile.stats || {};
    const oldAllTime = legacyProfile.all_time_stats || {};
    const allTimeStats = {
        total_flashes: (oldAllTime.total_flashes || 0) + (oldStats.total_flashes || 0),
        total_bubbles_popped: (oldAllTime.total_bubbles_popped || 0) + (oldStats.total_bubbles_popped || 0),
        total_video_minutes: (oldAllTime.total_video_minutes || 0) + (oldStats.total_video_minutes || 0),
        total_lock_cards_completed: (oldAllTime.total_lock_cards_completed || 0) + (oldStats.total_lock_cards_completed || 0),
    };

    const highestLevel = Math.max(legacyProfile.level || 1, legacyProfile.highest_level_ever || 0);

    const unifiedUser = {
        unified_id: unifiedId,
        display_name: legacyProfile.display_name || null,
        display_name_set_at: legacyProfile.display_name_set_at || null,

        // Linked providers
        patreon_id: null,
        discord_id: discordId,
        email_hash: hashEmail(legacyProfile.email) || null,

        // Progression — reset for current season
        xp: 0,
        level: 1,
        achievements: legacyProfile.achievements || [],
        stats: {},
        last_session: legacyProfile.last_session || null,

        // Patreon-specific (will be filled when linked)
        patreon_tier: 0,
        patreon_is_active: false,
        patreon_is_whitelisted: false,

        // Discord-specific (discord_username intentionally not stored — PII)
        allow_discord_dm: legacyProfile.allow_discord_dm || false,

        // Season
        current_season: getCurrentSeason(),
        highest_level_ever: highestLevel,
        unlocks: calculateUnlocks(highestLevel),
        all_time_stats: allTimeStats,
        is_season0_og: (legacyProfile.achievements?.length > 0) || (highestLevel > 1),
        level_reset_at: new Date().toISOString(),

        // Metadata
        created_at: legacyProfile.created_at || new Date().toISOString(),
        updated_at: new Date().toISOString(),
        migrated_from: 'discord',
        migrated_at: new Date().toISOString()
    };

    // Save unified user
    await redis.set(`${UNIFIED_USER_PREFIX}${unifiedId}`, JSON.stringify(unifiedUser));

    // Create indexes (both V1 and V2 key formats)
    await redis.set(`${DISCORD_USER_INDEX}${discordId}`, unifiedId);
    await redis.set(`discord_index:${discordId}`, unifiedId);
    if (unifiedUser.email_hash) {
        await redis.set(`email_index:${unifiedUser.email_hash}`, unifiedId);
    }
    if (unifiedUser.display_name) {
        await redis.set(`display_name_index:${unifiedUser.display_name.toLowerCase()}`, unifiedId);
    }

    console.log(`Migrated Discord user ${discordId} to unified user ${unifiedId}`);
    return unifiedUser;
}

/**
 * Register a new unified user (first-time login)
 */
async function registerUnifiedUser(displayName, provider, providerId, providerData) {
    if (!redis) throw new Error('Storage not available');

    const normalizedName = displayName.trim();
    const indexKey = `display_name_index:${normalizedName.toLowerCase()}`;

    // R8/M2: Acquire name lock to prevent TOCTOU race on name uniqueness
    const nameLockKey = `name_lock:${normalizedName.toLowerCase()}`;
    const nameLockAcquired = await redis.set(nameLockKey, '1', { nx: true, ex: 10 });
    if (!nameLockAcquired) {
        return { success: false, error: 'Display name is being registered by another user' };
    }

    try {
        // Check if name is taken
        const existingId = await redis.get(indexKey);
        if (existingId) {
            // Check if it's a claimable account (same email via hash comparison)
            const existingUser = await redis.get(`${UNIFIED_USER_PREFIX}${existingId}`);
            if (existingUser) {
                const userData = typeof existingUser === 'string' ? JSON.parse(existingUser) : existingUser;
                const canClaim = providerData.email && userData.email_hash &&
                                hashEmail(providerData.email) === userData.email_hash;
                return { success: false, error: 'Display name already taken', can_claim: !!canClaim, existing_unified_id: existingId };
            }
        }

        const unifiedId = generateUnifiedId();

        const unifiedUser = {
            unified_id: unifiedId,
            display_name: normalizedName,
            display_name_set_at: new Date().toISOString(),

            // Linked providers
            patreon_id: provider === 'patreon' ? providerId : null,
            discord_id: provider === 'discord' ? providerId : null,
            email_hash: hashEmail(providerData.email) || null,

            // Progression (starting fresh)
            xp: 0,
            level: 1,
            achievements: [],
            stats: {},
            last_session: new Date().toISOString(),

            // Patreon-specific
            patreon_tier: providerData.tier || 0,
            patreon_is_active: providerData.is_active || false,
            patreon_is_whitelisted: providerData.is_whitelisted || false,

            // Discord-specific (discord_username intentionally not stored — PII)
            allow_discord_dm: false,

            // Metadata
            created_at: new Date().toISOString(),
            updated_at: new Date().toISOString()
        };

        // Check whitelist by display name (use fresh provider email, not stored hash)
        if (isWhitelisted(providerData.email, null, normalizedName)) {
            unifiedUser.patreon_is_whitelisted = true;
            unifiedUser.patreon_tier = Math.max(unifiedUser.patreon_tier, 2);
        }

        // Save unified user
        await redis.set(`${UNIFIED_USER_PREFIX}${unifiedId}`, JSON.stringify(unifiedUser));

        // Create indexes
        await redis.set(indexKey, unifiedId);
        if (provider === 'patreon') {
            await redis.set(`${PATREON_USER_INDEX}${providerId}`, unifiedId);
        } else {
            await redis.set(`${DISCORD_USER_INDEX}${providerId}`, unifiedId);
        }
        if (unifiedUser.email_hash) {
            await redis.set(`email_index:${unifiedUser.email_hash}`, unifiedId);
        }

        console.log(`Registered new unified user ${unifiedId} with display name "${normalizedName}" via ${provider}`);
        await incrementTotalUserCount();
        return { success: true, unified_id: unifiedId, user: unifiedUser };
    } finally {
        await redis.del(nameLockKey).catch(() => {});
    }
}

/**
 * Link a second provider to an existing unified user
 */
async function linkProviderToUser(unifiedId, provider, providerId, providerData) {
    if (!redis) throw new Error('Storage not available');

    const userKey = `${UNIFIED_USER_PREFIX}${unifiedId}`;
    const existing = await redis.get(userKey);
    if (!existing) {
        return { success: false, error: 'User not found' };
    }

    const user = typeof existing === 'string' ? JSON.parse(existing) : existing;

    // Check if provider is already linked to a different user
    const existingLink = provider === 'patreon'
        ? await redis.get(`${PATREON_USER_INDEX}${providerId}`)
        : await redis.get(`${DISCORD_USER_INDEX}${providerId}`);

    if (existingLink && existingLink !== unifiedId) {
        return { success: false, error: 'This account is already linked to a different user' };
    }

    // Update user with new provider
    if (provider === 'patreon') {
        user.patreon_id = providerId;
        user.patreon_tier = providerData.tier || user.patreon_tier;
        user.patreon_is_active = providerData.is_active ?? user.patreon_is_active;
        user.patreon_is_whitelisted = providerData.is_whitelisted ?? user.patreon_is_whitelisted;
        await redis.set(`${PATREON_USER_INDEX}${providerId}`, unifiedId);
    } else {
        user.discord_id = providerId;
        // discord_username intentionally not stored — PII
        await redis.set(`${DISCORD_USER_INDEX}${providerId}`, unifiedId);
    }

    // Update email_hash if not set
    if (!user.email_hash && providerData.email) {
        user.email_hash = hashEmail(providerData.email);
        await redis.set(`email_index:${user.email_hash}`, unifiedId);
    }

    user.updated_at = new Date().toISOString();

    // Re-check whitelist with new data (use fresh provider email, not stored hash)
    if (isWhitelisted(providerData.email, null, user.display_name)) {
        user.patreon_is_whitelisted = true;
        user.patreon_tier = Math.max(user.patreon_tier, 2);
    }

    await redis.set(userKey, JSON.stringify(user));

    console.log(`Linked ${provider} ${providerId} to unified user ${unifiedId}`);
    return { success: true, unified_id: unifiedId, user };
}

/**
 * Update unified user's Patreon status
 */
async function updateUnifiedUserPatreonStatus(unifiedId, patreonData) {
    if (!redis) return;

    const userKey = `${UNIFIED_USER_PREFIX}${unifiedId}`;
    const existing = await redis.get(userKey);
    if (!existing) return;

    const user = typeof existing === 'string' ? JSON.parse(existing) : existing;

    user.patreon_tier = patreonData.tier ?? user.patreon_tier;
    user.patreon_is_active = patreonData.is_active ?? user.patreon_is_active;
    user.patreon_is_whitelisted = patreonData.is_whitelisted ?? user.patreon_is_whitelisted;
    user.updated_at = new Date().toISOString();

    await redis.set(userKey, JSON.stringify(user));
}

// =============================================================================
// UNIFIED AUTH ENDPOINTS
// =============================================================================

/**
 * POST /auth/lookup
 * Check if a provider account is already linked to a unified user
 * Body: { provider: "patreon"|"discord" }
 * Requires: Authorization header with provider's access token
 */
app.post('/auth/lookup', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        const accessToken = authHeader.substring(7);
        const { provider } = req.body;

        if (!provider || !['patreon', 'discord'].includes(provider)) {
            return res.status(400).json({ error: 'Valid provider required (patreon or discord)' });
        }

        let providerId, email, result;

        if (provider === 'patreon') {
            const identity = await getPatreonIdentity(accessToken);
            const tierInfo = determineTier(identity);
            providerId = tierInfo.patreon_user_id;
            email = tierInfo.patron_email;

            result = await lookupUserByPatreon(providerId);
            result.provider_data = {
                tier: tierInfo.tier,
                is_active: tierInfo.is_active,
                is_whitelisted: isWhitelisted(email, null, result.display_name)
            };
        } else {
            const discordUser = await getDiscordUser(accessToken);
            providerId = discordUser.id;
            email = discordUser.email;

            result = await lookupUserByDiscord(providerId);
            result.provider_data = {};
        }

        // If no unified user found, check for email-based auto-link opportunity
        if (!result.exists && email) {
            const emailLookup = await lookupUserByEmail(email);
            if (emailLookup) {
                result.can_auto_link = true;
                result.auto_link_unified_id = emailLookup.unified_id;
                result.auto_link_display_name = emailLookup.user.display_name;
            }
        }

        // Filter user object to safe fields only (never expose auth_token_hash, email_hash, etc.)
        if (result.user) {
            result.user = filterUserForResponse(result.user);
        }

        res.json(result);
    } catch (error) {
        console.error('Auth lookup error:', error.message);
        res.status(500).json({ error: 'Lookup failed' });
    }
});

/**
 * POST /auth/register
 * Register a new unified user (first-time login)
 * Body: { display_name: string, provider: "patreon"|"discord" }
 * Requires: Authorization header with provider's access token
 */
app.post('/auth/register', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        const accessToken = authHeader.substring(7);
        const { display_name, provider } = req.body;

        if (!display_name || typeof display_name !== 'string') {
            return res.status(400).json({ error: 'Display name required' });
        }

        if (!provider || !['patreon', 'discord'].includes(provider)) {
            return res.status(400).json({ error: 'Valid provider required (patreon or discord)' });
        }

        const trimmedName = display_name.trim();
        if (trimmedName.length < 3 || trimmedName.length > 20) {
            return res.status(400).json({ error: 'Display name must be 3-20 characters' });
        }

        if (!isValidDisplayNameChars(trimmedName)) {
            return res.status(400).json({ error: 'Display name contains invalid characters' });
        }

        let providerId, providerData;

        if (provider === 'patreon') {
            const identity = await getPatreonIdentity(accessToken);
            const tierInfo = determineTier(identity);
            providerId = tierInfo.patreon_user_id;
            providerData = {
                email: tierInfo.patron_email, // used internally for hashing, not sent to client
                tier: tierInfo.tier,
                is_active: tierInfo.is_active,
                is_whitelisted: isWhitelisted(tierInfo.patron_email, null, null)
            };
        } else {
            const discordUser = await getDiscordUser(accessToken);
            providerId = discordUser.id;
            providerData = {
                // discord_username intentionally not stored — PII
                email: discordUser.email // used internally for hashing, not sent to client
            };
        }

        // Check if provider is already registered (shouldn't happen, but safety check)
        const existingLookup = provider === 'patreon'
            ? await lookupUserByPatreon(providerId)
            : await lookupUserByDiscord(providerId);

        if (existingLookup.exists && existingLookup.has_display_name) {
            return res.status(409).json({
                error: 'Account already registered',
                unified_id: existingLookup.unified_id,
                display_name: existingLookup.display_name
            });
        }

        const result = await registerUnifiedUser(trimmedName, provider, providerId, providerData);

        if (!result.success) {
            return res.status(409).json(result);
        }

        const safeUser = filterUserForResponse(result.user);
        res.json({
            success: true,
            unified_id: result.unified_id,
            display_name: safeUser.display_name,
            user: safeUser
        });
    } catch (error) {
        console.error('Auth register error:', error.message);
        res.status(500).json({ error: 'Registration failed' });
    }
});

/**
 * POST /auth/link-provider
 * Link a second provider to an existing unified user
 * Body: { unified_id: string }
 * Requires: Authorization header with NEW provider's access token
 */
app.post('/auth/link-provider', async (req, res) => {
    try {
        // R11/H3: Per-IP rate limit on link-provider (prevents brute-force enumeration)
        if (redis) {
            const linkLimitKey = `link_provider_limit:${req.ip}`;
            await redis.set(linkLimitKey, 0, { nx: true, ex: 300 });
            const linkCount = await redis.incr(linkLimitKey);
            if (linkCount > 10) {
                return res.status(429).json({ error: 'Too many link attempts. Try again later.', retry_after: 300 });
            }
        }

        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        const accessToken = authHeader.substring(7);
        let { unified_id, provider } = req.body;

        // Try to determine provider from token
        let providerId, providerData;
        let detectedProvider = provider;

        // Try Patreon first
        try {
            const identity = await getPatreonIdentity(accessToken);
            const tierInfo = determineTier(identity);
            providerId = tierInfo.patreon_user_id;
            detectedProvider = 'patreon';
            providerData = {
                email: tierInfo.patron_email, // used internally for hashing, not sent to client
                tier: tierInfo.tier,
                is_active: tierInfo.is_active,
                is_whitelisted: isWhitelisted(tierInfo.patron_email, null, null)
            };
        } catch (patreonError) {
            // Try Discord
            try {
                const discordUser = await getDiscordUser(accessToken);
                providerId = discordUser.id;
                detectedProvider = 'discord';
                providerData = {
                    // discord_username intentionally not stored — PII
                    email: discordUser.email // used internally for hashing, not sent to client
                };
            } catch (discordError) {
                return res.status(401).json({ error: 'Invalid access token' });
            }
        }

        // unified_id is required — no more email-based auto-linking (prevents account takeover)
        if (!unified_id) {
            return res.status(400).json({ error: 'unified_id is required. Use the auth endpoints to find your account first.' });
        }

        // Validate unified_id format to prevent Redis key injection
        if (!isValidUnifiedId(unified_id)) {
            return res.status(400).json({ error: 'Invalid unified_id format' });
        }

        // Verify the caller owns this account via X-Auth-Token (prevents IDOR — linking to someone else's account)
        const targetUserData = await redis.get(`user:${unified_id}`);
        if (!targetUserData) {
            return res.status(404).json({ error: 'Account not found' });
        }
        const targetUser = typeof targetUserData === 'string' ? JSON.parse(targetUserData) : targetUserData;
        const authCheck = validateAuthToken(req, targetUser);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'X-Auth-Token required to link providers to this account' });
        }

        const result = await linkProviderToUser(unified_id, detectedProvider, providerId, providerData);

        if (!result.success) {
            return res.status(409).json(result);
        }

        const safeUser = filterUserForResponse(result.user);
        res.json({
            success: true,
            unified_id: result.unified_id,
            linked_providers: [
                safeUser.patreon_id ? 'patreon' : null,
                safeUser.discord_id ? 'discord' : null
            ].filter(Boolean),
            user: safeUser
        });
    } catch (error) {
        console.error('Auth link-provider error:', error.message);
        res.status(500).json({ error: 'Linking failed' });
    }
});

/**
 * POST /auth/claim
 * Claim an existing display name (link accounts with same owner)
 * Body: { display_name: string, provider: "patreon"|"discord" }
 */
app.post('/auth/claim', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        const accessToken = authHeader.substring(7);
        const { display_name, provider } = req.body;

        if (!display_name || !provider) {
            return res.status(400).json({ error: 'display_name and provider required' });
        }

        // Look up the existing user with this display name
        const indexKey = `display_name_index:${display_name.trim().toLowerCase()}`;
        const existingUnifiedId = await redis.get(indexKey);

        if (!existingUnifiedId) {
            return res.status(404).json({ error: 'Display name not found' });
        }

        let providerId, providerData;

        if (provider === 'patreon') {
            const identity = await getPatreonIdentity(accessToken);
            const tierInfo = determineTier(identity);
            providerId = tierInfo.patreon_user_id;
            providerData = {
                email: tierInfo.patron_email, // used internally for hashing, not sent to client
                tier: tierInfo.tier,
                is_active: tierInfo.is_active,
                is_whitelisted: isWhitelisted(tierInfo.patron_email, null, display_name)
            };
        } else {
            const discordUser = await getDiscordUser(accessToken);
            providerId = discordUser.id;
            providerData = {
                // discord_username intentionally not stored — PII
                email: discordUser.email // used internally for hashing, not sent to client
            };
        }

        // Verify claim is legitimate (same email)
        const existingUser = await redis.get(`${UNIFIED_USER_PREFIX}${existingUnifiedId}`);
        if (!existingUser) {
            return res.status(404).json({ error: 'User not found' });
        }

        const userData = typeof existingUser === 'string' ? JSON.parse(existingUser) : existingUser;

        // R8/H1: Require auth token to prevent unauthorized account linking
        const authCheck = validateAuthToken(req, userData);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Auth token required to claim account' });
        }

        // Check if emails match (compare fresh provider email hash against stored hash)
        if (!providerData.email || !userData.email_hash ||
            hashEmail(providerData.email) !== userData.email_hash) {
            return res.status(403).json({
                error: 'Cannot claim this account - email does not match',
                hint: 'Make sure you are using the same email address on both platforms'
            });
        }

        // Link the provider
        const result = await linkProviderToUser(existingUnifiedId, provider, providerId, providerData);

        if (!result.success) {
            return res.status(409).json(result);
        }

        const safeUser = filterUserForResponse(result.user);
        res.json({
            success: true,
            unified_id: result.unified_id,
            display_name: safeUser.display_name,
            linked_providers: [
                safeUser.patreon_id ? 'patreon' : null,
                safeUser.discord_id ? 'discord' : null
            ].filter(Boolean),
            user: safeUser
        });
    } catch (error) {
        console.error('Auth claim error:', error.message);
        res.status(500).json({ error: 'Claim failed' });
    }
});

// =============================================================================
// LEGACY PROFILE ENDPOINTS (with unified user integration)
// =============================================================================

/**
 * GET /user/profile
 * Load user's progression data (XP, level, achievements, stats)
 * Requires: Authorization: Bearer <patreon_access_token>
 */
app.get('/user/profile', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        // Per-IP cooldown to prevent Patreon API amplification
        // R12/M6: Atomic NX cooldown to prevent TOCTOU race
        const profileIp = req.ip || 'unknown';
        if (redis) {
            const v1ProfileCooldownKey = `v1_profile_cooldown:${profileIp}`;
            const cooldownSet = await redis.set(v1ProfileCooldownKey, '1', { nx: true, ex: 10 });
            if (!cooldownSet) {
                return res.status(429).json({ error: 'V1 profile cooldown active', retry_after: 10 });
            }
        }

        const accessToken = authHeader.substring(7);
        const identity = await getPatreonIdentity(accessToken);
        const tierInfo = determineTier(identity);

        const userId = tierInfo.patreon_user_id;
        if (!userId) {
            return res.status(401).json({ error: 'Could not identify user' });
        }

        // Check if Redis is available
        if (!redis) {
            return res.status(503).json({ error: 'Profile storage not available' });
        }

        // FIRST: Check for unified user via patreon_user index
        const unifiedUserId = await redis.get(`patreon_user:${userId}`);
        if (unifiedUserId) {
            const unifiedUserKey = `user:${unifiedUserId}`;
            const unifiedUserData = await redis.get(unifiedUserKey);
            if (unifiedUserData) {
                const unifiedUser = typeof unifiedUserData === 'string' ? JSON.parse(unifiedUserData) : unifiedUserData;
                console.log(`Found unified user for Patreon ${userId}: ${unifiedUserId} (Level ${unifiedUser.level})`);
                return res.json({
                    exists: true,
                    user_id: userId,
                    unified_user_id: unifiedUserId,
                    profile: {
                        display_name: unifiedUser.display_name,
                        xp: unifiedUser.xp || 0,
                        level: unifiedUser.level || 1,
                        achievements: unifiedUser.achievements || [],
                        stats: unifiedUser.stats || {},
                        updated_at: unifiedUser.updated_at,
                        reset_weekly_quest: unifiedUser.reset_weekly_quest || false,
                        reset_daily_quest: unifiedUser.reset_daily_quest || false,
                        skill_points: unifiedUser.skill_points || 0,
                        unlocked_skills: unifiedUser.unlocked_skills || []
                    }
                });
            }
        }

        const key = `${PROFILE_KEY_PREFIX}${userId}`;
        const profile = await redis.get(key);

        if (!profile) {
            // No profile found - return empty/default
            console.log(`No profile found for user ${userId}`);
            return res.json({
                exists: false,
                user_id: userId
            });
        }

        console.log(`Loaded profile for user ${userId}`);

        // Profile is stored as JSON string
        const profileData = typeof profile === 'string' ? JSON.parse(profile) : profile;

        // Filter profile to safe fields only (never expose auth_token_hash, anti_cheat_flags, etc.)
        res.json({
            exists: true,
            user_id: userId,
            profile: {
                display_name: profileData.display_name || null,
                xp: profileData.xp || 0,
                level: profileData.level || 1,
                achievements: profileData.achievements || [],
                stats: profileData.stats || {},
                updated_at: profileData.updated_at,
                last_session: profileData.last_session
            }
        });
    } catch (error) {
        console.error('Profile load error:', error.message);
        res.status(500).json({ error: 'Failed to load profile' });
    }
});

/**
 * POST /user/sync
 * Save user's progression data (XP, level, achievements, stats)
 * Requires: Authorization: Bearer <patreon_access_token>
 * Body: { xp, level, achievements[], stats{} }
 */
app.post('/user/sync', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        // Per-IP cooldown to prevent Patreon API amplification
        // R12/M6: Atomic NX cooldown to prevent TOCTOU race
        const syncIp = req.ip || 'unknown';
        if (redis) {
            const v1SyncCooldownKey = `v1_sync_cooldown:${syncIp}`;
            const cooldownSet = await redis.set(v1SyncCooldownKey, '1', { nx: true, ex: 30 });
            if (!cooldownSet) {
                return res.status(429).json({ error: 'V1 sync cooldown active', retry_after: 30 });
            }
        }

        const accessToken = authHeader.substring(7);
        const identity = await getPatreonIdentity(accessToken);
        const tierInfo = determineTier(identity);

        const userId = tierInfo.patreon_user_id;
        if (!userId) {
            return res.status(401).json({ error: 'Could not identify user' });
        }

        // Check if Redis is available
        if (!redis) {
            return res.status(503).json({ error: 'Profile storage not available' });
        }

        const { xp, level, achievements, stats, last_session, allow_discord_dm, share_profile_picture, show_online_status, discord_id, display_name: requestDisplayName, avatar_url, reset_weekly_quest, reset_daily_quest, force_streak_override: clientForceStreakOverride } = req.body;

        // Validate required fields
        if (typeof xp !== 'number' || !isFinite(xp) || typeof level !== 'number' || !isFinite(level)) {
            return res.status(400).json({ error: 'xp and level are required finite numbers' });
        }

        // Load existing profile to merge/compare
        const key = `${PROFILE_KEY_PREFIX}${userId}`;
        const existingProfile = await redis.get(key);
        let existing = null;

        if (existingProfile) {
            existing = typeof existingProfile === 'string' ? JSON.parse(existingProfile) : existingProfile;
        }

        // Check Patreon status live during sync (pledge >= 5 cents OR whitelisted)
        // Now checks display_name from existing profile OR request body
        const displayNameToCheck = existing?.display_name || requestDisplayName || null;
        const isWhitelistedUser = isWhitelisted(tierInfo.patron_email, null, displayNameToCheck);
        const hasActivePledge = tierInfo.is_active && tierInfo.pledge_cents >= 5;
        const effectiveTier = (isWhitelistedUser || hasActivePledge) ? Math.max(tierInfo.tier, isWhitelistedUser ? 2 : 1) : tierInfo.tier;

        // Merge achievements (union of local and server, capped at 500)
        let mergedAchievements = [];
        if (Array.isArray(achievements)) {
            mergedAchievements = achievements
                .filter(a => typeof a === 'string' && a.length <= 100)
                .slice(0, 500);
        }
        if (existing?.achievements) {
            const achievementSet = new Set([...existing.achievements, ...mergedAchievements]);
            mergedAchievements = Array.from(achievementSet).slice(0, 500);
        }

        // Anti-cheat: XP delta capping (matches V2 limits)
        const MAX_V1_XP_PER_SYNC = 50000;
        const MAX_V1_LEVEL = 999;
        const oldXp = existing?.xp || 0;
        const oldLevel = existing?.level || 0;
        let finalXp = Math.max(xp, oldXp);
        // Cap XP delta
        if (finalXp - oldXp > MAX_V1_XP_PER_SYNC) {
            console.log(`[Anti-cheat V1] XP clamped: tried +${finalXp - oldXp}, allowed +${MAX_V1_XP_PER_SYNC}`);
            finalXp = oldXp + MAX_V1_XP_PER_SYNC;
        }
        // Recalculate level from XP (trust XP, not client-reported level)
        let finalLevel = Math.max(level, oldLevel);
        if (finalLevel > MAX_V1_LEVEL) finalLevel = MAX_V1_LEVEL;
        if (typeof getCumulativeXPForLevel === 'function' && finalLevel > 1) {
            let recalcLevel = 1;
            let cumulativeCheck = 0;
            while (recalcLevel < MAX_V1_LEVEL) {
                cumulativeCheck += getXPForLevel(recalcLevel);
                if (finalXp < cumulativeCheck) break;
                recalcLevel++;
            }
            if (recalcLevel < finalLevel) {
                console.log(`[Anti-cheat V1] Level/XP mismatch: claimed level=${finalLevel}, xp=${finalXp}, recalculated=${recalcLevel}`);
                finalLevel = recalcLevel;
            }
        }

        console.log(`Sync for user ${userId}: incoming level=${level}, existing level=${existing?.level || 0}, final level=${finalLevel}`);

        // Stats keys controlled by force_streak_override
        const STREAK_STAT_KEYS_LEGACY = new Set([
            'daily_quest_streak', 'last_daily_quest_date', 'quest_completion_dates',
            'total_daily_quests_completed', 'total_weekly_quests_completed', 'total_xp_from_quests'
        ]);
        const hasForceStreakOverrideLegacy = existing?.force_streak_override === true;

        // Anti-cheat: V1 stat delta caps (matches V2 limits)
        const V1_STATS_MAX_PER_SYNC = {
            total_flashes: 700,
            total_bubbles_popped: 600,
            total_video_minutes: 70,
            total_lock_cards_completed: 30,
            completed_sessions: 5,
            longest_session_minutes: 480
        };
        const V1_ALLOWED_STRING_STATS = new Set(['last_daily_quest_date']);
        const V1_ALLOWED_ARRAY_STATS = new Set(['quest_completion_dates']);
        const V1_MAX_STAT_KEYS = 50;

        // Merge stats (take max of each stat, skip streak stats if force_streak_override is active)
        let mergedStats = stats || {};
        if (existing?.stats) {
            mergedStats = { ...existing.stats };
            let newKeyCount = 0;
            for (const [statKey, statValue] of Object.entries(stats || {})) {
                // Prevent prototype pollution
                if (statKey === '__proto__' || statKey === 'constructor' || statKey === 'prototype' || statKey === 'toString' || statKey === 'valueOf' || statKey === 'hasOwnProperty' || statKey === 'isPrototypeOf') continue;
                // Skip streak stats when admin has force-set them
                if (hasForceStreakOverrideLegacy && STREAK_STAT_KEYS_LEGACY.has(statKey)) {
                    continue;
                }
                // Limit total stat keys
                if (!(statKey in mergedStats)) {
                    newKeyCount++;
                    if (Object.keys(mergedStats).length + newKeyCount > V1_MAX_STAT_KEYS) continue;
                }
                // Handle allowed string stats
                if (V1_ALLOWED_STRING_STATS.has(statKey)) {
                    if (typeof statValue === 'string' && statValue.length <= 20) {
                        const existing = typeof mergedStats[statKey] === 'string' ? mergedStats[statKey] : '';
                        if (statValue >= existing) mergedStats[statKey] = statValue;
                    }
                    continue;
                }
                // Handle allowed array stats (cap at 100 entries)
                if (V1_ALLOWED_ARRAY_STATS.has(statKey)) {
                    const clientDates = Array.isArray(statValue) ? statValue.filter(d => typeof d === 'string' && d.length <= 20) : [];
                    const serverDates = Array.isArray(mergedStats[statKey]) ? mergedStats[statKey] : [];
                    const merged = clientDates.length >= serverDates.length ? clientDates : serverDates;
                    mergedStats[statKey] = merged.slice(-100);
                    continue;
                }
                // Only accept numeric values for all other stats
                if (typeof statValue !== 'number' || !isFinite(statValue)) continue;
                const existingVal = Number(mergedStats[statKey]) || 0;
                const statDelta = statValue - existingVal;
                if (statDelta <= 0) continue; // Don't decrease
                // Cap known stat deltas
                if (Object.prototype.hasOwnProperty.call(V1_STATS_MAX_PER_SYNC, statKey)) {
                    if (statDelta > V1_STATS_MAX_PER_SYNC[statKey]) {
                        mergedStats[statKey] = Math.round(existingVal + V1_STATS_MAX_PER_SYNC[statKey]);
                        console.log(`[Anti-cheat V1] Stat ${statKey} clamped: tried +${statDelta}, allowed +${V1_STATS_MAX_PER_SYNC[statKey]}`);
                    } else {
                        mergedStats[statKey] = statValue;
                    }
                } else {
                    // Unknown keys: accept with Math.max but cap delta (R8/M27)
                    const unknownDelta = statValue - existingVal;
                    if (unknownDelta > 10000) {
                        mergedStats[statKey] = existingVal + 10000;
                    } else {
                        mergedStats[statKey] = Math.max(existingVal, statValue);
                    }
                }
            }
        }

        const profile = {
            xp: finalXp,
            level: finalLevel,
            achievements: mergedAchievements,
            stats: mergedStats,
            last_session: last_session || new Date().toISOString(),
            updated_at: new Date().toISOString(),
            // Preserve display_name if already set — never auto-populate from patron_name (privacy)
            display_name: existing?.display_name || null,
            display_name_set_at: existing?.display_name_set_at || null,
            // Discord DM opt-in (R8/M19: strict boolean type check)
            allow_discord_dm: allow_discord_dm === true || allow_discord_dm === false ? allow_discord_dm : (existing?.allow_discord_dm === true),
            // Profile picture sharing opt-in
            share_profile_picture: share_profile_picture === true || share_profile_picture === false ? share_profile_picture : (existing?.share_profile_picture === true),
            // Online status visibility opt-in (default true - visible)
            show_online_status: show_online_status === true || show_online_status === false ? show_online_status : (existing?.show_online_status !== false),
            // Store Discord ID for DM feature (validate numeric, update if provided, otherwise preserve existing)
            discord_id: (discord_id && /^\d{1,20}$/.test(String(discord_id))) ? String(discord_id) : (existing?.discord_id || null),
            // Store Discord avatar URL (validate HTTPS, update if provided, otherwise preserve existing)
            avatar_url: (avatar_url && typeof avatar_url === 'string' && avatar_url.startsWith('https://') && avatar_url.length <= 500) ? avatar_url : (existing?.avatar_url || null),
            // Update Patreon status fields live (checked during sync)
            patreon_tier: effectiveTier,
            patreon_is_active: hasActivePledge,
            patreon_is_whitelisted: isWhitelistedUser,
            patreon_status_updated_at: new Date().toISOString(),
            email_hash: hashEmail(tierInfo.patron_email) || existing?.email_hash,
            // Capture pending reset flags before clearing
            reset_weekly_quest: (reset_weekly_quest === false) ? false : (existing?.reset_weekly_quest || false),
            reset_daily_quest: (reset_daily_quest === false) ? false : (existing?.reset_daily_quest || false),
            // Handle force_streak_override: clear when client sends false, otherwise preserve
            force_streak_override: (clientForceStreakOverride === false) ? undefined : (existing?.force_streak_override || false)
        };

        // Store profile (no expiry - permanent storage)
        await redis.set(key, JSON.stringify(profile));

        console.log(`Synced profile for user ${userId}: Level ${finalLevel}, ${finalXp} XP, ${mergedAchievements.length} achievements`);

        res.json({
            success: true,
            user_id: userId,
            profile: filterProfileForResponse(profile),
            merged: !!existing
        });
    } catch (error) {
        console.error('Profile sync error:', error.message);
        res.status(500).json({ error: 'Failed to sync profile' });
    }
});

/**
 * POST /user/heartbeat
 * Lightweight endpoint to keep user showing as online.
 * Only updates the updated_at timestamp, doesn't sync profile data.
 * Requires: Authorization: Bearer <patreon_access_token>
 */
app.post('/user/heartbeat', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        // Per-IP cooldown to prevent Patreon API amplification
        // R12/M6: Atomic NX cooldown to prevent TOCTOU race
        const hbIp = req.ip || 'unknown';
        if (redis) {
            const v1HbCooldownKey = `v1_heartbeat_cooldown:${hbIp}`;
            const cooldownSet = await redis.set(v1HbCooldownKey, '1', { nx: true, ex: 60 });
            if (!cooldownSet) {
                return res.status(429).json({ error: 'V1 heartbeat cooldown active', retry_after: 60 });
            }
        }

        const accessToken = authHeader.substring(7);
        const identity = await getPatreonIdentity(accessToken);
        const tierInfo = determineTier(identity);

        const userId = tierInfo.patreon_user_id;
        if (!userId) {
            return res.status(401).json({ error: 'Could not identify user' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Profile storage not available' });
        }

        const key = `${PROFILE_KEY_PREFIX}${userId}`;
        const existingProfile = await redis.get(key);

        if (existingProfile) {
            const profile = typeof existingProfile === 'string' ? JSON.parse(existingProfile) : existingProfile;
            // Only update the timestamp
            profile.updated_at = new Date().toISOString();
            await redis.set(key, JSON.stringify(profile));
        } else {
            // Create a basic profile if none exists
            // Note: display_name is NOT auto-set - user must choose their own name to appear on leaderboard
            const newProfile = {
                patreon_user_id: userId,
                xp: 0,
                level: 1,
                achievements: [],
                stats: {},
                created_at: new Date().toISOString(),
                updated_at: new Date().toISOString()
            };
            await redis.set(key, JSON.stringify(newProfile));
            console.log(`Heartbeat created new profile for user ${userId}`);
        }

        // Also update unified profile if one exists (for migrated users on older clients)
        try {
            const unifiedId = await redis.get(`${PATREON_USER_INDEX}${userId}`);
            if (unifiedId) {
                const unifiedData = await redis.get(`${UNIFIED_USER_PREFIX}${unifiedId}`);
                if (unifiedData) {
                    const unifiedUser = typeof unifiedData === 'string' ? JSON.parse(unifiedData) : unifiedData;
                    unifiedUser.last_seen = new Date().toISOString();
                    unifiedUser.updated_at = unifiedUser.last_seen;
                    unifiedUser.last_heartbeat = {
                        at: unifiedUser.last_seen,
                        is_active: true,
                        in_session: null,
                        app_version: null
                    };
                    await redis.set(`${UNIFIED_USER_PREFIX}${unifiedId}`, JSON.stringify(unifiedUser));
                    // Track in online_users sorted set for accurate online count
                    await trackUserOnline(unifiedId, unifiedUser.show_online_status);
                }
            }
        } catch (e) {
            // Non-critical — don't fail the heartbeat
            console.error('Legacy heartbeat unified cross-update error:', e.message);
        }

        res.json({ success: true });
    } catch (error) {
        console.error('Heartbeat error:', error.message);
        res.status(500).json({ error: 'Heartbeat failed' });
    }
});

/**
 * POST /user/set-display-name
 * Set custom display name (ONE TIME ONLY - cannot be changed after set)
 * Requires: Authorization: Bearer <patreon_access_token>
 * Body: { display_name: string }
 */
app.post('/user/set-display-name', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        // Per-IP cooldown to prevent Patreon API amplification
        // R12/M6: Atomic NX cooldown to prevent TOCTOU race
        const setNameIp = req.ip || 'unknown';
        if (redis) {
            const v1SetNameCooldownKey = `v1_setname_cooldown:${setNameIp}`;
            const cooldownSet = await redis.set(v1SetNameCooldownKey, '1', { nx: true, ex: 30 });
            if (!cooldownSet) {
                return res.status(429).json({ error: 'V1 set-display-name cooldown active', retry_after: 30 });
            }
        }

        const accessToken = authHeader.substring(7);
        const identity = await getPatreonIdentity(accessToken);
        const tierInfo = determineTier(identity);

        const userId = tierInfo.patreon_user_id;
        if (!userId) {
            return res.status(401).json({ error: 'Could not identify user' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Profile storage not available' });
        }

        const { display_name } = req.body;

        // Validate display name
        if (!display_name || typeof display_name !== 'string') {
            return res.status(400).json({ error: 'display_name is required' });
        }

        const trimmedName = display_name.trim();
        if (trimmedName.length < 3 || trimmedName.length > 20) {
            return res.status(400).json({ error: 'Display name must be 3-20 characters' });
        }

        // Strict character validation (prevent Redis key injection, log injection)
        if (!isValidDisplayNameChars(trimmedName)) {
            return res.status(400).json({ error: 'Display name contains invalid characters' });
        }

        // Load existing profile
        const key = `${PROFILE_KEY_PREFIX}${userId}`;
        const existingProfile = await redis.get(key);
        let profile = existingProfile
            ? (typeof existingProfile === 'string' ? JSON.parse(existingProfile) : existingProfile)
            : { xp: 0, level: 1, achievements: [], stats: {} };

        // Check if display name already set (ONE TIME ONLY)
        if (profile.display_name) {
            return res.status(400).json({
                error: 'Display name already set and cannot be changed',
                current_name: profile.display_name
            });
        }

        // Check if this display name is already taken by someone else (auto-cleans orphans)
        const existingOwner = await isDisplayNameTaken(trimmedName);
        if (existingOwner && existingOwner !== userId) {
            return res.status(400).json({
                error: 'This display name is already taken. Please choose another.'
            });
        }

        // Set the display name
        const indexKey = `display_name_index:${trimmedName.toLowerCase()}`;
        profile.display_name = trimmedName;
        profile.display_name_set_at = new Date().toISOString();
        profile.updated_at = new Date().toISOString();

        // Save profile and create index entry
        await redis.set(key, JSON.stringify(profile));
        await redis.set(indexKey, userId);

        console.log(`Display name set for user ${userId}`);

        res.json({
            success: true,
            display_name: trimmedName,
            is_whitelisted: profile.patreon_is_whitelisted || false,
            profile: filterProfileForResponse(profile)
        });
    } catch (error) {
        console.error('Set display name error:', error.message);
        res.status(500).json({ error: 'Failed to set display name' });
    }
});

/**
 * Check if a display name is available (not already taken)
 * GET /user/check-display-name?name=SomeName
 * Requires: Authorization: Bearer <patreon_access_token>
 */
app.get('/user/check-display-name', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        // Per-IP cooldown to prevent Patreon API amplification
        // R12/M6: Atomic NX cooldown to prevent TOCTOU race
        const checkNameIp = req.ip || 'unknown';
        if (redis) {
            const v1CheckNameCooldownKey = `v1_checkname_cooldown:${checkNameIp}`;
            const cooldownSet = await redis.set(v1CheckNameCooldownKey, '1', { nx: true, ex: 5 });
            if (!cooldownSet) {
                return res.status(429).json({ error: 'V1 check-display-name cooldown active', retry_after: 5 });
            }
        }

        // Validate token (ensures user is authenticated)
        const accessToken = authHeader.substring(7);
        await getPatreonIdentity(accessToken);

        if (!redis) {
            // Can't check without Redis, allow optimistically
            return res.json({ available: true });
        }

        const { name } = req.query;
        if (!name || typeof name !== 'string') {
            return res.status(400).json({ available: false, error: 'Name parameter is required' });
        }

        const trimmedName = name.trim().toLowerCase();
        if (trimmedName.length < 3 || trimmedName.length > 20) {
            return res.status(400).json({ available: false, error: 'Display name must be 3-20 characters' });
        }

        // Check the display name index (auto-cleans orphans)
        const owner = await isDisplayNameTaken(trimmedName);

        if (owner) {
            return res.json({ available: false, error: 'This name is already taken' });
        }

        res.json({ available: true });
    } catch (error) {
        console.error('Check display name error:', error.message);
        // On error, allow optimistically
        res.json({ available: true });
    }
});

/**
 * GET /v2/auth/check-name?name=SomeName
 * Lightweight name availability check (no OAuth required).
 * Used by invite code registration flow.
 * Rate limited by global IP limiter.
 */
app.get('/v2/auth/check-name', async (req, res) => {
    try {
        if (!redis) {
            return res.json({ available: true });
        }

        // R8/M7: Per-IP rate limit (10 checks per minute)
        const ip = req.ip || 'unknown';
        const checkNameKey = `check_name_limit:${ip}`;
        try {
            await redis.set(checkNameKey, 0, { nx: true, ex: 60 });
            const checkCount = await redis.incr(checkNameKey);
            if (checkCount > 10) {
                return res.status(429).json({ available: false });
            }
        } catch (e) { /* non-critical */ }

        const { name } = req.query;
        if (!name || typeof name !== 'string') {
            return res.status(400).json({ available: false });
        }

        const trimmedName = name.trim();
        if (trimmedName.length < 3 || trimmedName.length > 20) {
            return res.status(400).json({ available: false });
        }

        if (!isValidDisplayNameChars(trimmedName)) {
            return res.status(400).json({ available: false });
        }

        const owner = await isDisplayNameTaken(trimmedName);
        res.json({ available: !owner });
    } catch (error) {
        console.error('Check name error:', error.message);
        res.json({ available: true });
    }
});

// =============================================================================
// DISCORD USER DISPLAY NAME ENDPOINTS
// =============================================================================

const DISCORD_PROFILE_KEY_PREFIX = 'discord_profile:';

/**
 * POST /user/set-display-name-discord
 * Set custom display name for Discord users (ONE TIME ONLY - cannot be changed after set)
 * Requires: Authorization: Bearer <discord_access_token>
 * Body: { display_name: string }
 */
app.post('/user/set-display-name-discord', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        const accessToken = authHeader.substring(7);
        const user = await getDiscordUser(accessToken);
        const userId = `discord_${user.id}`;

        if (!redis) {
            return res.status(503).json({ error: 'Profile storage not available' });
        }

        const { display_name, claim_existing } = req.body;

        // Validate display name
        if (!display_name || typeof display_name !== 'string') {
            return res.status(400).json({ error: 'display_name is required' });
        }

        const trimmedName = display_name.trim();
        if (trimmedName.length < 3 || trimmedName.length > 20) {
            return res.status(400).json({ error: 'Display name must be 3-20 characters' });
        }

        // Strict character validation (prevent Redis key injection, log injection)
        if (!isValidDisplayNameChars(trimmedName)) {
            return res.status(400).json({ error: 'Display name contains invalid characters' });
        }

        // Load existing profile
        const key = `${DISCORD_PROFILE_KEY_PREFIX}${user.id}`;
        const existingProfile = await redis.get(key);
        let profile = existingProfile
            ? (typeof existingProfile === 'string' ? JSON.parse(existingProfile) : existingProfile)
            : { discord_id: user.id };

        // Check if display name already set (ONE TIME ONLY)
        if (profile.display_name) {
            return res.status(400).json({
                error: 'Display name already set and cannot be changed',
                current_name: profile.display_name
            });
        }

        // Check if this display name is already taken by someone else (shared namespace with Patreon)
        const indexKey = `display_name_index:${trimmedName.toLowerCase()}`;
        const existingUserId = await isDisplayNameTaken(trimmedName);

        if (existingUserId && existingUserId !== userId) {
            // Name is taken — no unverified claiming allowed
            return res.status(400).json({
                error: 'This display name is already taken. Please choose another name.'
            });
        }

        // Set the display name
        profile.display_name = trimmedName;
        profile.display_name_set_at = new Date().toISOString();
        profile.updated_at = new Date().toISOString();

        // Save profile and create index entry
        await redis.set(key, JSON.stringify(profile));
        await redis.set(indexKey, userId);

        console.log(`Display name set for Discord user ${user.id}`);

        res.json({
            success: true,
            display_name: trimmedName,
            is_whitelisted: profile.patreon_is_whitelisted || false
        });
    } catch (error) {
        console.error('Set Discord display name error:', error.message);
        res.status(500).json({ error: 'Failed to set display name' });
    }
});

/**
 * Check if a display name is available (not already taken)
 * GET /user/check-display-name-discord?name=SomeName
 * Requires: Authorization: Bearer <discord_access_token>
 */
app.get('/user/check-display-name-discord', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        // Validate token (ensures user is authenticated)
        const accessToken = authHeader.substring(7);
        await getDiscordUser(accessToken);

        if (!redis) {
            // Can't check without Redis, allow optimistically
            return res.json({ available: true });
        }

        const { name } = req.query;
        if (!name || typeof name !== 'string') {
            return res.status(400).json({ available: false, error: 'Name parameter is required' });
        }

        const trimmedName = name.trim().toLowerCase();
        if (trimmedName.length < 3 || trimmedName.length > 20) {
            return res.status(400).json({ available: false, error: 'Display name must be 3-20 characters' });
        }

        // Check the display name index (shared with Patreon users, auto-cleans orphans)
        const owner = await isDisplayNameTaken(trimmedName);

        if (owner) {
            return res.json({ available: false, error: 'This name is already taken' });
        }

        res.json({ available: true });
    } catch (error) {
        console.error('Check Discord display name error:', error.message);
        // On error, allow optimistically
        res.json({ available: true });
    }
});

/**
 * GET /user/profile-discord
 * Get Discord user's full profile (progression, achievements, stats)
 * Auto-links with Patreon account if same email is found
 * Requires: Authorization: Bearer <discord_access_token>
 */
app.get('/user/profile-discord', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        const accessToken = authHeader.substring(7);
        const user = await getDiscordUser(accessToken);

        if (!redis) {
            return res.json({ exists: false, user_id: user.id });
        }

        // FIRST: Check for unified user via discord_user index
        const unifiedUserId = await redis.get(`discord_user:${user.id}`);
        if (unifiedUserId) {
            const unifiedUserKey = `user:${unifiedUserId}`;
            const unifiedUserData = await redis.get(unifiedUserKey);
            if (unifiedUserData) {
                const unifiedUser = typeof unifiedUserData === 'string' ? JSON.parse(unifiedUserData) : unifiedUserData;
                console.log(`Found unified user for Discord ${user.id}: ${unifiedUserId} (Level ${unifiedUser.level})`);
                return res.json({
                    exists: true,
                    user_id: user.id,
                    unified_user_id: unifiedUserId,
                    profile: {
                        display_name: unifiedUser.display_name,
                        xp: unifiedUser.xp || 0,
                        level: unifiedUser.level || 1,
                        achievements: unifiedUser.achievements || [],
                        stats: unifiedUser.stats || {},
                        updated_at: unifiedUser.updated_at,
                        skill_points: unifiedUser.skill_points || 0,
                        unlocked_skills: unifiedUser.unlocked_skills || []
                    }
                });
            }
        }

        const discordProfileKey = `${DISCORD_PROFILE_KEY_PREFIX}${user.id}`;
        const existingProfile = await redis.get(discordProfileKey);

        // If Discord profile exists and has display name, return it
        if (existingProfile) {
            const profile = typeof existingProfile === 'string' ? JSON.parse(existingProfile) : existingProfile;
            if (profile.display_name) {
                return res.json({
                    exists: true,
                    user_id: user.id,
                    profile: {
                        display_name: profile.display_name,
                        xp: profile.xp || 0,
                        level: profile.level || 1,
                        achievements: profile.achievements || [],
                        stats: profile.stats || {},
                        updated_at: profile.updated_at
                    }
                });
            }
        }

        // Email-based auto-link HINT (no longer writes to Redis — client must confirm)
        if (user.email && user.verified) {
            const emailIndexKey = `email_index:${hashEmail(user.email)}`;
            const patreonUserId = await redis.get(emailIndexKey);

            if (patreonUserId) {
                const patreonProfileKey = `${PROFILE_KEY_PREFIX}${patreonUserId}`;
                const patreonProfileData = await redis.get(patreonProfileKey);

                if (patreonProfileData) {
                    const patreonProfile = typeof patreonProfileData === 'string'
                        ? JSON.parse(patreonProfileData)
                        : patreonProfileData;

                    if (patreonProfile.display_name) {
                        // Return hint only — do NOT auto-link or write to Redis
                        console.log(`[V1] Email match found for Discord ${user.id} → Patreon ${patreonUserId} (hint only)`);

                        const parsedProfile = existingProfile
                            ? (typeof existingProfile === 'string' ? JSON.parse(existingProfile) : existingProfile)
                            : null;
                        return res.json({
                            exists: true,
                            user_id: user.id,
                            can_auto_link: true,
                            auto_link_unified_id: patreonUserId,
                            auto_link_display_name: patreonProfile.display_name,
                            profile: parsedProfile ? {
                                display_name: parsedProfile.display_name || null,
                                xp: parsedProfile.xp || 0,
                                level: parsedProfile.level || 1,
                                achievements: parsedProfile.achievements || [],
                                stats: parsedProfile.stats || {},
                                updated_at: parsedProfile.updated_at
                            } : null
                        });
                    }
                }
            }
        }

        // No auto-link possible - return profile without display name (client will prompt)
        if (existingProfile) {
            const profile = typeof existingProfile === 'string' ? JSON.parse(existingProfile) : existingProfile;
            return res.json({
                exists: true,
                user_id: user.id,
                profile: {
                    display_name: null,
                    xp: profile.xp || 0,
                    level: profile.level || 1,
                    achievements: profile.achievements || [],
                    stats: profile.stats || {},
                    updated_at: profile.updated_at
                }
            });
        }

        return res.json({
            exists: false,
            user_id: user.id
        });
    } catch (error) {
        console.error('Get Discord profile error:', error.message);
        res.status(500).json({ error: 'Failed to load profile' });
    }
});

/**
 * POST /user/sync-discord
 * Sync Discord user's progression to cloud
 * Requires: Authorization: Bearer <discord_access_token>
 * Body: { xp, level, achievements[], stats{} }
 */
app.post('/user/sync-discord', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        const accessToken = authHeader.substring(7);
        const user = await getDiscordUser(accessToken);

        if (!redis) {
            return res.status(503).json({ error: 'Profile storage not available' });
        }

        const { xp, level, achievements, stats, allow_discord_dm, share_profile_picture, show_online_status, avatar_url } = req.body;

        // Validate input
        if (typeof level !== 'number' || !isFinite(level) || level < 1) {
            return res.status(400).json({ error: 'Invalid level' });
        }
        if (typeof xp === 'number' && !isFinite(xp)) {
            return res.status(400).json({ error: 'Invalid xp value' });
        }

        const key = `${DISCORD_PROFILE_KEY_PREFIX}${user.id}`;
        const existingProfile = await redis.get(key);
        let profile = existingProfile
            ? (typeof existingProfile === 'string' ? JSON.parse(existingProfile) : existingProfile)
            : { discord_id: user.id };

        // Ensure display_name is never null/empty - use anonymous fallback
        // This prevents "" accounts from being created
        if (!profile.display_name || profile.display_name.trim() === '') {
            profile.display_name = `User_${user.id.slice(-6)}`;
            console.log(`Auto-assigned display_name for Discord user ${user.id}`);
        }

        // Update Discord DM opt-in if provided
        if (typeof allow_discord_dm === 'boolean') {
            profile.allow_discord_dm = allow_discord_dm;
        }

        // Update profile picture sharing opt-in if provided
        if (typeof share_profile_picture === 'boolean') {
            profile.share_profile_picture = share_profile_picture;
        }

        // Update online status visibility opt-in if provided
        if (typeof show_online_status === 'boolean') {
            profile.show_online_status = show_online_status;
        }

        // Update avatar URL if provided (from client's Discord auth, must be HTTPS)
        if (avatar_url && typeof avatar_url === 'string' && avatar_url.startsWith('https://') && avatar_url.length <= 500) {
            profile.avatar_url = avatar_url;
        }

        // Anti-cheat: XP delta capping (matches V2 limits)
        const MAX_V1D_XP_PER_SYNC = 50000;
        const MAX_V1D_LEVEL = 999;
        const newXp = typeof xp === 'number' ? xp : 0;
        const oldXpD = profile.xp || 0;
        const oldLevelD = profile.level || 1;

        let finalXpD = Math.max(newXp, oldXpD);
        if (finalXpD - oldXpD > MAX_V1D_XP_PER_SYNC) {
            console.log(`[Anti-cheat V1D] XP clamped: tried +${finalXpD - oldXpD}, allowed +${MAX_V1D_XP_PER_SYNC}`);
            finalXpD = oldXpD + MAX_V1D_XP_PER_SYNC;
        }

        let finalLevelD = Math.max(level, oldLevelD);
        if (finalLevelD > MAX_V1D_LEVEL) finalLevelD = MAX_V1D_LEVEL;
        // Recalculate level from XP if possible
        if (typeof getCumulativeXPForLevel === 'function' && finalLevelD > 1) {
            let recalcLevel = 1;
            let cumulativeCheck = 0;
            while (recalcLevel < MAX_V1D_LEVEL) {
                cumulativeCheck += getXPForLevel(recalcLevel);
                if (finalXpD < cumulativeCheck) break;
                recalcLevel++;
            }
            if (recalcLevel < finalLevelD) {
                console.log(`[Anti-cheat V1D] Level/XP mismatch: claimed=${finalLevelD}, recalculated=${recalcLevel}`);
                finalLevelD = recalcLevel;
            }
        }
        profile.xp = finalXpD;
        profile.level = finalLevelD;

        // Merge achievements (union - never remove, capped at 500)
        if (Array.isArray(achievements)) {
            const existingAchievements = new Set(profile.achievements || []);
            const filtered = achievements.filter(a => typeof a === 'string' && a.length <= 100);
            filtered.forEach(a => existingAchievements.add(a));
            profile.achievements = Array.from(existingAchievements).slice(0, 500);
        }

        // Merge stats (take higher values, anti-cheat caps)
        const V1D_STATS_MAX = {
            total_flashes: 700, total_bubbles_popped: 600, total_video_minutes: 70,
            total_lock_cards_completed: 30, completed_sessions: 5, longest_session_minutes: 480
        };
        const V1D_MAX_STAT_KEYS = 50;
        if (stats && typeof stats === 'object') {
            profile.stats = profile.stats || {};
            let newStatKeys = 0;
            for (const [key, value] of Object.entries(stats)) {
                if (key === '__proto__' || key === 'constructor' || key === 'prototype') continue;
                if (typeof value !== 'number' || !isFinite(value)) continue;
                // Limit total stat keys
                if (!(key in profile.stats)) {
                    newStatKeys++;
                    if (Object.keys(profile.stats).length + newStatKeys > V1D_MAX_STAT_KEYS) continue;
                }
                const existingVal = Number(profile.stats[key]) || 0;
                const delta = value - existingVal;
                if (delta <= 0) continue;
                if (Object.prototype.hasOwnProperty.call(V1D_STATS_MAX, key)) {
                    if (delta > V1D_STATS_MAX[key]) {
                        profile.stats[key] = Math.round(existingVal + V1D_STATS_MAX[key]);
                        console.log(`[Anti-cheat V1D] Stat ${key} clamped: tried +${delta}, allowed +${V1D_STATS_MAX[key]}`);
                    } else {
                        profile.stats[key] = value;
                    }
                } else {
                    profile.stats[key] = Math.max(existingVal, value);
                }
            }
        }

        profile.updated_at = new Date().toISOString();
        profile.last_sync = new Date().toISOString();

        await redis.set(key, JSON.stringify(profile));

        console.log(`Discord profile synced: user=${user.id}, level=${profile.level}, xp=${profile.xp}`);

        res.json({
            success: true,
            user_id: user.id,
            profile: {
                xp: profile.xp,
                level: profile.level,
                achievements: profile.achievements,
                stats: profile.stats
            }
        });
    } catch (error) {
        console.error('Discord profile sync error:', error.message);
        res.status(500).json({ error: 'Failed to sync profile' });
    }
});

/**
 * POST /user/heartbeat-discord
 * Keep Discord user's session alive
 * Requires: Authorization: Bearer <discord_access_token>
 */
app.post('/user/heartbeat-discord', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization header required' });
        }

        const accessToken = authHeader.substring(7);
        const user = await getDiscordUser(accessToken);

        if (redis) {
            const now = new Date().toISOString();
            const key = `${DISCORD_PROFILE_KEY_PREFIX}${user.id}`;
            const existingProfile = await redis.get(key);
            if (existingProfile) {
                const profile = typeof existingProfile === 'string' ? JSON.parse(existingProfile) : existingProfile;
                profile.last_seen = now;
                profile.updated_at = now;
                // Update avatar if changed
                if (user.avatar && profile.avatar !== user.avatar) {
                    profile.avatar = user.avatar;
                }
                await redis.set(key, JSON.stringify(profile));
            } else {
                // Create a basic profile if none exists
                const newProfile = {
                    discord_user_id: user.id,
                    // discord_username intentionally not stored — PII
                    avatar: user.avatar || null,
                    xp: 0,
                    level: 1,
                    achievements: [],
                    stats: {},
                    created_at: now,
                    updated_at: now,
                    last_seen: now
                };
                await redis.set(key, JSON.stringify(newProfile));
                console.log(`Discord heartbeat created new profile for discord_id:${user.id}`);
            }

            // Also update unified user's last_seen if this Discord user is linked to one
            const unifiedId = await redis.get(`${DISCORD_USER_INDEX}${user.id}`);
            if (unifiedId) {
                const unifiedUserKey = `${UNIFIED_USER_PREFIX}${unifiedId}`;
                const unifiedUserData = await redis.get(unifiedUserKey);
                if (unifiedUserData) {
                    const unifiedUser = typeof unifiedUserData === 'string' ? JSON.parse(unifiedUserData) : unifiedUserData;
                    unifiedUser.last_seen = now;
                    unifiedUser.updated_at = now;
                    await redis.set(unifiedUserKey, JSON.stringify(unifiedUser));
                    // Track in online_users sorted set for accurate online count
                    await trackUserOnline(unifiedId, unifiedUser.show_online_status);
                }
            }
        }

        res.json({ success: true, user_id: user.id });
    } catch (error) {
        console.error('Discord heartbeat error:', error.message);
        res.status(500).json({ error: 'Heartbeat failed' });
    }
});

// =============================================================================
// LEADERBOARD
// =============================================================================

// Leaderboard cache - stores deduplicated profiles to avoid expensive Redis scans
// Cache is refreshed every 2 minutes, reducing reads by ~99%
let leaderboardProfilesCache = null;
let leaderboardCacheTime = 0;
const LEADERBOARD_CACHE_TTL = 2 * 60 * 1000; // 2 minutes

/**
 * Helper function to fetch and deduplicate all profiles from Redis
 * This is the expensive operation we want to cache
 */
async function fetchAllProfiles() {
    const profiles = [];

    // Helper to process profile keys
    const processProfiles = async (pattern) => {
        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: pattern, count: 100 });
            cursor = String(result[0]);
            const keys = result[1] || [];

            for (const key of keys) {
                try {
                    const data = await redis.get(key);
                    if (data) {
                        const profile = typeof data === 'string' ? JSON.parse(data) : data;
                        // Privacy: if display_name matches patron_name, treat as unset
                        let profileDisplayName = profile.display_name || null;
                        if (profileDisplayName && profile.patron_name &&
                            profileDisplayName.toLowerCase().trim() === profile.patron_name.toLowerCase().trim()) {
                            profileDisplayName = null;
                        }
                        // Only include users with display_name set
                        if (profileDisplayName) {
                            // Check if user is online (updated within last minute)
                            const lastSeen = profile.last_seen || profile.updated_at;
                            const lastSeenDate = lastSeen ? new Date(lastSeen) : null;
                            const actuallyOnline = lastSeenDate && (Date.now() - lastSeenDate.getTime()) < 180000; // 3 min (heartbeat is 2 min)
                            // Respect show_online_status setting (default true if not set)
                            const isOnline = actuallyOnline && profile.show_online_status !== false;

                            // Check if user is an active Patreon supporter:
                            // - Has active subscription flag (set during validation)
                            // - OR is whitelisted
                            // - OR has a paid tier > 0
                            const isPatreon = profile.patreon_is_active ||
                                              profile.patreon_is_whitelisted ||
                                              (profile.patreon_tier && profile.patreon_tier > 0);

                            // Get Discord ID if user has opted in for DMs
                            // For Discord profiles, the ID is in the key (discord_profile:ID) or profile.discord_id
                            let discordId = null;
                            if (profile.allow_discord_dm) {
                                if (profile.discord_id) {
                                    discordId = profile.discord_id;
                                } else if (key.startsWith('discord_profile:')) {
                                    // Extract ID from key for Discord profiles
                                    discordId = key.replace('discord_profile:', '');
                                }
                            }

                            profiles.push({
                                display_name: profileDisplayName,
                                level: profile.level || 1,
                                xp: profile.xp || 0,
                                total_bubbles_popped: profile.stats?.total_bubbles_popped || 0,
                                total_flashes: profile.stats?.total_flashes || 0,
                                total_video_minutes: Math.round((profile.stats?.total_video_minutes || 0) * 10) / 10,
                                total_lock_cards_completed: profile.stats?.total_lock_cards_completed || 0,
                                achievements_count: profile.achievements?.length || 0,
                                is_online: isOnline,
                                is_patreon: !!isPatreon,
                                patreon_tier: profile.patreon_tier || 0,
                                discord_id: discordId,
                                is_season0_og: profile.is_season0_og || false,
                                last_seen: profile.last_seen || profile.updated_at, // Store for online recalc
                                show_online_status: profile.show_online_status !== false // Store for online recalc
                            });
                        }
                    }
                } catch (parseError) {
                    console.error(`Error parsing profile ${key}:`, parseError.message);
                }
            }
        } while (cursor !== "0");
    };

    // Scan all profile types: Patreon, Discord, and unified users
    await processProfiles('profile:*');
    await processProfiles('discord_profile:*');
    await processProfiles('user:*');

    // Deduplicate by display_name - keep the entry with highest level (then XP as tiebreaker)
    // Also merge is_online and discord_id from duplicates
    const deduped = [];
    const seenNames = new Map(); // display_name (lowercase) -> index in deduped array
    for (const profile of profiles) {
        const nameKey = profile.display_name.toLowerCase();
        if (seenNames.has(nameKey)) {
            // Compare with existing entry - keep the one with higher level/XP
            const existingIdx = seenNames.get(nameKey);
            const existing = deduped[existingIdx];
            if (profile.level > existing.level ||
                (profile.level === existing.level && profile.xp > existing.xp)) {
                // Replace with better entry, but merge fields from existing
                if (!profile.discord_id && existing.discord_id) {
                    profile.discord_id = existing.discord_id;
                }
                // If existing was online, preserve that (Discord heartbeat may have updated different profile)
                if (existing.is_online && !profile.is_online) {
                    profile.is_online = true;
                }
                // Merge OG status from either entry
                if (existing.is_season0_og && !profile.is_season0_og) {
                    profile.is_season0_og = true;
                }
                deduped[existingIdx] = profile;
            } else {
                // Keep existing but merge fields from duplicate
                if (!existing.discord_id && profile.discord_id) {
                    existing.discord_id = profile.discord_id;
                }
                // If duplicate is online, the user IS online (Discord heartbeat updated that profile)
                if (profile.is_online && !existing.is_online) {
                    existing.is_online = true;
                }
                // Merge OG status from either entry
                if (profile.is_season0_og && !existing.is_season0_og) {
                    existing.is_season0_og = true;
                }
            }
        } else {
            seenNames.set(nameKey, deduped.length);
            deduped.push(profile);
        }
    }

    return deduped;
}

/**
 * GET /leaderboard
 * Returns top users sorted by specified field (public, no auth required)
 * Query: ?sort_by=xp|level|total_bubbles_popped|total_flashes|total_video_minutes|total_lock_cards_completed|is_patreon
 * Query: ?limit=N (default 200, max 1000)
 */
app.get('/leaderboard', async (req, res) => {
    try {
        // Temporarily disabled during new leaderboard transition (2026-02-06)
        return res.status(503).json({ error: 'Leaderboard is temporarily unavailable while we upgrade to the new system. Check back soon!' });

        if (!redis) {
            return res.status(503).json({ error: 'Leaderboard not available' });
        }

        const sortBy = req.query.sort_by || 'xp';
        const validSortFields = ['xp', 'level', 'total_bubbles_popped', 'total_flashes',
                                  'total_video_minutes', 'total_lock_cards_completed', 'is_patreon'];

        if (!validSortFields.includes(sortBy)) {
            return res.status(400).json({ error: 'Invalid sort_by field' });
        }

        // Parse limit parameter (default 200, max 1000)
        let limit = parseInt(req.query.limit) || 200;
        limit = Math.min(Math.max(limit, 1), 1000);

        // Check if we have a fresh cache
        const now = Date.now();
        if (!leaderboardProfilesCache || (now - leaderboardCacheTime) > LEADERBOARD_CACHE_TTL) {
            // Cache is stale or doesn't exist - fetch fresh data
            console.log('Leaderboard cache miss - fetching from Redis');
            leaderboardProfilesCache = await fetchAllProfiles();
            leaderboardCacheTime = now;
        } else {
            console.log('Leaderboard cache hit - using cached data');
        }

        // Work with a copy for sorting (don't modify the cache)
        // Use cached online status as-is - it's accurate within the 2-minute cache window
        const deduped = [...leaderboardProfilesCache];

        // Sort by requested field (descending)
        deduped.sort((a, b) => {
            if (sortBy === 'is_patreon') {
                // Sort by Patreon status first, then by level as secondary
                const aPat = a.is_patreon ? 1 : 0;
                const bPat = b.is_patreon ? 1 : 0;
                if (bPat !== aPat) return bPat - aPat;
                return b.level - a.level; // Secondary sort by level
            }
            const aVal = sortBy === 'xp' ? a.xp :
                         sortBy === 'level' ? a.level :
                         a[sortBy] || 0;
            const bVal = sortBy === 'xp' ? b.xp :
                         sortBy === 'level' ? b.level :
                         b[sortBy] || 0;
            return bVal - aVal;
        });

        // Count online users
        const onlineCount = deduped.filter(p => p.is_online).length;

        // Add ranks and apply limit
        const ranked = deduped.slice(0, limit).map((p, i) => ({
            rank: i + 1,
            ...p
        }));

        res.json({
            entries: ranked,
            total_users: deduped.length,
            online_users: onlineCount,
            sort_by: sortBy,
            fetched_at: new Date().toISOString()
        });
    } catch (error) {
        console.error('Leaderboard error:', error.message);
        res.status(500).json({ error: 'Failed to fetch leaderboard' });
    }
});

/**
 * GET /user/lookup
 * Look up a specific user's profile by display_name (for profile viewer)
 * Returns fresh is_online status and avatar info
 * Query: ?display_name=XXX
 */
app.get('/user/lookup', async (req, res) => {
    try {
        if (!redis) {
            return res.status(503).json({ error: 'Service not available' });
        }

        // Per-IP cooldown (5s) to prevent mass enumeration
        // R12/L4: Atomic NX cooldown to prevent TOCTOU race
        const lookupIp = req.ip || 'unknown';
        const lookupCooldownKey = `lookup_cooldown:${lookupIp}`;
        const cooldownSet = await redis.set(lookupCooldownKey, '1', { nx: true, ex: 5 });
        if (!cooldownSet) {
            return res.status(429).json({ error: 'Lookup cooldown active', retry_after: 5 });
        }

        const displayName = req.query.display_name;
        if (!displayName) {
            return res.status(400).json({ error: 'display_name query parameter required' });
        }
        // R12/M4: Validate display_name chars before using in Redis key
        if (!isValidDisplayNameChars(displayName)) {
            return res.status(400).json({ error: 'Invalid display name characters' });
        }

        // Find user by display_name index
        const indexKey = `display_name_index:${displayName.toLowerCase()}`;
        const userId = await redis.get(indexKey);

        if (!userId) {
            return res.status(404).json({ error: 'User not found' });
        }

        // Try to get profile data from multiple possible sources
        let profile = null;
        let patreonProfile = null;
        let discordAvatar = null;
        let discordId = null;

        // Check unified user first
        if (userId.startsWith('u_')) {
            const userData = await redis.get(`user:${userId}`);
            if (userData) {
                profile = typeof userData === 'string' ? JSON.parse(userData) : userData;
                discordId = profile.discord_id;

                // Also load Patreon profile for additional settings (share_profile_picture, etc)
                // since /user/sync stores to profile:XXX, not user:u_XXX
                if (profile.patreon_id) {
                    const patreonData = await redis.get(`profile:${profile.patreon_id}`);
                    if (patreonData) {
                        patreonProfile = typeof patreonData === 'string' ? JSON.parse(patreonData) : patreonData;
                    }
                }
            }
        }

        // Check Patreon profile (for non-unified users)
        if (!profile) {
            const patreonData = await redis.get(`profile:${userId}`);
            if (patreonData) {
                profile = typeof patreonData === 'string' ? JSON.parse(patreonData) : patreonData;
                patreonProfile = profile; // Same profile in this case
            }
        }

        // Check Discord profile
        if (!profile && userId.startsWith('discord_')) {
            const discordUserId = userId.replace('discord_', '');
            const discordData = await redis.get(`discord_profile:${discordUserId}`);
            if (discordData) {
                profile = typeof discordData === 'string' ? JSON.parse(discordData) : discordData;
                discordId = discordUserId;
            }
        }

        if (!profile) {
            return res.status(404).json({ error: 'Profile not found' });
        }

        // Get Discord profile data (for avatar and fresh last_seen)
        // Check discord_id from all profile sources
        if (!discordId) {
            discordId = profile.discord_id || patreonProfile?.discord_id;
        }

        let discordProfile = null;
        if (discordId) {
            const discordProfileKey = `discord_profile:${discordId}`;
            const discordProfileData = await redis.get(discordProfileKey);
            if (discordProfileData) {
                discordProfile = typeof discordProfileData === 'string' ? JSON.parse(discordProfileData) : discordProfileData;
                if (discordProfile.avatar) {
                    // Discord avatar URL format
                    const ext = discordProfile.avatar.startsWith('a_') ? 'gif' : 'png';
                    discordAvatar = `https://cdn.discordapp.com/avatars/${discordId}/${discordProfile.avatar}.${ext}?size=256`;
                }
            }
        }

        // Note: Previously had a full SCAN of discord_profile:* as fallback here.
        // Removed to prevent O(N) Redis operations on this unauthenticated endpoint (DoS vector).
        // Discord profiles are linked via user.discord_id which is checked above.

        // Calculate fresh online status - check ALL sources and OR them together
        // (matching leaderboard's merge logic: if ANY source shows online, user is online)
        const now = Date.now();
        const ONLINE_THRESHOLD = 60 * 1000; // 60 seconds

        // Check main profile
        const mainLastSeen = profile.last_seen || profile.updated_at;
        const mainLastSeenDate = mainLastSeen ? new Date(mainLastSeen) : null;
        const mainIsOnline = mainLastSeenDate && (now - mainLastSeenDate.getTime()) < ONLINE_THRESHOLD;

        // Check discord profile
        const discordLastSeen = discordProfile?.last_seen;
        const discordLastSeenDate = discordLastSeen ? new Date(discordLastSeen) : null;
        const discordIsOnline = discordLastSeenDate && (now - discordLastSeenDate.getTime()) < ONLINE_THRESHOLD;

        // Check patreon profile (if separate from main)
        const patreonLastSeen = patreonProfile?.last_seen || patreonProfile?.updated_at;
        const patreonLastSeenDate = patreonLastSeen ? new Date(patreonLastSeen) : null;
        const patreonIsOnline = patreonLastSeenDate && (now - patreonLastSeenDate.getTime()) < ONLINE_THRESHOLD;

        // User is online if ANY source shows them as online
        const actuallyOnline = mainIsOnline || discordIsOnline || patreonIsOnline;
        // Respect show_online_status setting from any profile source (default true if not set)
        const showOnlineStatus = profile.show_online_status !== false &&
                                  patreonProfile?.show_online_status !== false &&
                                  discordProfile?.show_online_status !== false;
        const isOnline = actuallyOnline && showOnlineStatus;

        // For last_seen display, use the most recent timestamp
        let lastSeen = mainLastSeen;
        if (discordLastSeenDate && (!mainLastSeenDate || discordLastSeenDate > mainLastSeenDate)) {
            lastSeen = discordLastSeen;
        }
        if (patreonLastSeenDate && (!lastSeen || patreonLastSeenDate > new Date(lastSeen))) {
            lastSeen = patreonLastSeen;
        }

        // Check Patreon status (from either main profile or patreon profile)
        const isPatreon = profile.patreon_is_active || patreonProfile?.patreon_is_active ||
                          profile.patreon_is_whitelisted || patreonProfile?.patreon_is_whitelisted ||
                          (profile.patreon_tier && profile.patreon_tier > 0) ||
                          (patreonProfile?.patreon_tier && patreonProfile.patreon_tier > 0);

        // Check share_profile_picture - V2 user settings take priority
        // If V2 user explicitly set it to false, respect that (don't fall back to old profiles)
        // Only fall back to old profiles if V2 user hasn't set it (undefined)
        let shareProfilePicture;
        if (typeof profile.share_profile_picture === 'boolean') {
            // V2 user has explicitly set this setting
            shareProfilePicture = profile.share_profile_picture;
        } else {
            // Fall back to old profile settings
            shareProfilePicture = patreonProfile?.share_profile_picture || discordProfile?.share_profile_picture;
        }

        // Check allow_discord_dm from all profile sources
        const allowDiscordDm = profile.allow_discord_dm || patreonProfile?.allow_discord_dm || discordProfile?.allow_discord_dm;

        // Privacy: never expose patron_name as display_name
        let safeName = profile.display_name || null;
        if (safeName && profile.patron_name && !profile.display_name_set_at &&
            safeName.toLowerCase().trim() === profile.patron_name.toLowerCase().trim()) {
            safeName = null;
        }

        res.json({
            display_name: safeName,
            level: profile.level || 1,
            xp: profile.xp || 0,
            total_bubbles_popped: profile.stats?.total_bubbles_popped || 0,
            total_flashes: profile.stats?.total_flashes || 0,
            total_video_minutes: Math.round((profile.stats?.total_video_minutes || 0) * 10) / 10,
            total_lock_cards_completed: profile.stats?.total_lock_cards_completed || 0,
            achievements_count: profile.achievements?.length || 0,
            achievements: profile.achievements || [],
            is_online: isOnline,
            is_patreon: !!isPatreon,
            patreon_tier: profile.patreon_tier || patreonProfile?.patreon_tier || 0,
            discord_id: (allowDiscordDm && discordId) ? discordId : null,
            // Use discordAvatar (from discord profile hash) or stored avatar_url (from sync) as fallback
            avatar_url: shareProfilePicture ? (discordAvatar || profile.avatar_url || patreonProfile?.avatar_url || discordProfile?.avatar_url || null) : null,
            last_seen: showOnlineStatus ? lastSeen : null
        });
    } catch (error) {
        console.error('User lookup error:', error.message);
        res.status(500).json({ error: 'Failed to look up user' });
    }
});

// =============================================================================
// ADMIN ENDPOINTS (for testing/moderation)
// =============================================================================

/**
 * Calculate XP required for a single level (matching client-side formula)
 */
function getXPForLevel(level) {
    if (level <= 80) {
        return Math.round(800 + (level - 1) * (1700 / 79));
    } else if (level <= 100) {
        return Math.round(2500 + (level - 80) * (1500 / 20));
    } else if (level <= 125) {
        return Math.round(4000 + (level - 100) * (2000 / 25));
    } else if (level <= 150) {
        return Math.round(6000 + (level - 125) * (4000 / 25));
    } else {
        return Math.round(10000 * Math.pow(1.03, level - 150));
    }
}

/**
 * Calculate cumulative XP to reach a given level
 */
function getCumulativeXPForLevel(level) {
    let total = 0;
    for (let i = 1; i <= level; i++) {
        total += getXPForLevel(i);
    }
    return total;
}

/**
 * POST /admin/set-level
 * Set a user's level (admin only - for testing)
 * Body: { display_name: string, level: number, admin_token: string, fix_xp?: boolean }
 */
app.post('/admin/set-level', async (req, res) => {
    try {
        const { display_name, level, fix_xp } = req.body;
        const admin_token = req.headers['x-admin-token'];

        // Admin token check
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        if (!display_name || typeof display_name !== 'string') {
            return res.status(400).json({ error: 'display_name is required' });
        }

        if (typeof level !== 'number' || level < 1 || level > 999) {
            return res.status(400).json({ error: 'level must be a number between 1 and 999' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        // Scan for profile by display_name (works for both Patreon and Discord profiles)
        const targetName = display_name.toLowerCase();
        let foundKey = null;
        let foundProfile = null;

        const scanForProfile = async (pattern) => {
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: pattern, count: 100 });
                cursor = String(result[0]);
                const keys = result[1] || [];

                for (const key of keys) {
                    try {
                        const data = await redis.get(key);
                        if (data) {
                            const profile = typeof data === 'string' ? JSON.parse(data) : data;
                            if (profile.display_name && profile.display_name.toLowerCase() === targetName) {
                                foundKey = key;
                                foundProfile = profile;
                                return true;
                            }
                        }
                    } catch (e) { /* ignore parse errors */ }
                }
            } while (cursor !== "0");
            return false;
        };

        // Check V2 user:* keys FIRST (these are the authoritative records)
        await scanForProfile('user:*');
        if (!foundKey) await scanForProfile('profile:*');
        if (!foundKey) await scanForProfile('discord_profile:*');

        // If no profile found, check if user exists by display_name index and create profile
        if (!foundKey) {
            const indexKey = `display_name_index:${targetName}`;
            const unifiedId = await redis.get(indexKey);

            if (unifiedId) {
                // User index exists but profile is missing - recreate it
                const userKey = `user:${unifiedId}`;
                const userData = await redis.get(userKey);

                if (userData) {
                    const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

                    // Determine profile key based on account type
                    if (user.discord_id) {
                        foundKey = `discord_profile:${user.discord_id}`;
                    } else if (user.patreon_id) {
                        foundKey = `profile:${user.patreon_id}`;
                    } else {
                        foundKey = `profile:${unifiedId}`;
                    }

                    // Create a new profile
                    foundProfile = {
                        unified_id: unifiedId,
                        display_name: display_name,
                        level: 1,
                        xp: 0,
                        achievements: [],
                        stats: {},
                        patreon_id: user.patreon_id || null,
                        discord_id: user.discord_id || null,
                        created_at: new Date().toISOString(),
                        recreated_from_backup: true
                    };

                    console.log(`[ADMIN] Creating missing profile for "${display_name}" at ${foundKey}`);
                }
            }
        }

        if (!foundKey) {
            return res.status(404).json({ error: `No profile found with display_name "${display_name}"` });
        }

        const oldLevel = foundProfile.level || 1;
        const oldXp = foundProfile.xp || 0;
        foundProfile.level = level;
        foundProfile.updated_at = new Date().toISOString();

        // Optionally fix XP to match the level
        let newXp = oldXp;
        if (fix_xp) {
            newXp = getCumulativeXPForLevel(level - 1); // XP to reach this level (not including current level progress)
            foundProfile.xp = newXp;
        }

        // For V2 user:* records, also update leaderboard and set level_reset flag
        if (foundKey.startsWith('user:')) {
            foundProfile.highest_level_ever = Math.max(foundProfile.highest_level_ever || 0, level);
            foundProfile.level_reset_at = new Date().toISOString(); // Prevent client from pushing old cached level back
            delete foundProfile.level_reset; // Clean up old boolean flag
            foundProfile.unlocks = calculateUnlocks(level);

            // Update leaderboard
            const season = getCurrentSeason();
            const leaderboardXp = fix_xp ? newXp : (foundProfile.xp || 0);
            const setLevelMemberId = foundKey.replace('user:', '');
            await redis.zadd(`leaderboard:${season}`, { score: safeLeaderboardScore(leaderboardXp), member: setLevelMemberId });
            try { await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(foundProfile.total_xp_earned || 0), member: setLevelMemberId }); } catch (e) { console.log(`[WARN] All-time leaderboard update failed:`, e.message); }
        }

        await redis.set(foundKey, JSON.stringify(foundProfile));

        console.log(`[ADMIN] Set level for "${display_name}" (${foundKey}): ${oldLevel} -> ${level}${fix_xp ? `, XP: ${oldXp} -> ${newXp}` : ''}`);

        res.json({
            success: true,
            display_name: display_name,
            profile_key: foundKey,
            old_level: oldLevel,
            new_level: level,
            old_xp: oldXp,
            new_xp: fix_xp ? newXp : oldXp,
            xp_fixed: !!fix_xp
        });
    } catch (error) {
        console.error('Admin set-level error:', error.message);
        res.status(500).json({ error: 'Failed to set level' });
    }
});

/**
 * POST /admin/reset-progress
 * Reset a user's progress to level 1, 0 XP, clear achievements and stats (admin only)
 * Body: { display_name: string, admin_token: string }
 */
app.post('/admin/reset-progress', async (req, res) => {
    try {
        const { display_name } = req.body;
        const admin_token = req.headers['x-admin-token'];

        // Admin token check
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        if (!display_name || typeof display_name !== 'string') {
            return res.status(400).json({ error: 'display_name is required' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        // Scan for profile by display_name (works for both Patreon and Discord profiles)
        const targetName = display_name.toLowerCase();
        let profileKey = null;
        let oldProfile = null;

        const scanForProfile = async (pattern) => {
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: pattern, count: 100 });
                cursor = String(result[0]);
                const keys = result[1] || [];

                for (const key of keys) {
                    try {
                        const data = await redis.get(key);
                        if (data) {
                            const profile = typeof data === 'string' ? JSON.parse(data) : data;
                            if (profile.display_name && profile.display_name.toLowerCase() === targetName) {
                                profileKey = key;
                                oldProfile = profile;
                                return true;
                            }
                        }
                    } catch (e) { /* ignore parse errors */ }
                }
            } while (cursor !== "0");
            return false;
        };

        await scanForProfile('profile:*');
        if (!profileKey) await scanForProfile('discord_profile:*');

        if (!profileKey) {
            return res.status(404).json({ error: `No profile found with display_name "${display_name}"` });
        }

        // Reset profile but keep identity info
        const resetProfile = {
            xp: 0,
            level: 1,
            achievements: [],
            stats: {
                completed_sessions: 0,
                longest_session_minutes: 0,
                total_flashes: 0,
                consecutive_days: 0,
                total_bubbles_popped: 0,
                total_video_minutes: 0,
                total_lock_cards_completed: 0
            },
            last_session: null,
            updated_at: new Date().toISOString(),
            display_name: oldProfile.display_name,
            display_name_set_at: oldProfile.display_name_set_at
        };

        await redis.set(profileKey, JSON.stringify(resetProfile));

        console.log(`[ADMIN] Reset progress for "${display_name}" (${profileKey}): Level ${oldProfile.level} -> 1, XP ${oldProfile.xp} -> 0`);

        res.json({
            success: true,
            display_name: display_name,
            profile_key: profileKey,
            old_level: oldProfile.level,
            old_xp: oldProfile.xp,
            message: 'Progress reset to level 1, 0 XP, cleared achievements and stats'
        });
    } catch (error) {
        console.error('Admin reset-progress error:', error.message);
        res.status(500).json({ error: 'Failed to reset progress' });
    }
});

/**
 * GET /admin/user-info
 * Get user info by display_name (admin only)
 * Query: ?display_name=xxx&admin_token=xxx
 */
app.get('/admin/user-info', async (req, res) => {
    try {
        const { display_name } = req.query;
        const admin_token = req.headers['x-admin-token'];

        // Admin token check
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        if (!display_name) {
            return res.status(400).json({ error: 'display_name query param required' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        // Find user by display_name index
        const indexKey = `display_name_index:${display_name.toLowerCase()}`;
        const userId = await redis.get(indexKey);

        if (!userId) {
            return res.status(404).json({ error: `User with display_name "${display_name}" not found` });
        }

        // Load profile - try unified user key first, then Patreon, then Discord
        let profileKey = `user:${userId}`;
        let profile = await redis.get(profileKey);

        // Try legacy Patreon profile key
        if (!profile) {
            profileKey = `${PROFILE_KEY_PREFIX}${userId}`;
            profile = await redis.get(profileKey);
        }

        // If not found and userId starts with discord_, try Discord profile key
        if (!profile && userId.startsWith('discord_')) {
            const discordId = userId.replace('discord_', '');
            profileKey = `${DISCORD_PROFILE_KEY_PREFIX}${discordId}`;
            profile = await redis.get(profileKey);
        }

        if (!profile) {
            return res.status(404).json({ error: `Profile for user ${userId} not found` });
        }

        const profileData = typeof profile === 'string' ? JSON.parse(profile) : profile;

        res.json({
            user_id: userId,
            profile_key: profileKey,
            profile: filterUserForAdmin(profileData)
        });
    } catch (error) {
        console.error('Admin user-info error:', error.message);
        res.status(500).json({ error: 'Failed to get user info' });
    }
});

/**
 * POST /admin/cleanup-index
 * Removes orphaned display_name_index entries (where the profile no longer exists)
 * Body: { display_name: string, admin_token: string }
 */
app.post('/admin/cleanup-index', async (req, res) => {
    try {
        const { display_name } = req.body;
        const admin_token = req.headers['x-admin-token'];

        // Admin token check
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        if (!display_name || typeof display_name !== 'string') {
            return res.status(400).json({ error: 'display_name is required' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        const indexKey = `display_name_index:${display_name.toLowerCase()}`;
        const userId = await redis.get(indexKey);

        if (!userId) {
            return res.status(404).json({ error: `No index found for display_name "${display_name}"` });
        }

        // Check if the profile actually exists
        const profileKey = `profile:${userId}`;
        const discordProfileKey = `discord_profile:${userId}`;
        const profile = await redis.get(profileKey) || await redis.get(discordProfileKey);

        if (profile) {
            return res.status(400).json({
                error: `Profile for "${display_name}" still exists - not an orphan`,
                user_id: userId
            });
        }

        // Profile doesn't exist - delete the orphaned index
        await redis.del(indexKey);

        console.log(`[ADMIN] Cleaned up orphaned index for "${display_name}" (was pointing to ${userId})`);

        res.json({
            success: true,
            display_name: display_name,
            deleted_index: indexKey,
            was_pointing_to: userId,
            message: `Orphaned index for "${display_name}" has been removed`
        });
    } catch (error) {
        console.error('Admin cleanup-index error:', error.message);
        res.status(500).json({ error: 'Failed to cleanup index' });
    }
});

/**
 * GET /admin/scan-orphaned-indexes
 * Scans all display_name_index:* keys and checks if the referenced user/profile still exists.
 * Returns a list of orphaned entries (name -> stale ID) without deleting anything.
 * Query: ?admin_token=xxx
 */
app.get('/admin/scan-orphaned-indexes', async (req, res) => {
    try {
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock = await enforceAdminScanCooldown(res, 'scan-orphaned-indexes');
        if (scanBlock) return;

        const orphaned = [];
        const valid = [];
        let cursor = "0";

        do {
            const result = await redis.scan(cursor, { match: 'display_name_index:*', count: 100 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                const displayName = key.replace('display_name_index:', '');
                const targetId = await redis.get(key);
                if (!targetId) {
                    orphaned.push({ display_name: displayName, points_to: null, reason: 'index value is null' });
                    continue;
                }

                // Check if target exists as user:*, profile:*, or discord_profile:*
                const userExists = await redis.get(`user:${targetId}`);
                const profileExists = !userExists ? await redis.get(`profile:${targetId}`) : null;
                const discordProfileExists = (!userExists && !profileExists) ? await redis.get(`discord_profile:${targetId}`) : null;

                if (userExists || profileExists || discordProfileExists) {
                    valid.push({ display_name: displayName, points_to: targetId });
                } else {
                    orphaned.push({ display_name: displayName, points_to: targetId, reason: 'target record does not exist' });
                }
            }
        } while (cursor !== "0");

        res.json({
            success: true,
            total_indexes: orphaned.length + valid.length,
            valid_count: valid.length,
            orphaned_count: orphaned.length,
            orphaned
        });
    } catch (error) {
        console.error('Scan orphaned indexes error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /admin/fix-display-name
 * Set or fix display_name for a user by their Discord ID or Patreon ID
 * Also recreates the index and applies whitelist if applicable
 * Body: { discord_id?: string, patreon_id?: string, new_display_name: string, admin_token: string }
 */
app.post('/admin/fix-display-name', async (req, res) => {
    try {
        const { discord_id, patreon_id, new_display_name } = req.body;
        const admin_token = req.headers['x-admin-token'];

        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        if (!new_display_name || typeof new_display_name !== 'string') {
            return res.status(400).json({ error: 'new_display_name is required' });
        }

        if (!discord_id && !patreon_id) {
            return res.status(400).json({ error: 'Either discord_id or patreon_id is required' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        const trimmedName = new_display_name.trim();
        if (trimmedName.length < 3 || trimmedName.length > 20) {
            return res.status(400).json({ error: 'Display name must be 3-20 characters' });
        }

        // Check if new display_name is already taken by someone else
        const newIndexKey = `display_name_index:${trimmedName.toLowerCase()}`;
        const existingOwner = await redis.get(newIndexKey);

        // Determine user ID and profile key
        let userId, profileKey, profile;

        if (discord_id) {
            userId = `discord_${discord_id}`;
            profileKey = `discord_profile:${discord_id}`;
        } else {
            userId = patreon_id;
            profileKey = `profile:${patreon_id}`;
        }

        // Check if name is taken by a different user
        if (existingOwner && existingOwner !== userId) {
            return res.status(400).json({
                error: 'Display name already taken by another user',
                taken_by: existingOwner
            });
        }

        // Load existing profile
        const profileData = await redis.get(profileKey);
        if (!profileData) {
            return res.status(404).json({
                error: 'Profile not found',
                profile_key: profileKey
            });
        }

        profile = typeof profileData === 'string' ? JSON.parse(profileData) : profileData;
        const oldDisplayName = profile.display_name;

        // If they had an old display_name, remove the old index
        if (oldDisplayName && oldDisplayName.toLowerCase() !== trimmedName.toLowerCase()) {
            const oldIndexKey = `display_name_index:${oldDisplayName.toLowerCase()}`;
            await redis.del(oldIndexKey);
            console.log(`[ADMIN] Removed old index: ${oldIndexKey}`);
        }

        // Update profile with new display_name
        profile.display_name = trimmedName;
        profile.display_name_set_at = profile.display_name_set_at || new Date().toISOString();
        profile.display_name_fixed_at = new Date().toISOString();
        profile.updated_at = new Date().toISOString();

        // Check whitelist and update status
        const whitelistedByDisplayName = isWhitelisted(null, null, trimmedName);
        if (whitelistedByDisplayName) {
            profile.patreon_is_whitelisted = true;
            profile.patreon_tier = Math.max(profile.patreon_tier || 0, 2);
            console.log(`[ADMIN] User ${userId} whitelisted by display_name: "${trimmedName}"`);
        }

        // Save profile and create new index
        await redis.set(profileKey, JSON.stringify(profile));
        await redis.set(newIndexKey, userId);

        console.log(`[ADMIN] Fixed display_name for ${userId}: "${oldDisplayName || '(none)'}" -> "${trimmedName}"`);

        res.json({
            success: true,
            user_id: userId,
            profile_key: profileKey,
            old_display_name: oldDisplayName || null,
            new_display_name: trimmedName,
            is_whitelisted: whitelistedByDisplayName || profile.patreon_is_whitelisted || false,
            profile: filterUserForAdmin(profile)
        });
    } catch (error) {
        console.error('Admin fix-display-name error:', error.message);
        res.status(500).json({ error: 'Failed to fix display name' });
    }
});

/**
 * POST /admin/fix-patreon-tier
 * Fixes patreon_tier for whitelisted users who have tier 0
 * Body: { display_name: string, admin_token: string, tier?: number }
 */
app.post('/admin/fix-patreon-tier', async (req, res) => {
    try {
        const { display_name, tier } = req.body;
        const admin_token = req.headers['x-admin-token'];

        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        if (!display_name || typeof display_name !== 'string') {
            return res.status(400).json({ error: 'display_name is required' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        // Find user by display_name_index
        const normalizedName = display_name.toLowerCase().trim();
        const unifiedId = await redis.get(`display_name_index:${normalizedName}`);

        if (!unifiedId) {
            return res.status(404).json({ error: 'User not found' });
        }

        const userData = await redis.get(`user:${unifiedId}`);
        if (!userData) {
            return res.status(404).json({ error: 'User data not found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
        const oldTier = user.patreon_tier || 0;

        // Check if user is whitelisted
        const isWhitelistedUser = isWhitelisted(null, null, user.display_name);

        // Set tier: use provided tier, or 2 if whitelisted, or keep existing
        const newTier = tier !== undefined ? tier : (isWhitelistedUser ? Math.max(oldTier, 2) : oldTier);

        user.patreon_tier = newTier;
        user.patreon_is_whitelisted = isWhitelistedUser || user.patreon_is_whitelisted;
        user.updated_at = new Date().toISOString();

        await redis.set(`user:${unifiedId}`, JSON.stringify(user));

        console.log(`[ADMIN] Fixed patreon_tier for "${display_name}": ${oldTier} -> ${newTier}, whitelisted=${isWhitelistedUser}`);

        res.json({
            success: true,
            display_name: user.display_name,
            unified_id: unifiedId,
            old_tier: oldTier,
            new_tier: newTier,
            is_whitelisted: isWhitelistedUser || user.patreon_is_whitelisted
        });
    } catch (error) {
        console.error('Admin fix-patreon-tier error:', error.message);
        res.status(500).json({ error: 'Failed to fix patreon tier' });
    }
});

/**
 * POST /admin/set-og
 * Set is_season0_og flag for a user by display_name
 * Body: { display_name: string, admin_token: string, og?: boolean }
 */
app.post('/admin/set-og', async (req, res) => {
    try {
        const { display_name, og } = req.body;
        const admin_token = req.headers['x-admin-token'];

        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        if (!display_name || typeof display_name !== 'string') {
            return res.status(400).json({ error: 'display_name is required' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        const normalizedName = display_name.toLowerCase().trim();
        const unifiedId = await redis.get(`display_name_index:${normalizedName}`);

        if (!unifiedId) {
            return res.status(404).json({ error: `User "${display_name}" not found` });
        }

        const userData = await redis.get(`user:${unifiedId}`);
        if (!userData) {
            return res.status(404).json({ error: 'User data not found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
        const oldOg = user.is_season0_og || false;
        const newOg = og !== undefined ? og : true;

        user.is_season0_og = newOg;
        user.updated_at = new Date().toISOString();

        await redis.set(`user:${unifiedId}`, JSON.stringify(user));

        console.log(`[ADMIN] Set OG for "${display_name}" (${unifiedId}): ${oldOg} -> ${newOg}`);

        res.json({
            success: true,
            display_name: user.display_name,
            unified_id: unifiedId,
            old_og: oldOg,
            new_og: newOg
        });
    } catch (error) {
        console.error('Admin set-og error:', error.message);
        res.status(500).json({ error: 'Failed to set OG status' });
    }
});

/**
 * POST /admin/delete-profile
 * Deletes a user profile by scanning all profile keys for matching display_name
 * Works for both Patreon and Discord profiles
 * Body: { display_name: string, admin_token: string }
 */
app.post('/admin/delete-profile', async (req, res) => {
    try {
        const { display_name } = req.body;
        const admin_token = req.headers['x-admin-token'];

        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        if (!display_name || typeof display_name !== 'string') {
            return res.status(400).json({ error: 'display_name is required' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        const targetName = display_name.toLowerCase();
        let foundKey = null;
        let foundProfile = null;

        // Scan both profile patterns
        const scanForProfile = async (pattern) => {
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: pattern, count: 100 });
                cursor = String(result[0]);
                const keys = result[1] || [];

                for (const key of keys) {
                    try {
                        const data = await redis.get(key);
                        if (data) {
                            const profile = typeof data === 'string' ? JSON.parse(data) : data;
                            if (profile.display_name && profile.display_name.toLowerCase() === targetName) {
                                foundKey = key;
                                foundProfile = profile;
                                return true;
                            }
                        }
                    } catch (e) { /* ignore parse errors */ }
                }
            } while (cursor !== "0");
            return false;
        };

        await scanForProfile('profile:*');
        if (!foundKey) await scanForProfile('discord_profile:*');

        if (!foundKey) {
            return res.status(404).json({ error: `No profile found with display_name "${display_name}"` });
        }

        // Delete the profile
        await redis.del(foundKey);

        // Also clean up the display_name index if it exists
        const indexKey = `display_name_index:${targetName}`;
        await redis.del(indexKey);

        console.log(`[ADMIN] Deleted profile for "${display_name}" (key: ${foundKey})`);

        res.json({
            success: true,
            display_name: display_name,
            deleted_key: foundKey,
            deleted_index: indexKey,
            old_level: foundProfile.level,
            old_xp: foundProfile.xp,
            message: `Profile for "${display_name}" has been deleted`
        });
    } catch (error) {
        console.error('Admin delete-profile error:', error.message);
        res.status(500).json({ error: 'Failed to delete profile' });
    }
});

/**
 * POST /admin/batch-restore
 * Batch restore user profiles from backup data - recreates ALL necessary records
 * Body: { profiles: [{unified_id, level, xp, achievements, stats, display_name, patreon_id, discord_id}], admin_token: string }
 */
app.post('/admin/batch-restore', async (req, res) => {
    try {
        const { profiles } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!profiles || !Array.isArray(profiles)) {
            return res.status(400).json({ error: 'profiles array is required' });
        }
        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        const results = [];
        for (const profile of profiles) {
            try {
                const { unified_id, level, xp, achievements, stats, display_name, patreon_id, discord_id } = profile;
                if (!unified_id) {
                    results.push({ unified_id: 'unknown', success: false, error: 'missing unified_id' });
                    continue;
                }

                // First, recreate the user record if it doesn't exist
                const userKey = `user:${unified_id}`;
                let userData = await redis.get(userKey);
                let user;

                if (!userData) {
                    // User record is gone - recreate it from backup data
                    user = {
                        unified_id,
                        display_name: display_name || null,
                        patreon_id: patreon_id || null,
                        discord_id: discord_id || null,
                        created_at: new Date().toISOString(),
                        recreated_from_backup: true
                    };
                    await redis.set(userKey, JSON.stringify(user));
                    console.log(`[ADMIN] Recreated user record: ${userKey}`);
                } else {
                    user = typeof userData === 'string' ? JSON.parse(userData) : userData;
                }

                // Determine profile key based on account type
                let profileKey;
                if (discord_id || user.discord_id) {
                    profileKey = `discord_profile:${discord_id || user.discord_id}`;
                } else if (patreon_id || user.patreon_id) {
                    profileKey = `profile:${patreon_id || user.patreon_id}`;
                } else {
                    profileKey = `profile:${unified_id}`;
                }

                // Build the restored profile with full data
                const restoredProfile = {
                    unified_id,
                    display_name: display_name || user.display_name || null,
                    level: level || 1,
                    xp: xp || 0,
                    achievements: achievements || [],
                    stats: stats || {},
                    patreon_id: patreon_id || user.patreon_id || null,
                    discord_id: discord_id || user.discord_id || null,
                    created_at: new Date().toISOString(),
                    restored_at: new Date().toISOString(),
                    restored_from_backup: true
                };

                await redis.set(profileKey, JSON.stringify(restoredProfile));

                // Also update/create display_name index if applicable
                if (restoredProfile.display_name) {
                    await redis.set(`display_name_index:${restoredProfile.display_name.toLowerCase()}`, unified_id);
                }

                // Create patreon/discord user indexes if applicable
                if (restoredProfile.patreon_id) {
                    await redis.set(`patreon_user:${restoredProfile.patreon_id}`, unified_id);
                }
                if (restoredProfile.discord_id) {
                    await redis.set(`discord_user:${restoredProfile.discord_id}`, unified_id);
                }

                results.push({
                    unified_id,
                    success: true,
                    profile_key: profileKey,
                    level,
                    display_name: restoredProfile.display_name
                });

                console.log(`[ADMIN] Restored profile: ${restoredProfile.display_name || unified_id} to Level ${level}`);
            } catch (err) {
                results.push({ unified_id: profile.unified_id || 'unknown', success: false, error: err.message });
            }
        }

        const successCount = results.filter(r => r.success).length;
        const failCount = results.filter(r => !r.success).length;

        res.json({
            success: true,
            total: profiles.length,
            restored: successCount,
            failed: failCount,
            results
        });
    } catch (error) {
        console.error('Admin batch-restore error:', error.message);
        res.status(500).json({ error: 'Failed to batch restore profiles' });
    }
});

/**
 * POST /admin/link-accounts
 * Links a Patreon account to a Discord account
 * Body: { patreon_id: string, discord_id: string, admin_token: string }
 */
app.post('/admin/link-accounts', async (req, res) => {
    try {
        const { patreon_id, discord_id } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!patreon_id || !discord_id) {
            return res.status(400).json({ error: 'Both patreon_id and discord_id are required' });
        }
        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        const patreonProfileKey = `${PROFILE_KEY_PREFIX}${patreon_id}`;
        const discordProfileKey = `discord_profile:${discord_id}`;
        let patreonProfile = await redis.get(patreonProfileKey);
        let discordProfile = await redis.get(discordProfileKey);
        if (patreonProfile && typeof patreonProfile === 'string') patreonProfile = JSON.parse(patreonProfile);
        if (discordProfile && typeof discordProfile === 'string') discordProfile = JSON.parse(discordProfile);

        const unifiedId = generateUnifiedId();
        const mergedProfile = {
            display_name: discordProfile?.display_name || patreonProfile?.display_name || 'Unknown',
            level: Math.max(patreonProfile?.level || 1, discordProfile?.level || 1),
            xp: Math.max(patreonProfile?.xp || 0, discordProfile?.xp || 0),
            total_flashes: (patreonProfile?.total_flashes || 0) + (discordProfile?.total_flashes || 0),
            total_video_minutes: (patreonProfile?.total_video_minutes || 0) + (discordProfile?.total_video_minutes || 0),
            total_lock_cards_completed: (patreonProfile?.total_lock_cards_completed || 0) + (discordProfile?.total_lock_cards_completed || 0),
            total_bubbles_popped: (patreonProfile?.total_bubbles_popped || 0) + (discordProfile?.total_bubbles_popped || 0),
            achievements: [...new Set([...(patreonProfile?.achievements || []), ...(discordProfile?.achievements || [])])],
            patreon_id, discord_id,
            created_at: patreonProfile?.created_at || discordProfile?.created_at || new Date().toISOString(),
            updated_at: new Date().toISOString()
        };

        await redis.set(`${UNIFIED_USER_PREFIX}${unifiedId}`, JSON.stringify(mergedProfile));
        await redis.set(`${PATREON_USER_INDEX}${patreon_id}`, unifiedId);
        await redis.set(`${DISCORD_USER_INDEX}${discord_id}`, unifiedId);
        if (mergedProfile.display_name) {
            await redis.set(`display_name_index:${mergedProfile.display_name.toLowerCase()}`, unifiedId);
        }
        if (discordProfile) {
            discordProfile.unified_id = unifiedId;
            discordProfile.patreon_id = patreon_id;
            await redis.set(discordProfileKey, JSON.stringify(discordProfile));
        }

        console.log(`[ADMIN] Linked: Patreon ${patreon_id} <-> Discord ${discord_id} (${unifiedId})`);
        res.json({ success: true, unified_id: unifiedId, merged_profile: filterUserForAdmin(mergedProfile) });
    } catch (error) {
        console.error('Admin link-accounts error:', error.message);
        res.status(500).json({ error: 'Failed to link accounts' });
    }
});

/**
 * GET /admin/search-profiles
 * Search profiles by pattern
 * Query: ?pattern=xxx&admin_token=xxx
 */
app.get('/admin/search-profiles', async (req, res) => {
    try {
        const { pattern } = req.query;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!pattern) return res.status(400).json({ error: 'pattern required' });
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock = await enforceAdminScanCooldown(res, 'search-profiles');
        if (scanBlock) return;

        const searchPattern = pattern.toLowerCase();
        const results = [];
        const scanProfiles = async (keyPattern, type) => {
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: keyPattern, count: 100 });
                cursor = String(result[0]);
                for (const key of (result[1] || [])) {
                    try {
                        const data = await redis.get(key);
                        if (data) {
                            const p = typeof data === 'string' ? JSON.parse(data) : data;
                            if (p.display_name && p.display_name.toLowerCase().includes(searchPattern)) {
                                results.push({ key, type, display_name: p.display_name, level: p.level,
                                    patreon_id: p.patreon_id || (type === 'patreon' ? key.replace('profile:', '') : null),
                                    discord_id: p.discord_id || (type === 'discord' ? key.replace('discord_profile:', '') : null) });
                            }
                        }
                    } catch (e) {}
                }
            } while (cursor !== "0");
        };
        await scanProfiles('profile:*', 'patreon');
        await scanProfiles('discord_profile:*', 'discord');
        res.json({ pattern, results_count: results.length, results });
    } catch (error) {
        console.error('Admin search-profiles error:', error.message);
        res.status(500).json({ error: 'Failed to search profiles' });
    }
});

/**
 * GET /admin/scan-unified-users
 * Scan unified users and find those with missing/empty display_name
 * Query: ?admin_token=xxx
 */
app.get('/admin/scan-unified-users', async (req, res) => {
    try {
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock = await enforceAdminScanCooldown(res, 'scan-unified-users');
        if (scanBlock) return;

        const brokenUsers = [];
        const allUsers = [];
        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                try {
                    const data = await redis.get(key);
                    if (data) {
                        const user = typeof data === 'string' ? JSON.parse(data) : data;
                        allUsers.push({ key, unified_id: user.unified_id, display_name: user.display_name, patreon_id: user.patreon_id, discord_id: user.discord_id });
                        if (!user.display_name || user.display_name.trim() === '') {
                            brokenUsers.push({ key, user: filterUserForAdmin(user) });
                        }
                    }
                } catch (e) {}
            }
        } while (cursor !== "0");

        res.json({
            total_unified_users: allUsers.length,
            broken_users_count: brokenUsers.length,
            broken_users: brokenUsers,
            all_users: allUsers
        });
    } catch (error) {
        console.error('Admin scan-unified-users error:', error.message);
        res.status(500).json({ error: 'Failed to scan unified users' });
    }
});


/**
 * POST /admin/seed-total-users
 * Count all user:* keys and set global:total_users counter.
 * Also seeds online_users sorted set from last_seen timestamps.
 * Body: { dry_run?: boolean }
 */
app.post('/admin/seed-total-users', async (req, res) => {
    try {
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock = await enforceAdminScanCooldown(res, 'seed-total-users');
        if (scanBlock) return;

        const dryRun = req.body?.dry_run === true;
        let totalCount = 0;
        let onlineSeeded = 0;
        const onlineThreshold = Date.now() - 180000; // 3 minutes

        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 200 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                totalCount++;
                if (!dryRun) {
                    try {
                        const data = await redis.get(key);
                        if (data) {
                            const user = typeof data === 'string' ? JSON.parse(data) : data;
                            // Seed online_users sorted set from last_seen (only visible users)
                            if (user.last_seen && user.show_online_status !== false) {
                                const lastSeenMs = new Date(user.last_seen).getTime();
                                if (lastSeenMs > onlineThreshold && user.unified_id) {
                                    await redis.zadd('online_users', { score: lastSeenMs, member: user.unified_id });
                                    onlineSeeded++;
                                }
                            }
                        }
                    } catch (e) {}
                }
            }
        } while (cursor !== "0");

        if (!dryRun) {
            await redis.set('global:total_users', totalCount);
        }

        res.json({
            dry_run: dryRun,
            total_users_counted: totalCount,
            online_users_seeded: onlineSeeded,
            global_counter_set: !dryRun
        });
    } catch (error) {
        console.error('Admin seed-total-users error:', error.message);
        res.status(500).json({ error: 'Failed to seed total users' });
    }
});

/**
 * POST /admin/fix-migrated-users
 * Scan all users and fix missing current_season, discord_index keys, leaderboard entries
 * Body: { admin_token: string, dry_run?: boolean }
 */
app.post('/admin/fix-migrated-users', async (req, res) => {
    try {
        const { dry_run = true } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock2 = await enforceAdminScanCooldown(res, 'fix-migrated-users');
        if (scanBlock2) return;

        const currentSeason = getCurrentSeason();
        const fixes = { missing_season: [], missing_discord_index: [], missing_leaderboard: [] };

        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                try {
                    const data = await redis.get(key);
                    if (!data) continue;
                    const user = typeof data === 'string' ? JSON.parse(data) : data;
                    if (!user.unified_id) continue;
                    let changed = false;

                    // Fix missing current_season
                    if (!user.current_season) {
                        fixes.missing_season.push({ unified_id: user.unified_id, display_name: user.display_name });
                        if (!dry_run) {
                            user.current_season = currentSeason;
                            changed = true;
                        }
                    }

                    // Fix missing discord_index for discord users
                    if (user.discord_id) {
                        const discordIndexKey = `discord_index:${user.discord_id}`;
                        const existing = await redis.get(discordIndexKey);
                        if (!existing) {
                            fixes.missing_discord_index.push({ unified_id: user.unified_id, display_name: user.display_name, discord_id: user.discord_id });
                            if (!dry_run) {
                                await redis.set(discordIndexKey, user.unified_id);
                            }
                        }
                    }

                    // Fix missing leaderboard entry
                    const season = user.current_season || currentSeason;
                    const score = await redis.zscore(`leaderboard:${season}`, user.unified_id);
                    if (score === null || score === undefined) {
                        fixes.missing_leaderboard.push({ unified_id: user.unified_id, display_name: user.display_name, season });
                        if (!dry_run) {
                            await redis.zadd(`leaderboard:${season}`, { score: safeLeaderboardScore(user.xp), member: user.unified_id });
                            try { await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(user.total_xp_earned || 0), member: user.unified_id }); } catch (e) { /* best-effort */ }
                        }
                    }

                    if (changed) {
                        await redis.set(key, JSON.stringify(user));
                    }
                } catch (e) {}
            }
        } while (cursor !== "0");

        res.json({
            success: true,
            dry_run,
            fixes: {
                missing_season: fixes.missing_season.length,
                missing_discord_index: fixes.missing_discord_index.length,
                missing_leaderboard: fixes.missing_leaderboard.length,
                details: fixes
            }
        });
    } catch (error) {
        console.error('Admin fix-migrated-users error:', error.message);
        res.status(500).json({ error: 'Failed to fix migrated users' });
    }
});

/**
 * POST /admin/purge-user-data
 * Scans ALL keys and deletes any containing the specified display_name
 * Body: { display_name: string, admin_token: string, dry_run?: boolean }
 */
app.post('/admin/purge-user-data', async (req, res) => {
    try {
        const { display_name, dry_run = false } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!display_name) return res.status(400).json({ error: 'display_name required' });
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock = await enforceAdminScanCooldown(res, 'purge-user-data');
        if (scanBlock) return;

        const searchName = display_name.toLowerCase();
        const foundKeys = [];
        const deletedKeys = [];

        // Scan all keys
        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: '*', count: 100 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                try {
                    // Check key name itself
                    if (key.toLowerCase().includes(searchName)) {
                        foundKeys.push({ key, reason: 'key_name_match' });
                        if (!dry_run) {
                            await redis.del(key);
                            deletedKeys.push(key);
                        }
                        continue;
                    }
                    // Check value content
                    const data = await redis.get(key);
                    if (data) {
                        const strData = typeof data === 'string' ? data : JSON.stringify(data);
                        if (strData.toLowerCase().includes(searchName)) {
                            foundKeys.push({ key, reason: 'value_contains_name' });
                            if (!dry_run) {
                                await redis.del(key);
                                deletedKeys.push(key);
                            }
                        }
                    }
                } catch (e) {}
            }
        } while (cursor !== "0");

        console.log(`[ADMIN] Purge user data for "${display_name}": found ${foundKeys.length}, deleted ${deletedKeys.length} (dry_run: ${dry_run})`);
        res.json({
            display_name,
            dry_run,
            found_count: foundKeys.length,
            deleted_count: deletedKeys.length,
            found_keys: foundKeys,
            deleted_keys: deletedKeys
        });
    } catch (error) {
        console.error('Admin purge-user-data error:', error.message);
        res.status(500).json({ error: 'Failed to purge user data' });
    }
});


// Allowed key prefixes for raw admin key operations
const ADMIN_KEY_PREFIXES = ['user:', 'config:', 'whitelist:', 'profile:', 'discord_profile:', 'display_name_index:', 'leaderboard:'];
function isAllowedKeyPrefix(key) {
    return ADMIN_KEY_PREFIXES.some(prefix => key.startsWith(prefix));
}

/**
 * GET /admin/get-key
 * Get raw value of a Redis key (restricted to allowed prefixes)
 * Query: ?key=xxx&admin_token=xxx
 */
app.get('/admin/get-key', async (req, res) => {
    try {
        const { key } = req.query;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!key) return res.status(400).json({ error: 'key required' });
        if (!isAllowedKeyPrefix(key)) return res.status(400).json({ error: 'Key prefix not allowed' });
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const data = await redis.get(key);
        if (data === null) return res.status(404).json({ error: 'Key not found', key });

        let parsed = data;
        try {
            if (typeof data === 'string') parsed = JSON.parse(data);
        } catch (e) {}

        // Strip auth material from user: key responses
        if (key.startsWith('user:') && parsed && typeof parsed === 'object') {
            parsed = filterUserForAdmin(parsed);
        }

        res.json({ key, value: parsed });
    } catch (error) {
        console.error('Admin get-key error:', error.message);
        res.status(500).json({ error: 'Failed to get key' });
    }
});

/**
 * POST /admin/delete-key
 * Delete a specific Redis key
 * Body: { key: string, admin_token: string }
 */
app.post('/admin/delete-key', async (req, res) => {
    try {
        const { key } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!key) return res.status(400).json({ error: 'key required' });
        if (!isAllowedKeyPrefix(key)) return res.status(400).json({ error: 'Key prefix not allowed' });
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const deleted = await redis.del(key);
        console.log(`[ADMIN] Deleted key: ${key} (result: ${deleted})`);
        res.json({ success: true, key, deleted: deleted > 0 });
    } catch (error) {
        console.error('Admin delete-key error:', error.message);
        res.status(500).json({ error: 'Failed to delete key' });
    }
});

/**
 * POST /admin/update-key
 * Update a specific field in a JSON Redis key
 * Body: { key: string, field: string, value: any, admin_token: string }
 */
app.post('/admin/update-key', async (req, res) => {
    try {
        const { key, field, value } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!key || !field) return res.status(400).json({ error: 'key and field required' });
        if (!isAllowedKeyPrefix(key)) return res.status(400).json({ error: 'Key prefix not allowed' });
        // Prevent prototype pollution via field name
        if (field === '__proto__' || field === 'constructor' || field === 'prototype') {
            return res.status(400).json({ error: 'Invalid field name' });
        }
        // Prevent overwriting auth material via update-key
        if (field === 'password_hash' || field === 'auth_token_hash') {
            return res.status(400).json({ error: 'Cannot modify auth fields via update-key' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        let data = await redis.get(key);
        if (data === null) return res.status(404).json({ error: 'Key not found', key });

        let parsed = typeof data === 'string' ? JSON.parse(data) : data;
        const oldValue = parsed[field];
        if (value === null) {
            delete parsed[field];
        } else {
            parsed[field] = value;
        }
        await redis.set(key, JSON.stringify(parsed));

        console.log(`[ADMIN] Updated key ${key}.${field}: ${JSON.stringify(oldValue)} -> ${JSON.stringify(value)}`);
        res.json({ success: true, key, field, old_value: oldValue, new_value: value });
    } catch (error) {
        console.error('Admin update-key error:', error.message);
        res.status(500).json({ error: 'Failed to update key' });
    }
});

/**
 * POST /admin/set-key
 * Directly set a Redis key with JSON data (for restoring backups)
 * Body: { key: string, data: object, admin_token: string }
 */
app.post('/admin/set-key', async (req, res) => {
    try {
        const { key, data } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!key || !data) return res.status(400).json({ error: 'key and data required' });
        if (!isAllowedKeyPrefix(key)) return res.status(400).json({ error: 'Key prefix not allowed' });
        // Prevent prototype pollution via data keys
        if (typeof data === 'object' && data !== null) {
            if ('__proto__' in data || 'constructor' in data || 'prototype' in data) {
                return res.status(400).json({ error: 'Invalid data: forbidden property names' });
            }
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const existing = await redis.get(key);
        await redis.set(key, JSON.stringify(data));

        console.log(`[ADMIN] Set key ${key}: ${existing ? 'REPLACED' : 'CREATED'}`);
        res.json({ success: true, key, replaced: !!existing });
    } catch (error) {
        console.error('Admin set-key error:', error.message);
        res.status(500).json({ error: 'Failed to set key' });
    }
});

/**
 * POST /admin/mass-restore-display-names
 * DISABLED — This endpoint previously copied patron_name into display_name,
 * which leaked real names onto the leaderboard. Do not re-enable.
 */
app.post('/admin/mass-restore-display-names', async (req, res) => {
    return res.status(410).json({ error: 'This endpoint has been disabled to protect user privacy. patron_name must never be used as display_name.' });
});

/**
 * POST /admin/cleanup-empty-profiles
 * Find and delete profile:* entries that have no display_name
 * These cause "already linked to different profile" errors when users try to link accounts
 * Body: { admin_token: string, dry_run?: boolean }
 */
app.post('/admin/cleanup-empty-profiles', async (req, res) => {
    try {
        const { dry_run = true } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock = await enforceAdminScanCooldown(res, 'cleanup-empty-profiles');
        if (scanBlock) return;

        const emptyProfiles = [];
        const deleted = [];
        const errors = [];
        let totalScanned = 0;

        // Scan all profile:* keys
        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'profile:*', count: 100 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                totalScanned++;
                try {
                    const data = await redis.get(key);
                    if (!data) continue;
                    const profile = typeof data === 'string' ? JSON.parse(data) : data;

                    // Check if display_name is missing or empty
                    const displayName = profile.display_name;
                    if (!displayName || displayName.trim() === '') {
                        const patreonId = key.replace('profile:', '');
                        emptyProfiles.push({
                            key,
                            patreon_id: patreonId,
                            // email_hash intentionally excluded — PII
                            tier: profile.patreon_tier,
                            is_active: profile.patreon_is_active,
                            is_whitelisted: profile.patreon_is_whitelisted
                        });

                        if (!dry_run) {
                            // Delete the profile and any associated index
                            await redis.del(key);
                            await redis.del(`patreon_user_index:${patreonId}`);
                            deleted.push(key);
                        }
                    }
                } catch (e) {
                    errors.push({ key, error: e.message });
                }
            }
        } while (cursor !== "0");

        res.json({
            dry_run,
            total_scanned: totalScanned,
            empty_profiles_found: emptyProfiles.length,
            deleted_count: deleted.length,
            empty_profiles: emptyProfiles,
            deleted: deleted,
            errors
        });
    } catch (error) {
        console.error('Admin cleanup-empty-profiles error:', error.message);
        res.status(500).json({ error: 'Failed to cleanup empty profiles' });
    }
});

// =============================================================================
// V2 API - MONTHLY SEASONS SYSTEM
// =============================================================================

/**
 * Level unlock thresholds
 */
const LEVEL_UNLOCKS = {
    avatars: 50,
    autonomy_mode: 75,
    takeover_mode: 100,
    ai_companion: 150
};

/**
 * Calculate unlocks based on highest level ever achieved
 */
function calculateUnlocks(highestLevel) {
    return {
        avatars: highestLevel >= LEVEL_UNLOCKS.avatars,
        autonomy_mode: highestLevel >= LEVEL_UNLOCKS.autonomy_mode,
        takeover_mode: highestLevel >= LEVEL_UNLOCKS.takeover_mode,
        ai_companion: highestLevel >= LEVEL_UNLOCKS.ai_companion
    };
}

/**
 * Get current season string (YYYY-MM)
 */
function getCurrentSeason() {
    const now = new Date();
    return `${now.getUTCFullYear()}-${String(now.getUTCMonth() + 1).padStart(2, '0')}`;
}

/**
 * POST /admin/capture-legacy-users
 * Captures all existing users from profile:*, discord_profile:*, user:* keys
 * and stores them in season0:<provider_id> format for v5.5 migration.
 * Body: { admin_token: string, dry_run?: boolean }
 */
app.post('/admin/capture-legacy-users', async (req, res) => {
    try {
        const { dry_run = true } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock = await enforceAdminScanCooldown(res, 'capture-legacy-users');
        if (scanBlock) return;

        const legacyUsers = [];
        const season0Keys = [];
        const errors = [];

        // Helper to extract user data from any profile type
        const processProfile = (key, profile) => {
            const displayName = profile.display_name;
            if (!displayName || displayName.trim() === '') return null;

            // Extract provider IDs
            let patreonId = profile.patreon_id || profile.patreon_user_id;
            let discordId = profile.discord_id;

            // For profile:XXX keys, the XXX is often the patreon_id
            if (key.startsWith('profile:') && !patreonId) {
                const keyId = key.replace('profile:', '');
                // Only use if it looks like a Patreon ID (numeric)
                if (/^\d+$/.test(keyId)) {
                    patreonId = keyId;
                }
            }

            // For discord_profile:XXX keys, the XXX is the discord_id
            if (key.startsWith('discord_profile:') && !discordId) {
                discordId = key.replace('discord_profile:', '');
            }

            // For user:u_XXX keys, the unified_id is in the key
            let unifiedId = null;
            if (key.startsWith('user:')) {
                unifiedId = key.replace('user:', '');
            }

            return {
                key,
                display_name: displayName,
                patreon_id: patreonId || null,
                discord_id: discordId || null,
                unified_id: unifiedId,
                level: profile.level || 1,
                xp: profile.xp || 0,
                highest_level_ever: profile.level || 1,
                achievements: profile.achievements || [],
                stats: {
                    total_flashes: profile.stats?.total_flashes || 0,
                    total_bubbles_popped: profile.stats?.total_bubbles_popped || 0,
                    total_video_minutes: profile.stats?.total_video_minutes || 0,
                    total_lock_cards_completed: profile.stats?.total_lock_cards_completed || 0
                },
                patreon_tier: profile.patreon_tier || 0,
                patreon_is_active: profile.patreon_is_active || false,
                patreon_is_whitelisted: profile.patreon_is_whitelisted || false,
                // email_hash intentionally excluded — PII
                allow_discord_dm: profile.allow_discord_dm || false,
                show_online_status: profile.show_online_status !== false,
                captured_at: new Date().toISOString()
            };
        };

        // Scan all profile types
        const patterns = ['profile:*', 'discord_profile:*', 'user:*'];
        for (const pattern of patterns) {
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: pattern, count: 100 });
                cursor = String(result[0]);
                for (const key of (result[1] || [])) {
                    try {
                        const data = await redis.get(key);
                        if (!data) continue;
                        const profile = typeof data === 'string' ? JSON.parse(data) : data;
                        const userData = processProfile(key, profile);
                        if (userData) {
                            legacyUsers.push(userData);
                        }
                    } catch (e) {
                        errors.push({ key, error: e.message });
                    }
                }
            } while (cursor !== "0");
        }

        // Deduplicate by display_name (keep highest level)
        const deduped = new Map();
        for (const user of legacyUsers) {
            const nameKey = user.display_name.toLowerCase();
            if (deduped.has(nameKey)) {
                const existing = deduped.get(nameKey);
                // Keep the one with higher level, merge provider IDs
                if (user.level > existing.level) {
                    // Merge provider IDs from existing
                    if (!user.patreon_id && existing.patreon_id) user.patreon_id = existing.patreon_id;
                    if (!user.discord_id && existing.discord_id) user.discord_id = existing.discord_id;
                    // Merge achievements
                    const allAchievements = new Set([...user.achievements, ...existing.achievements]);
                    user.achievements = [...allAchievements];
                    // Keep higher stats
                    user.stats.total_flashes = Math.max(user.stats.total_flashes, existing.stats.total_flashes);
                    user.stats.total_bubbles_popped = Math.max(user.stats.total_bubbles_popped, existing.stats.total_bubbles_popped);
                    user.stats.total_video_minutes = Math.max(user.stats.total_video_minutes, existing.stats.total_video_minutes);
                    user.stats.total_lock_cards_completed = Math.max(user.stats.total_lock_cards_completed, existing.stats.total_lock_cards_completed);
                    deduped.set(nameKey, user);
                } else {
                    // Keep existing but merge provider IDs from this one
                    if (!existing.patreon_id && user.patreon_id) existing.patreon_id = user.patreon_id;
                    if (!existing.discord_id && user.discord_id) existing.discord_id = user.discord_id;
                    // Merge achievements
                    const allAchievements = new Set([...existing.achievements, ...user.achievements]);
                    existing.achievements = [...allAchievements];
                    // Keep higher stats
                    existing.stats.total_flashes = Math.max(existing.stats.total_flashes, user.stats.total_flashes);
                    existing.stats.total_bubbles_popped = Math.max(existing.stats.total_bubbles_popped, user.stats.total_bubbles_popped);
                    existing.stats.total_video_minutes = Math.max(existing.stats.total_video_minutes, user.stats.total_video_minutes);
                    existing.stats.total_lock_cards_completed = Math.max(existing.stats.total_lock_cards_completed, user.stats.total_lock_cards_completed);
                }
            } else {
                deduped.set(nameKey, user);
            }
        }

        const finalUsers = [...deduped.values()];

        // Store in season0: keys if not dry run
        if (!dry_run) {
            for (const user of finalUsers) {
                const season0Data = {
                    display_name: user.display_name,
                    patreon_id: user.patreon_id,
                    discord_id: user.discord_id,
                    highest_level_ever: user.level,
                    achievements: user.achievements,
                    stats: user.stats,
                    unlocks: calculateUnlocks(user.level),
                    patreon_tier: user.patreon_tier,
                    patreon_is_active: user.patreon_is_active,
                    patreon_is_whitelisted: user.patreon_is_whitelisted,
                    // email_hash intentionally excluded — PII
                    allow_discord_dm: user.allow_discord_dm,
                    show_online_status: user.show_online_status,
                    captured_at: user.captured_at
                };

                // Store by patreon_id if available
                if (user.patreon_id) {
                    await redis.set(`season0:patreon:${user.patreon_id}`, JSON.stringify(season0Data));
                    season0Keys.push(`season0:patreon:${user.patreon_id}`);
                }
                // Store by discord_id if available
                if (user.discord_id) {
                    await redis.set(`season0:discord:${user.discord_id}`, JSON.stringify(season0Data));
                    season0Keys.push(`season0:discord:${user.discord_id}`);
                }
            }
        }

        console.log(`[ADMIN] Captured ${finalUsers.length} legacy users (dry_run: ${dry_run})`);

        res.json({
            dry_run,
            total_raw_profiles: legacyUsers.length,
            deduplicated_users: finalUsers.length,
            season0_keys_created: season0Keys.length,
            errors_count: errors.length,
            users: finalUsers,
            season0_keys: season0Keys,
            errors
        });
    } catch (error) {
        console.error('Admin capture-legacy-users error:', error.message);
        res.status(500).json({ error: 'Failed to capture legacy users' });
    }
});

/**
 * POST /admin/migrate-legacy-to-v2
 * Migrate legacy profile:* and discord_profile:* users to full V2 user:* records.
 * Skips users that already have V2 records (checked via patreon_index, discord_index, patreon_user, discord_user).
 * Deduplicates by display_name (keeps highest level, merges provider IDs and stats).
 * Body: { dry_run?: boolean } (default: true)
 */
app.post('/admin/migrate-legacy-to-v2', async (req, res) => {
    try {
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const { dry_run = true } = req.body || {};
        const errors = [];
        const legacyProfiles = [];
        const skippedAlreadyMigrated = [];

        // Scan profile:* and discord_profile:* keys
        const patterns = ['profile:*', 'discord_profile:*'];
        for (const pattern of patterns) {
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: pattern, count: 100 });
                cursor = String(result[0]);
                for (const key of (result[1] || [])) {
                    try {
                        const data = await redis.get(key);
                        if (!data) continue;
                        const profile = typeof data === 'string' ? JSON.parse(data) : data;

                        // Extract provider IDs from key
                        let patreonId = null;
                        let discordId = null;

                        if (key.startsWith('profile:')) {
                            patreonId = key.replace('profile:', '');
                        }
                        if (key.startsWith('discord_profile:')) {
                            discordId = key.replace('discord_profile:', '');
                        }

                        // Also check provider IDs stored in the profile itself
                        if (profile.patreon_id) patreonId = profile.patreon_id;
                        if (profile.discord_id) discordId = profile.discord_id;

                        // Check if already migrated to V2 via any index
                        let alreadyMigrated = false;
                        if (patreonId) {
                            const v2Index = await redis.get(`patreon_index:${patreonId}`);
                            const v1Index = await redis.get(`patreon_user:${patreonId}`);
                            if (v2Index || v1Index) alreadyMigrated = true;
                        }
                        if (!alreadyMigrated && discordId) {
                            const v2Index = await redis.get(`discord_index:${discordId}`);
                            const v1Index = await redis.get(`discord_user:${discordId}`);
                            if (v2Index || v1Index) alreadyMigrated = true;
                        }

                        if (alreadyMigrated) {
                            skippedAlreadyMigrated.push({ key, patreon_id: patreonId, discord_id: discordId });
                            continue;
                        }

                        const displayName = profile.display_name || profile.displayName || profile.name || null;
                        if (!displayName) {
                            errors.push({ key, error: 'No display_name found' });
                            continue;
                        }

                        legacyProfiles.push({
                            key,
                            display_name: displayName,
                            patreon_id: patreonId,
                            discord_id: discordId,
                            level: profile.level || 1,
                            xp: profile.xp || 0,
                            achievements: profile.achievements || [],
                            stats: {
                                total_flashes: profile.stats?.total_flashes || 0,
                                total_bubbles_popped: profile.stats?.total_bubbles_popped || 0,
                                total_video_minutes: profile.stats?.total_video_minutes || 0,
                                total_lock_cards_completed: profile.stats?.total_lock_cards_completed || 0
                            },
                            patreon_tier: profile.patreon_tier || 0,
                            patreon_is_active: profile.patreon_is_active || false,
                            patreon_is_whitelisted: profile.patreon_is_whitelisted || false,
                            // email_hash intentionally excluded — PII
                            allow_discord_dm: profile.allow_discord_dm || false,
                            show_online_status: profile.show_online_status !== false
                        });
                    } catch (e) {
                        errors.push({ key, error: e.message });
                    }
                }
            } while (cursor !== "0");
        }

        // Deduplicate by display_name (keep highest level, merge provider IDs + stats)
        const deduped = new Map();
        for (const user of legacyProfiles) {
            const nameKey = user.display_name.toLowerCase();
            if (deduped.has(nameKey)) {
                const existing = deduped.get(nameKey);
                if (user.level > existing.level) {
                    if (!user.patreon_id && existing.patreon_id) user.patreon_id = existing.patreon_id;
                    if (!user.discord_id && existing.discord_id) user.discord_id = existing.discord_id;
                    const allAchievements = new Set([...user.achievements, ...existing.achievements]);
                    user.achievements = [...allAchievements];
                    user.stats.total_flashes = Math.max(user.stats.total_flashes, existing.stats.total_flashes);
                    user.stats.total_bubbles_popped = Math.max(user.stats.total_bubbles_popped, existing.stats.total_bubbles_popped);
                    user.stats.total_video_minutes = Math.max(user.stats.total_video_minutes, existing.stats.total_video_minutes);
                    user.stats.total_lock_cards_completed = Math.max(user.stats.total_lock_cards_completed, existing.stats.total_lock_cards_completed);
                    if (!user.email_hash && existing.email_hash) user.email_hash = existing.email_hash;
                    deduped.set(nameKey, user);
                } else {
                    if (!existing.patreon_id && user.patreon_id) existing.patreon_id = user.patreon_id;
                    if (!existing.discord_id && user.discord_id) existing.discord_id = user.discord_id;
                    const allAchievements = new Set([...existing.achievements, ...user.achievements]);
                    existing.achievements = [...allAchievements];
                    existing.stats.total_flashes = Math.max(existing.stats.total_flashes, user.stats.total_flashes);
                    existing.stats.total_bubbles_popped = Math.max(existing.stats.total_bubbles_popped, user.stats.total_bubbles_popped);
                    existing.stats.total_video_minutes = Math.max(existing.stats.total_video_minutes, user.stats.total_video_minutes);
                    existing.stats.total_lock_cards_completed = Math.max(existing.stats.total_lock_cards_completed, user.stats.total_lock_cards_completed);
                    if (!existing.email_hash && user.email_hash) existing.email_hash = user.email_hash;
                }
            } else {
                deduped.set(nameKey, user);
            }
        }

        const finalUsers = [...deduped.values()];
        const created = [];

        if (!dry_run) {
            for (const user of finalUsers) {
                try {
                    const unifiedId = generateUnifiedId();
                    const now = new Date().toISOString();

                    // Check display_name isn't already taken
                    const nameOwner = await isDisplayNameTaken(user.display_name);
                    if (nameOwner) {
                        errors.push({ key: user.key, error: `display_name "${user.display_name}" already taken by ${nameOwner}` });
                        continue;
                    }

                    // Check Season 0 legacy data for OG status
                    let isV1Og = false;
                    if (user.patreon_id) {
                        const s0 = await lookupSeason0('patreon', user.patreon_id);
                        if (s0) isV1Og = true;
                    }
                    if (!isV1Og && user.discord_id) {
                        const s0 = await lookupSeason0('discord', user.discord_id);
                        if (s0) isV1Og = true;
                        if (!isV1Og) isV1Og = await isV1DiscordUser(user.discord_id);
                    }

                    const newUser = {
                        unified_id: unifiedId,
                        display_name: user.display_name,
                        patreon_id: user.patreon_id || null,
                        discord_id: user.discord_id || null,
                        email_hash: user.email_hash || null,
                        current_season: '2026-02',
                        xp: user.xp || 0,
                        level: user.level || 1,
                        highest_level_ever: user.level || 1,
                        unlocks: calculateUnlocks(user.level || 1),
                        achievements: user.achievements || [],
                        all_time_stats: {
                            total_flashes: 0,
                            total_bubbles_popped: 0,
                            total_video_minutes: 0,
                            total_lock_cards_completed: 0,
                            seasons_completed: 0
                        },
                        stats: user.stats,
                        is_season0_og: isV1Og,
                        patreon_tier: user.patreon_tier || 0,
                        patreon_is_active: user.patreon_is_active || false,
                        patreon_is_whitelisted: user.patreon_is_whitelisted || false,
                        skill_points: user.level || 1,
                        unlocked_skills: [],
                        allow_discord_dm: user.allow_discord_dm || false,
                        show_online_status: user.show_online_status !== false,
                        auth_token_hash: null,
                        auth_method: 'legacy_migration',
                        display_name_set_at: now,
                        created_at: now,
                        updated_at: now,
                        last_seen: now
                    };

                    // Save user record
                    await redis.set(`user:${unifiedId}`, JSON.stringify(newUser));

                    // Create indexes
                    await redis.set(`display_name_index:${user.display_name.toLowerCase()}`, unifiedId);
                    if (user.patreon_id) {
                        await redis.set(`patreon_index:${user.patreon_id}`, unifiedId);
                    }
                    if (user.discord_id) {
                        await redis.set(`discord_index:${user.discord_id}`, unifiedId);
                    }
                    if (user.email_hash) {
                        await redis.set(`email_index:${user.email_hash}`, unifiedId);
                    }

                    // Add to leaderboard
                    const totalXp = user.xp || 0;
                    await redis.zadd('leaderboard:2026-02', { score: totalXp, member: unifiedId });
                    try { await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(user.total_xp_earned || 0), member: unifiedId }); } catch (e) { /* best-effort */ }

                    created.push({
                        unified_id: unifiedId,
                        display_name: user.display_name,
                        patreon_id: user.patreon_id,
                        discord_id: user.discord_id,
                        level: user.level,
                        is_season0_og: isV1Og
                    });
                } catch (e) {
                    errors.push({ key: user.key, error: e.message });
                }
            }
        }

        console.log(`[ADMIN] migrate-legacy-to-v2: ${finalUsers.length} users to migrate, ${created.length} created, ${skippedAlreadyMigrated.length} skipped (already migrated), dry_run=${dry_run}`);

        res.json({
            dry_run,
            total_legacy_profiles: legacyProfiles.length,
            deduplicated_count: finalUsers.length,
            already_migrated_count: skippedAlreadyMigrated.length,
            created_count: created.length,
            errors_count: errors.length,
            users: dry_run ? finalUsers.map(u => ({
                display_name: u.display_name,
                patreon_id: u.patreon_id,
                discord_id: u.discord_id,
                level: u.level,
                xp: u.xp
            })) : created,
            already_migrated: skippedAlreadyMigrated,
            errors
        });
    } catch (error) {
        console.error('Admin migrate-legacy-to-v2 error:', error.message);
        res.status(500).json({ error: 'Failed to migrate legacy users' });
    }
});

/**
 * GET /admin/season0-lookup
 * Look up a Season 0 legacy user by provider ID
 * Query: ?admin_token=XXX&provider=patreon|discord&provider_id=XXX
 */
app.get('/admin/season0-lookup', async (req, res) => {
    try {
        const { provider, provider_id } = req.query;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        if (!provider || !provider_id) {
            return res.status(400).json({ error: 'provider and provider_id required' });
        }

        if (!['patreon', 'discord'].includes(provider)) {
            return res.status(400).json({ error: 'provider must be patreon or discord' });
        }

        const key = `season0:${provider}:${provider_id}`;
        const data = await redis.get(key);

        if (!data) {
            return res.json({ found: false, provider, provider_id });
        }

        const legacyUser = typeof data === 'string' ? JSON.parse(data) : data;
        res.json({
            found: true,
            provider,
            provider_id,
            legacy_user: legacyUser
        });
    } catch (error) {
        console.error('Admin season0-lookup error:', error.message);
        res.status(500).json({ error: 'Failed to lookup legacy user' });
    }
});

// generateUnifiedId() is defined earlier in the file (uses crypto.randomBytes)

/**
 * Look up Season 0 legacy data for a provider
 * @returns {Promise<object|null>} Legacy user data or null
 */
async function lookupSeason0(provider, providerId) {
    if (!redis) return null;
    const key = `season0:${provider}:${providerId}`;
    const data = await redis.get(key);
    if (!data) return null;
    return typeof data === 'string' ? JSON.parse(data) : data;
}

/**
 * Check if a discord ID was present in the V1 leaderboard backup.
 * Uses the season0_discord_ids Redis set as a fallback when season0: keys are missing.
 * @returns {Promise<boolean>}
 */
async function isV1DiscordUser(discordId) {
    if (!redis || !discordId) return false;
    return await redis.sismember('season0_discord_ids', discordId);
}

/**
 * Check if a display name is truly taken, auto-cleaning orphaned indexes.
 * Returns the unified_id of the owner if taken, or null if available (orphan was cleaned).
 */
async function isDisplayNameTaken(name) {
    if (!redis || !name) return null;
    const indexKey = `display_name_index:${name.toLowerCase()}`;
    const existingId = await redis.get(indexKey);
    if (!existingId) return null;

    // Verify the target record actually exists
    const userExists = await redis.get(`user:${existingId}`);
    if (userExists) return existingId; // genuinely taken

    const profileExists = await redis.get(`profile:${existingId}`);
    if (profileExists) return existingId;

    const discordProfileExists = await redis.get(`discord_profile:${existingId}`);
    if (discordProfileExists) return existingId;

    // Orphaned index — auto-clean
    console.log(`[Auto-cleanup] Orphaned display_name_index for "${name}" (was pointing to ${existingId}) — deleted`);
    await redis.del(indexKey);
    return null;
}

/**
 * POST /v2/auth/discord
 * Discord authentication for v5.5+ (monthly seasons system)
 * Body: { access_token: string, display_name?: string }
 * - If display_name provided: creates new user or updates existing
 * - If not provided: just checks if user exists
 */
app.post('/v2/auth/discord', async (req, res) => {
    try {
        const { access_token, display_name } = req.body;

        if (!access_token) {
            return res.status(400).json({ error: 'access_token required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Get Discord user info
        const discordUser = await getDiscordUser(access_token);
        const discordId = discordUser.id;

        // Check if user already exists via index (check both V2 and V1 key formats)
        const indexKey = `discord_index:${discordId}`;
        let existingUnifiedId = await redis.get(indexKey);
        if (!existingUnifiedId) {
            // Fallback: check V1 key format (discord_user:<id>) for migrated users
            const v1Key = `discord_user:${discordId}`;
            const v1Id = await redis.get(v1Key);
            if (v1Id) {
                existingUnifiedId = v1Id;
                // Migrate to V2 key format so future lookups are fast
                await redis.set(indexKey, v1Id);
                console.log(`[V2] Migrated discord index key for ${discordId}: discord_user -> discord_index`);
            }
        }

        // Fallback: if index is missing, scan user:* keys to find orphaned account
        // This prevents data loss when discord_index key is accidentally deleted
        // R12/H1: Global cooldown (30s) prevents abuse of this SCAN on auth-exempt path
        if (!existingUnifiedId) {
            const orphanScanKey = `discord_orphan_scan_cooldown`;
            const scanAllowed = await redis.set(orphanScanKey, '1', { nx: true, ex: 30 });
            if (scanAllowed) {
                console.log(`[V2] discord_index missing for ${discordId}, scanning for orphaned account...`);
                try {
                    const scanResult = await Promise.race([
                        (async () => {
                            let cursor = 0;
                            do {
                                const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
                                cursor = result[0];
                                for (const key of result[1]) {
                                    try {
                                        const userData = await redis.get(key);
                                        if (!userData) continue;
                                        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
                                        if (user.discord_id === discordId) {
                                            return user.unified_id || key.replace('user:', '');
                                        }
                                    } catch (e) {
                                        // Skip malformed records
                                    }
                                }
                            } while (cursor !== 0);
                            return null;
                        })(),
                        new Promise(resolve => setTimeout(() => resolve(null), 3000))
                    ]);
                    if (scanResult) {
                        existingUnifiedId = scanResult;
                        await redis.set(indexKey, existingUnifiedId);
                        console.log(`[V2] RECOVERED orphaned account for discord ${discordId}: ${existingUnifiedId}. Repaired discord_index.`);
                    }
                } catch (scanErr) {
                    console.error('[V2] Discord orphan scan error:', scanErr.message);
                }
            } else {
                console.log(`[V2] discord_index missing for ${discordId}, orphan scan on cooldown (skipped)`);
            }
        }

        // Email-based auto-link HINT: if user not found by discord_id, check if their Discord email
        // matches an existing account. We do NOT auto-link transparently — return a hint instead
        // so the client can prompt the user to confirm. This prevents account takeover via email.
        let autoLinkHint = null;
        if (!existingUnifiedId && discordUser.email) {
            const emailLookup = await lookupUserByEmail(discordUser.email);
            if (emailLookup && emailLookup.user) {
                autoLinkHint = {
                    can_auto_link: true,
                    auto_link_unified_id: emailLookup.unified_id,
                    auto_link_display_name: emailLookup.user.display_name || null
                };
                console.log(`[V2] Email match found for Discord ${discordId} → ${emailLookup.unified_id} (hint only, not auto-linked)`);
            }
        }

        if (existingUnifiedId) {
            // Existing user - return their data
            const userData = await redis.get(`user:${existingUnifiedId}`);
            if (userData) {
                const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

                // Update last_seen + auto-fix missing fields from V1 migration
                user.last_seen = new Date().toISOString();
                const currentSeason = getCurrentSeason();
                if (!user.current_season) {
                    user.current_season = currentSeason;
                    console.log(`[V2] Auto-fixed missing current_season for ${existingUnifiedId}`);
                }
                if (!user.highest_level_ever) {
                    user.highest_level_ever = user.level || 1;
                }
                if (!user.unlocks) {
                    user.unlocks = calculateUnlocks(user.highest_level_ever || 0);
                }

                // Issue auth token
                const { token: authToken, hash: authTokenHash } = generateAuthToken();
                user.auth_token_hash = authTokenHash;

                await redis.set(`user:${existingUnifiedId}`, JSON.stringify(user));

                return res.json({
                    success: true,
                    is_new_user: false,
                    needs_registration: false,
                    unified_id: existingUnifiedId,
                    auth_token: authToken,
                    user: {
                        unified_id: user.unified_id,
                        display_name: user.display_name,
                        discord_id: user.discord_id,
                        patreon_id: user.patreon_id,
                        level: user.level,
                        xp: user.xp,
                        current_season: user.current_season,
                        highest_level_ever: user.highest_level_ever,
                        unlocks: user.unlocks,
                        achievements: user.achievements,
                        is_season0_og: user.is_season0_og,
                        patreon_tier: user.patreon_tier,
                        patreon_is_active: user.patreon_is_active
                    },
                    discord: {
                        id: discordUser.id,
                        username: discordUser.username,
                        global_name: discordUser.global_name,
                        avatar: discordUser.avatar
                    }
                });
            }
        }

        // User doesn't exist yet
        // Check Season 0 legacy data
        const legacyData = await lookupSeason0('discord', discordId);

        // Fallback: check V1 leaderboard discord IDs set if no season0 key found
        const isV1Og = legacyData ? true : await isV1DiscordUser(discordId);

        // If no display_name provided, just return registration status
        if (!display_name) {
            const response = {
                success: true,
                is_new_user: true,
                needs_registration: true,
                is_legacy_user: isV1Og,
                legacy_data: legacyData ? {
                    display_name: legacyData.display_name,
                    highest_level_ever: legacyData.highest_level_ever,
                    achievements_count: legacyData.achievements?.length || 0,
                    unlocks: legacyData.unlocks
                } : null,
                discord: {
                    id: discordUser.id,
                    username: discordUser.username,
                    global_name: discordUser.global_name,
                    avatar: discordUser.avatar
                }
            };
            // Include auto-link hint if email matched an existing account
            if (autoLinkHint) {
                response.can_auto_link = autoLinkHint.can_auto_link;
                response.auto_link_unified_id = autoLinkHint.auto_link_unified_id;
                response.auto_link_display_name = autoLinkHint.auto_link_display_name;
            }
            return res.json(response);
        }

        // Create new user with display_name
        const trimmedName = display_name.trim();
        if (!trimmedName) {
            return res.status(400).json({ error: 'display_name cannot be empty' });
        }
        if (!isValidDisplayNameChars(trimmedName)) {
            return res.status(400).json({ error: 'display_name contains invalid characters' });
        }
        if (trimmedName.length < 3 || trimmedName.length > 20) {
            return res.status(400).json({ error: 'display_name must be 3-20 characters' });
        }

        // CLEANUP: Delete any old user data for this discord_id before creating new account
        // This ONLY runs for whitelisted/OG users reclaiming their account - not for regular users
        // This protects existing users from accidental data loss
        const staleUnifiedId = await redis.get(indexKey);
        // Restrict reclaim to whitelisted OG users only (not any user with highest_level_ever > 0)
        const isOgUserReclaiming = legacyData && legacyData.is_whitelisted_og;
        if (staleUnifiedId && isOgUserReclaiming) {
            console.log(`[V2] Whitelisted OG user reclaiming account - cleaning up old data for discord_id ${discordId} (unified_id: ${staleUnifiedId})`);

            // Get old user data to find their display_name
            const oldUserData = await redis.get(`user:${staleUnifiedId}`);
            if (oldUserData) {
                const oldUser = typeof oldUserData === 'string' ? JSON.parse(oldUserData) : oldUserData;

                // Delete old user record
                await redis.del(`user:${staleUnifiedId}`);

                // Delete old display_name_index
                if (oldUser.display_name) {
                    await redis.del(`display_name_index:${oldUser.display_name.toLowerCase()}`);
                }

                // Delete old patreon_index if they had one
                if (oldUser.patreon_id) {
                    await redis.del(`patreon_index:${oldUser.patreon_id}`);
                }

                // Remove from leaderboard
                if (oldUser.current_season) {
                    await redis.zrem(`leaderboard:${oldUser.current_season}`, staleUnifiedId);
                }

                // Delete season0 keys for this account
                await redis.del(`season0:discord:${discordId}`);
                if (oldUser.patreon_id) {
                    await redis.del(`season0:patreon:${oldUser.patreon_id}`);
                }

                console.log(`[V2] Deleted old user: ${staleUnifiedId} (${oldUser.display_name})`);
            }

            // Delete the discord_index (will be recreated)
            await redis.del(indexKey);
        }

        // Acquire a per-name lock to prevent race conditions on display name uniqueness
        const nameLockKey = `name_lock:${trimmedName.toLowerCase()}`;
        const nameLockAcquired = await redis.set(nameLockKey, '1', { nx: true, ex: 10 });
        if (!nameLockAcquired) {
            return res.status(409).json({ error: 'display_name registration in progress, please try again' });
        }

        try {
        // Check if display_name is already taken by a DIFFERENT user
        const nameIndexKey = `display_name_index:${trimmedName.toLowerCase()}`;
        const existingNameUser = await redis.get(nameIndexKey);
        if (existingNameUser) {
            // Check if this is a leftover index (user record doesn't exist)
            const existingUserData = await redis.get(`user:${existingNameUser}`);
            if (existingUserData) {
                // Name exists - but check if this is an OG user reclaiming their exact legacy name
                const isReclaimingLegacyName = isOgUserReclaiming &&
                    legacyData?.display_name &&
                    legacyData.display_name.toLowerCase() === trimmedName.toLowerCase();

                if (isReclaimingLegacyName) {
                    // OG user is reclaiming their exact legacy name - delete the existing user
                    console.log(`[V2] OG user reclaiming legacy name "${trimmedName}" - deleting existing user ${existingNameUser}`);
                    const oldUser = typeof existingUserData === 'string' ? JSON.parse(existingUserData) : existingUserData;

                    // Delete existing user completely
                    await redis.del(`user:${existingNameUser}`);
                    await redis.del(nameIndexKey);
                    if (oldUser.patreon_id) await redis.del(`patreon_index:${oldUser.patreon_id}`);
                    if (oldUser.discord_id) await redis.del(`discord_index:${oldUser.discord_id}`);
                    if (oldUser.current_season) await redis.zrem(`leaderboard:${oldUser.current_season}`, existingNameUser);

                    console.log(`[V2] Deleted existing user ${existingNameUser} (${oldUser.display_name}) to allow OG reclaim`);
                } else {
                    // Name is genuinely taken by another user and this isn't a legacy reclaim
                    return res.status(409).json({ error: 'display_name already taken' });
                }
            } else {
                // Orphaned index - clean it up
                console.log(`[V2] Cleaning up orphaned display_name_index for "${trimmedName}"`);
                await redis.del(nameIndexKey);
            }
        }

        const unifiedId = generateUnifiedId();
        const currentSeason = getCurrentSeason();

        // Build new user object
        const newUser = {
            unified_id: unifiedId,
            display_name: trimmedName,
            patreon_id: null,
            discord_id: discordId,
            email_hash: hashEmail(discordUser.email) || null,

            // Seasonal data (starts fresh)
            current_season: currentSeason,
            xp: 0,
            level: 1,

            // Permanent data (migrating users start fresh, only keep achievements + OG status)
            highest_level_ever: 0,
            unlocks: calculateUnlocks(0),
            achievements: legacyData?.achievements || [],
            all_time_stats: {
                total_flashes: 0,
                total_bubbles_popped: 0,
                total_video_minutes: 0,
                total_lock_cards_completed: 0,
                seasons_completed: 0
            },

            // All-time XP tracking (server-computed, never trust client)
            total_xp_earned: 0,

            // Legacy status — check both season0 key AND V1 discord IDs set
            is_season0_og: isV1Og,

            // Patreon status (starts empty)
            patreon_tier: legacyData?.patreon_tier || 0,
            patreon_is_active: legacyData?.patreon_is_active || false,
            patreon_is_whitelisted: legacyData?.patreon_is_whitelisted || false,

            // Skill tree (level 1 = 1 sparkle point)
            skill_points: 1,
            unlocked_skills: [],

            // Settings
            allow_discord_dm: legacyData?.allow_discord_dm || false,
            show_online_status: legacyData?.show_online_status !== false,

            // Metadata
            display_name_set_at: new Date().toISOString(),
            created_at: new Date().toISOString(),
            updated_at: new Date().toISOString(),
            last_seen: new Date().toISOString()
        };

        // Issue auth token for new user
        const { token: newAuthToken, hash: newAuthTokenHash } = generateAuthToken();
        newUser.auth_token_hash = newAuthTokenHash;

        // Save user and indexes
        await redis.set(`user:${unifiedId}`, JSON.stringify(newUser));
        await redis.set(indexKey, unifiedId);
        await redis.set(nameIndexKey, unifiedId);
        if (newUser.email_hash) {
            await redis.set(`email_index:${newUser.email_hash}`, unifiedId);
        }

        // Add to leaderboard sorted set
        await redis.zadd(`leaderboard:${currentSeason}`, { score: 0, member: unifiedId });
        try { await redis.zadd('leaderboard:all-time', { score: 0, member: unifiedId }); } catch (e) { console.log(`[WARN] All-time leaderboard update failed for ${unifiedId}:`, e.message); }

        console.log(`[V2] Created new user: ${unifiedId} (${trimmedName}) via Discord, OG=${isV1Og}`);

        res.json({
            success: true,
            is_new_user: true,
            needs_registration: false,
            is_legacy_user: !!legacyData,
            unified_id: unifiedId,
            auth_token: newAuthToken,
            user: {
                unified_id: newUser.unified_id,
                display_name: newUser.display_name,
                discord_id: newUser.discord_id,
                patreon_id: newUser.patreon_id,
                level: newUser.level,
                xp: newUser.xp,
                current_season: newUser.current_season,
                highest_level_ever: newUser.highest_level_ever,
                unlocks: newUser.unlocks,
                achievements: newUser.achievements,
                is_season0_og: newUser.is_season0_og,
                patreon_tier: newUser.patreon_tier,
                patreon_is_active: newUser.patreon_is_active
            },
            discord: {
                id: discordUser.id,
                username: discordUser.username,
                global_name: discordUser.global_name,
                avatar: discordUser.avatar
            }
        });
        } finally {
            // Release display name lock
            await redis.del(nameLockKey).catch(() => {});
        }
    } catch (error) {
        console.error('V2 Discord auth error:', error.message);
        if (error.message === 'UNAUTHORIZED') {
            return res.status(401).json({ error: 'Discord token expired or invalid' });
        }
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/auth/patreon
 * Patreon authentication for v5.5+ (monthly seasons system)
 * Body: { access_token: string, display_name?: string }
 */
app.post('/v2/auth/patreon', async (req, res) => {
    try {
        const { access_token, display_name } = req.body;

        if (!access_token) {
            return res.status(400).json({ error: 'access_token required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Get Patreon user info
        const identity = await getPatreonIdentity(access_token);
        const patreonId = identity.data.id;
        const patronName = identity.data.attributes?.full_name || 'Unknown';
        const patronEmail = identity.data.attributes?.email;

        // Check subscription tier
        const tierInfo = getTierFromMemberships(identity.included || []);

        // Check if user already exists via index
        // Check both V2 key (patreon_index:) and V1 key (patreon_user:) for backward compatibility
        const indexKey = `patreon_index:${patreonId}`;
        let existingUnifiedId = await redis.get(indexKey);

        if (!existingUnifiedId) {
            // Fallback: check V1 index key used by older registration flow
            const v1IndexKey = `${PATREON_USER_INDEX}${patreonId}`;
            existingUnifiedId = await redis.get(v1IndexKey);
            if (existingUnifiedId) {
                // Migrate: create the V2 index key so future lookups are fast
                await redis.set(indexKey, existingUnifiedId);
                console.log(`[V2] Migrated patreon index for ${patreonId}: ${v1IndexKey} -> ${indexKey}`);
            }
        }

        // Fallback: if index is missing, scan user:* keys to find orphaned account
        // This prevents data loss when patreon_index key is accidentally deleted
        // Wrapped in a 3s timeout to prevent Vercel function timeouts on large datasets
        if (!existingUnifiedId) {
            console.log(`[V2] patreon_index missing for ${patreonId}, scanning for orphaned account...`);
            try {
                const scanResult = await Promise.race([
                    (async () => {
                        let cursor = 0;
                        do {
                            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
                            cursor = result[0];
                            for (const key of result[1]) {
                                try {
                                    const userData = await redis.get(key);
                                    if (!userData) continue;
                                    const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
                                    if (user.patreon_id === patreonId) {
                                        existingUnifiedId = user.unified_id || key.replace('user:', '');
                                        // Repair the missing index so future lookups are fast
                                        await redis.set(indexKey, existingUnifiedId);
                                        console.log(`[V2] RECOVERED orphaned account for patreon ${patreonId}: ${existingUnifiedId} (${user.display_name}). Repaired patreon_index.`);
                                        return 'found';
                                    }
                                } catch (e) {
                                    // Skip malformed records
                                }
                            }
                        } while (cursor !== 0);
                        return 'not_found';
                    })(),
                    new Promise((resolve) => setTimeout(() => resolve('timeout'), 3000))
                ]);
                if (scanResult === 'timeout') {
                    console.warn(`[V2] Orphan scan timed out for patreon ${patreonId} — continuing as new user`);
                }
            } catch (e) {
                console.error(`[V2] Orphan scan error for patreon ${patreonId}:`, e);
            }
        }

        // Email-based auto-link HINT: if user not found by patreon_id, check if their Patreon email
        // matches an existing account. We do NOT auto-link transparently — return a hint instead
        // so the client can prompt the user to confirm. This prevents account takeover via email.
        let autoLinkHint = null;
        if (!existingUnifiedId && patronEmail) {
            const emailLookup = await lookupUserByEmail(patronEmail);
            if (emailLookup && emailLookup.user) {
                autoLinkHint = {
                    can_auto_link: true,
                    auto_link_unified_id: emailLookup.unified_id,
                    auto_link_display_name: emailLookup.user.display_name || null
                };
                console.log(`[V2] Email match found for Patreon ${patreonId} → ${emailLookup.unified_id} (hint only, not auto-linked)`);
            }
        }

        if (existingUnifiedId) {
            // Existing user - update their Patreon status and return data
            const userData = await redis.get(`user:${existingUnifiedId}`);
            if (userData) {
                const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

                // Update Patreon status + auto-fix missing fields from V1 migration
                const existingWhitelisted = isWhitelisted(patronEmail, patronName, user.display_name);
                user.patreon_tier = existingWhitelisted ? Math.max(tierInfo.tier, 2) : tierInfo.tier;
                user.patreon_is_active = tierInfo.is_active;
                user.patreon_is_whitelisted = existingWhitelisted;
                user.last_seen = new Date().toISOString();
                user.updated_at = new Date().toISOString();
                const currentSeason = getCurrentSeason();
                if (!user.current_season) {
                    user.current_season = currentSeason;
                    console.log(`[V2] Auto-fixed missing current_season for ${existingUnifiedId}`);
                }
                if (!user.highest_level_ever) {
                    user.highest_level_ever = user.level || 1;
                }
                if (!user.unlocks) {
                    user.unlocks = calculateUnlocks(user.highest_level_ever || 0);
                }

                // Issue auth token
                const { token: authToken, hash: authTokenHash } = generateAuthToken();
                user.auth_token_hash = authTokenHash;

                await redis.set(`user:${existingUnifiedId}`, JSON.stringify(user));

                return res.json({
                    success: true,
                    is_new_user: false,
                    needs_registration: false,
                    unified_id: existingUnifiedId,
                    auth_token: authToken,
                    user: {
                        unified_id: user.unified_id,
                        display_name: user.display_name,
                        discord_id: user.discord_id,
                        patreon_id: user.patreon_id,
                        level: user.level,
                        xp: user.xp,
                        current_season: user.current_season,
                        highest_level_ever: user.highest_level_ever,
                        unlocks: user.unlocks,
                        achievements: user.achievements,
                        is_season0_og: user.is_season0_og,
                        patreon_tier: user.patreon_tier,
                        patreon_is_active: user.patreon_is_active
                    },
                    patreon: {
                        id: patreonId,
                        tier: tierInfo.tier,
                        is_active: tierInfo.is_active
                    }
                });
            }
        }

        // User doesn't exist yet
        // Check Season 0 legacy data
        let legacyData = await lookupSeason0('patreon', patreonId);

        // Fallback: If not in Season 0 capture but IS whitelisted, treat as OG supporter
        // (They were Patreon members before v5.5 but may not have logged into the app)
        const whitelistedForLegacy = isWhitelisted(patronEmail, patronName, null);
        console.log(`[V2 DEBUG] isWhitelisted result: ${whitelistedForLegacy}`);
        if (!legacyData && whitelistedForLegacy) {
            console.log(`[V2] Whitelisted user not in Season 0 - treating as OG`);

            // Try to find their old profile from existing data to restore their display_name
            let oldDisplayName = null;
            let oldHighestLevel = 0;
            let oldAchievements = [];

            // Search profile:* keys for matching email or patron_name
            try {
                let cursor = "0";
                outer: do {
                    const result = await redis.scan(cursor, { match: 'profile:*', count: 100 });
                    cursor = String(result[0]);
                    for (const key of (result[1] || [])) {
                        const profileData = await redis.get(key);
                        if (profileData) {
                            const profile = typeof profileData === 'string' ? JSON.parse(profileData) : profileData;
                            // Match by email (case insensitive) or patron_name (case insensitive)
                            const profileEmailHash = profile.email_hash || (profile.email ? hashEmail(profile.email) : null);
                            const profilePatronName = (profile.patron_name || '').toLowerCase();
                            const searchEmailHash = patronEmail ? hashEmail(patronEmail) : null;
                            const searchName = (patronName || '').toLowerCase();

                            if ((searchEmailHash && profileEmailHash === searchEmailHash) ||
                                (searchName && profilePatronName === searchName)) {
                                oldDisplayName = profile.display_name || null;
                                oldHighestLevel = profile.level || 0;
                                oldAchievements = profile.achievements || [];
                                console.log(`[V2] Found old profile: display_name="${oldDisplayName}", level=${oldHighestLevel}`);
                                break outer;
                            }
                        }
                    }
                } while (cursor !== "0");
            } catch (scanErr) {
                console.error('[V2] Error scanning for old profile:', scanErr.message);
            }

            legacyData = {
                display_name: oldDisplayName,  // Restored from old profile if found
                highest_level_ever: oldHighestLevel,
                achievements: oldAchievements,
                unlocks: calculateUnlocks(oldHighestLevel),
                is_whitelisted_og: true  // Flag that this is from whitelist, not capture
            };
        }

        // If no display_name provided, just return registration status
        if (!display_name) {
            const response = {
                success: true,
                is_new_user: true,
                needs_registration: true,
                is_legacy_user: !!legacyData,
                legacy_data: legacyData ? {
                    display_name: legacyData.display_name,
                    highest_level_ever: legacyData.highest_level_ever,
                    achievements_count: legacyData.achievements?.length || 0,
                    unlocks: legacyData.unlocks
                } : null,
                patreon: {
                    id: patreonId,
                    tier: tierInfo.tier,
                    is_active: tierInfo.is_active
                }
            };
            // Include auto-link hint if email matched an existing account
            if (autoLinkHint) {
                response.can_auto_link = autoLinkHint.can_auto_link;
                response.auto_link_unified_id = autoLinkHint.auto_link_unified_id;
                response.auto_link_display_name = autoLinkHint.auto_link_display_name;
            }
            return res.json(response);
        }

        // Create new user with display_name
        const trimmedName = display_name.trim();
        if (!trimmedName) {
            return res.status(400).json({ error: 'display_name cannot be empty' });
        }
        if (!isValidDisplayNameChars(trimmedName)) {
            return res.status(400).json({ error: 'display_name contains invalid characters' });
        }
        if (trimmedName.length < 3 || trimmedName.length > 20) {
            return res.status(400).json({ error: 'display_name must be 3-20 characters' });
        }

        // CLEANUP: Delete any old user data for this patreon_id before creating new account
        // This ONLY runs for whitelisted/OG users reclaiming their account - not for regular users
        // This protects existing users from accidental data loss
        const staleUnifiedId = await redis.get(indexKey);
        // Restrict reclaim to whitelisted OG users only (not any user with highest_level_ever > 0)
        const isOgUserReclaiming = legacyData && legacyData.is_whitelisted_og;
        if (staleUnifiedId && isOgUserReclaiming) {
            console.log(`[V2] Whitelisted OG user reclaiming account - cleaning up old data for patreon_id ${patreonId} (unified_id: ${staleUnifiedId})`);

            // Get old user data to find their display_name
            const oldUserData = await redis.get(`user:${staleUnifiedId}`);
            if (oldUserData) {
                const oldUser = typeof oldUserData === 'string' ? JSON.parse(oldUserData) : oldUserData;

                // Delete old user record
                await redis.del(`user:${staleUnifiedId}`);

                // Delete old display_name_index
                if (oldUser.display_name) {
                    await redis.del(`display_name_index:${oldUser.display_name.toLowerCase()}`);
                }

                // Delete old discord_index if they had one
                if (oldUser.discord_id) {
                    await redis.del(`discord_index:${oldUser.discord_id}`);
                }

                // Remove from leaderboard
                if (oldUser.current_season) {
                    await redis.zrem(`leaderboard:${oldUser.current_season}`, staleUnifiedId);
                }

                // Delete season0 keys for this account
                await redis.del(`season0:patreon:${patreonId}`);
                if (oldUser.discord_id) {
                    await redis.del(`season0:discord:${oldUser.discord_id}`);
                }

                console.log(`[V2] Deleted old user: ${staleUnifiedId} (${oldUser.display_name})`);
            }

            // Delete the patreon_index (will be recreated)
            await redis.del(indexKey);
        }

        // Acquire a per-name lock to prevent race conditions on display name uniqueness
        const nameLockKey = `name_lock:${trimmedName.toLowerCase()}`;
        const nameLockAcquired = await redis.set(nameLockKey, '1', { nx: true, ex: 10 });
        if (!nameLockAcquired) {
            return res.status(409).json({ error: 'display_name registration in progress, please try again' });
        }

        try {
        // Check if display_name is already taken by a DIFFERENT user
        const nameIndexKey = `display_name_index:${trimmedName.toLowerCase()}`;
        const existingNameUser = await redis.get(nameIndexKey);
        if (existingNameUser) {
            // Check if this is a leftover index (user record doesn't exist)
            const existingUserData = await redis.get(`user:${existingNameUser}`);
            if (existingUserData) {
                // Name exists - check if this is a whitelisted OG user who can reclaim
                // Whitelisted users can claim ANY taken name (whitelist = proof of OG status)
                // This handles the case where profile:* keys were deleted so legacyData.display_name is null
                // Only whitelisted users can reclaim names (not just any user with highest_level_ever > 0)
                const canReclaimAsOg = whitelistedForLegacy;

                if (canReclaimAsOg) {
                    // OG/whitelisted user wants this name — rename the existing user instead of deleting
                    console.log(`[V2] Whitelisted OG user claiming "${trimmedName}" - renaming existing user ${existingNameUser}`);
                    const oldUser = typeof existingUserData === 'string' ? JSON.parse(existingUserData) : existingUserData;

                    // Rename the existing user's display name so the OG can have it
                    // R8/M25: Add random salt to ensure uniqueness
                    const conflictSuffix = '_' + Date.now().toString(36) + crypto.randomBytes(2).toString('hex');
                    oldUser.display_name = ((oldUser.display_name || 'user') + conflictSuffix).slice(0, 30);
                    oldUser.display_name_conflict = true;
                    oldUser.display_name_conflict_at = new Date().toISOString();
                    await redis.set(`user:${existingNameUser}`, JSON.stringify(oldUser));

                    // Update the name index to point to the renamed user
                    const newNameKey = `display_name_index:${oldUser.display_name.toLowerCase()}`;
                    await redis.set(newNameKey, existingNameUser);

                    // Remove the old name index (will be recreated for the OG user below)
                    await redis.del(nameIndexKey);

                    console.log(`[V2] Renamed existing user ${existingNameUser} to "${oldUser.display_name}" to allow OG claim`);
                } else {
                    // Name is genuinely taken by another user and this isn't an OG
                    return res.status(409).json({ error: 'display_name already taken' });
                }
            } else {
                // Orphaned index - clean it up
                console.log(`[V2] Cleaning up orphaned display_name_index for "${trimmedName}"`);
                await redis.del(nameIndexKey);
            }
        }

        const unifiedId = generateUnifiedId();
        const currentSeason = getCurrentSeason();

        // Check whitelist
        const whitelisted = isWhitelisted(patronEmail, patronName, null);

        // Build new user object
        const newUser = {
            unified_id: unifiedId,
            display_name: trimmedName,
            patreon_id: patreonId,
            discord_id: null,
            email_hash: hashEmail(patronEmail) || null,

            // Seasonal data (starts fresh)
            current_season: currentSeason,
            xp: 0,
            level: 1,

            // Permanent data (migrating users start fresh, only keep achievements + OG status)
            highest_level_ever: 0,
            unlocks: calculateUnlocks(0),
            achievements: legacyData?.achievements || [],
            all_time_stats: {
                total_flashes: 0,
                total_bubbles_popped: 0,
                total_video_minutes: 0,
                total_lock_cards_completed: 0,
                seasons_completed: 0
            },

            // All-time XP tracking (server-computed, never trust client)
            total_xp_earned: 0,

            // Legacy status
            is_season0_og: !!legacyData,

            // Patreon status
            patreon_tier: whitelisted ? Math.max(tierInfo.tier, 2) : tierInfo.tier,
            patreon_is_active: tierInfo.is_active,
            patreon_is_whitelisted: whitelisted,

            // Skill tree (level 1 = 1 sparkle point)
            skill_points: 1,
            unlocked_skills: [],

            // Settings
            allow_discord_dm: legacyData?.allow_discord_dm || false,
            show_online_status: legacyData?.show_online_status !== false,

            // Metadata
            display_name_set_at: new Date().toISOString(),
            created_at: new Date().toISOString(),
            updated_at: new Date().toISOString(),
            last_seen: new Date().toISOString()
        };

        // Issue auth token for new user
        const { token: newAuthToken, hash: newAuthTokenHash } = generateAuthToken();
        newUser.auth_token_hash = newAuthTokenHash;

        // Save user and indexes
        await redis.set(`user:${unifiedId}`, JSON.stringify(newUser));
        await redis.set(indexKey, unifiedId);
        await redis.set(nameIndexKey, unifiedId);
        if (newUser.email_hash) {
            await redis.set(`email_index:${newUser.email_hash}`, unifiedId);
        }

        // Add to leaderboard sorted set
        await redis.zadd(`leaderboard:${currentSeason}`, { score: 0, member: unifiedId });
        try { await redis.zadd('leaderboard:all-time', { score: 0, member: unifiedId }); } catch (e) { console.log(`[WARN] All-time leaderboard update failed for ${unifiedId}:`, e.message); }

        console.log(`[V2] Created new user: ${unifiedId} (${trimmedName}) via Patreon, OG=${!!legacyData}`);

        res.json({
            success: true,
            is_new_user: true,
            needs_registration: false,
            is_legacy_user: !!legacyData,
            unified_id: unifiedId,
            auth_token: newAuthToken,
            user: {
                unified_id: newUser.unified_id,
                display_name: newUser.display_name,
                discord_id: newUser.discord_id,
                patreon_id: newUser.patreon_id,
                level: newUser.level,
                xp: newUser.xp,
                current_season: newUser.current_season,
                highest_level_ever: newUser.highest_level_ever,
                unlocks: newUser.unlocks,
                achievements: newUser.achievements,
                is_season0_og: newUser.is_season0_og,
                patreon_tier: newUser.patreon_tier,
                patreon_is_active: newUser.patreon_is_active
            },
            patreon: {
                id: patreonId,
                tier: tierInfo.tier,
                is_active: tierInfo.is_active
            }
        });
        } finally {
            // Release display name lock
            await redis.del(nameLockKey).catch(() => {});
        }
    } catch (error) {
        console.error('V2 Patreon auth error:', error.message);
        if (error.message === 'UNAUTHORIZED') {
            return res.status(401).json({ error: 'Patreon token expired or invalid' });
        }
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/auth/register
 * Register a new account with invite code + display name + password.
 * Body: { invite_code: string, display_name: string, password: string }
 */
app.post('/v2/auth/register', async (req, res) => {
    let outerCodeLockKey = null;
    try {
        const { invite_code, display_name, password } = req.body;

        // typeof checks on all fields (audit S8)
        if (typeof invite_code !== 'string' || typeof display_name !== 'string' || typeof password !== 'string') {
            return res.status(400).json({ error: 'invite_code, display_name, and password required' });
        }
        if (!invite_code || !display_name || !password) {
            return res.status(400).json({ error: 'invite_code, display_name, and password required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Validate password
        if (password.length < 8) {
            return res.status(400).json({ error: 'Password must be at least 8 characters' });
        }
        if (password.length > 128) {
            return res.status(400).json({ error: 'Password too long' });
        }

        // Rate limit: 3 registrations per hour per IP (atomic init — audit S4, S6/M15)
        const ip = req.ip || 'unknown';
        const regLimitKey = `register_limit:${ip}`;
        try {
            await redis.set(regLimitKey, 0, { nx: true, ex: 3600 });
            const regCount = await redis.incr(regLimitKey);
            if (regCount > 3) {
                return res.status(429).json({ error: 'Too many registration attempts. Try again later.' });
            }
        } catch (e) {
            // Redis error — deny (fail-closed)
            return res.status(429).json({ error: 'Rate limit check failed. Try again later.' });
        }

        // Validate invite code format (R8/M3 — prevent arbitrary Redis key construction)
        const normalizedCode = invite_code.toUpperCase().trim();
        if (!/^[A-Z0-9]{6,12}$/.test(normalizedCode)) {
            return res.status(400).json({ error: 'Invalid invite code format' });
        }
        const codeKey = `invite_code:${normalizedCode}`;
        const codeData = await redis.get(codeKey);
        if (!codeData) {
            return res.status(400).json({ error: 'Invalid or expired invite code' });
        }
        const code = typeof codeData === 'string' ? JSON.parse(codeData) : codeData;
        if (code.used) {
            return res.status(400).json({ error: 'This invite code has already been used' });
        }
        if (code.expires_at && new Date(code.expires_at) < new Date()) {
            return res.status(400).json({ error: 'Invalid or expired invite code' });
        }

        // Acquire atomic lock on invite code to prevent TOCTOU race condition
        // (two concurrent requests both passing code.used check before either marks it used)
        const codeLockKey = `invite_code_lock:${normalizedCode}`;
        outerCodeLockKey = codeLockKey;
        const codeLockAcquired = await redis.set(codeLockKey, '1', { nx: true, ex: 15 });
        if (!codeLockAcquired) {
            return res.status(409).json({ error: 'Invite code registration in progress. Please try again.' });
        }

        // Validate display name (R8/M22: release code lock on early returns)
        const trimmedName = display_name.trim();
        if (!trimmedName || trimmedName.length < 3 || trimmedName.length > 20) {
            await redis.del(codeLockKey).catch(() => {});
            return res.status(400).json({ error: 'Display name must be 3-20 characters' });
        }
        if (!isValidDisplayNameChars(trimmedName)) {
            await redis.del(codeLockKey).catch(() => {});
            return res.status(400).json({ error: 'Display name contains invalid characters' });
        }

        // Check display name uniqueness with lock
        const nameIndexKey = `display_name_index:${trimmedName.toLowerCase()}`;
        const nameLockKey = `name_lock:${trimmedName.toLowerCase()}`;
        const nameLockAcquired = await redis.set(nameLockKey, '1', { nx: true, ex: 10 });
        if (!nameLockAcquired) {
            await redis.del(codeLockKey).catch(() => {});
            return res.status(409).json({ error: 'display_name is being registered by another user' });
        }

        try {
            // R8/M1: Re-read invite code inside lock to prevent stale data
            const freshCodeData = await redis.get(codeKey);
            if (!freshCodeData) {
                return res.status(400).json({ error: 'Invalid or expired invite code' });
            }
            const freshCode = typeof freshCodeData === 'string' ? JSON.parse(freshCodeData) : freshCodeData;
            if (freshCode.used) {
                return res.status(400).json({ error: 'This invite code has already been used' });
            }
            if (freshCode.expires_at && new Date(freshCode.expires_at) < new Date()) {
                return res.status(400).json({ error: 'Invalid or expired invite code' });
            }

            const existingNameUser = await redis.get(nameIndexKey);
            if (existingNameUser) {
                // Verify the index points to a real user
                const existingUserData = await redis.get(`user:${existingNameUser}`);
                if (existingUserData) {
                    return res.status(409).json({ error: 'display_name already taken' });
                } else {
                    // Orphaned index — clean up
                    await redis.del(nameIndexKey);
                }
            }

            // Hash password
            const passwordHash = await hashPassword(password);

            // Create user
            const unifiedId = generateUnifiedId();
            const currentSeason = getCurrentSeason();

            const newUser = {
                unified_id: unifiedId,
                display_name: trimmedName,
                patreon_id: null,
                discord_id: null,
                password_hash: passwordHash,
                auth_method: 'invite',

                current_season: currentSeason,
                xp: 0,
                level: 1,

                highest_level_ever: 0,
                unlocks: calculateUnlocks(0),
                achievements: [],
                all_time_stats: {
                    total_flashes: 0,
                    total_bubbles_popped: 0,
                    total_video_minutes: 0,
                    total_lock_cards_completed: 0,
                    seasons_completed: 0
                },

                // All-time XP tracking (server-computed, never trust client)
                total_xp_earned: 0,

                is_season0_og: false,

                patreon_tier: 0,
                patreon_is_active: false,
                patreon_is_whitelisted: false,

                skill_points: 1,
                unlocked_skills: [],

                allow_discord_dm: false,
                show_online_status: true,

                display_name_set_at: new Date().toISOString(),
                created_at: new Date().toISOString(),
                updated_at: new Date().toISOString(),
                last_seen: new Date().toISOString()
            };

            // Issue auth token
            const { token: authToken, hash: authTokenHash } = generateAuthToken();
            newUser.auth_token_hash = authTokenHash;

            // Save user and indexes
            await redis.set(`user:${unifiedId}`, JSON.stringify(newUser));
            await redis.set(nameIndexKey, unifiedId);

            // Mark invite code as used (use freshCode from inside-lock re-read)
            freshCode.used = true;
            freshCode.used_by = unifiedId;
            freshCode.used_at = new Date().toISOString();
            await redis.set(codeKey, JSON.stringify(freshCode));

            // Add to leaderboard
            await redis.zadd(`leaderboard:${currentSeason}`, { score: 0, member: unifiedId });
            try { await redis.zadd('leaderboard:all-time', { score: 0, member: unifiedId }); } catch (e) { console.log(`[WARN] All-time leaderboard update failed for ${unifiedId}:`, e.message); }

            // Check whitelist by display name
            const whitelisted = isWhitelisted(null, null, trimmedName);
            if (whitelisted) {
                newUser.patreon_is_whitelisted = true;
                await redis.set(`user:${unifiedId}`, JSON.stringify(newUser));
            }

            await incrementTotalUserCount();
            console.log(`[V2] Created new invite user: ${unifiedId} (${trimmedName})`);

            res.json({
                success: true,
                is_new_user: true,
                needs_registration: false,
                auth_token: authToken,
                user: {
                    unified_id: newUser.unified_id,
                    display_name: newUser.display_name,
                    discord_id: newUser.discord_id,
                    patreon_id: newUser.patreon_id,
                    level: newUser.level,
                    xp: newUser.xp,
                    current_season: newUser.current_season,
                    highest_level_ever: newUser.highest_level_ever,
                    unlocks: newUser.unlocks,
                    achievements: newUser.achievements,
                    is_season0_og: newUser.is_season0_og,
                    patreon_tier: newUser.patreon_tier,
                    patreon_is_active: newUser.patreon_is_active
                }
            });
        } finally {
            await redis.del(nameLockKey).catch(() => {});
            await redis.del(codeLockKey).catch(() => {});
        }
    } catch (error) {
        // Release code lock on any error (outerCodeLockKey may be null if error was before lock)
        if (outerCodeLockKey) {
            await redis.del(outerCodeLockKey).catch(() => {});
        }
        console.error('V2 invite register error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/auth/login
 * Login with display name + password.
 * Body: { display_name: string, password: string }
 */
app.post('/v2/auth/login', async (req, res) => {
    try {
        const { display_name, password } = req.body;

        // typeof checks (audit S8)
        if (typeof display_name !== 'string' || typeof password !== 'string') {
            return res.status(400).json({ error: 'display_name and password required' });
        }
        if (!display_name || !password) {
            return res.status(400).json({ error: 'display_name and password required' });
        }
        if (display_name.length > 30 || password.length > 128) {
            return res.status(400).json({ error: 'Invalid credentials' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const ip = req.ip || 'unknown';

        // Per-IP rate limit: 5 attempts per 5 min (atomic init — audit S5, S6/M15)
        const failKey = `login_fail:${ip}`;
        try {
            await redis.set(failKey, 0, { nx: true, ex: 300 });
            const failCount = await redis.incr(failKey);
            if (failCount > 5) {
                return res.status(429).json({ error: 'Too many failed login attempts. Try again in 5 minutes.' });
            }
        } catch (e) {
            return res.status(429).json({ error: 'Rate limit check failed. Try again later.' });
        }

        // Look up user by display name
        const nameKey = `display_name_index:${display_name.trim().toLowerCase()}`;
        const unifiedId = await redis.get(nameKey);

        if (!unifiedId) {
            // Perform dummy scrypt to prevent timing-based username enumeration
            await verifyPassword('dummy', '0123456789abcdef0123456789abcdef:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef').catch(() => {});
            return res.status(401).json({ error: 'Invalid credentials' });
        }

        // Per-account rate limit: 10 attempts per hour (audit S6 — prevents distributed brute-force)
        // Check count first (read-only), only increment on actual failure
        const userFailKey = `login_fail_user:${unifiedId}`;
        try {
            const currentCount = await redis.get(userFailKey);
            if (currentCount && parseInt(currentCount) > 10) {
                return res.status(429).json({ error: 'Account temporarily locked. Try again later.' });
            }
        } catch (e) {
            return res.status(429).json({ error: 'Rate limit check failed. Try again later.' });
        }

        // Load user
        const userData = await redis.get(`user:${unifiedId}`);
        if (!userData) {
            return res.status(401).json({ error: 'Invalid credentials' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Verify password (skip non-password users)
        if (!user.password_hash) {
            return res.status(401).json({ error: 'Invalid credentials' });
        }

        const passwordValid = await verifyPassword(password, user.password_hash);
        if (!passwordValid) {
            // Increment per-account failure counter only on actual wrong password
            try {
                const newCount = await redis.incr(userFailKey);
                if (newCount === 1) await redis.expire(userFailKey, 3600);
            } catch (e) { /* non-critical */ }
            return res.status(401).json({ error: 'Invalid credentials' });
        }

        // Login successful — clear fail counters
        try { await redis.del(failKey); } catch (e) { /* non-critical */ }
        try { await redis.del(userFailKey); } catch (e) { /* non-critical */ }

        // Rotate auth token
        const { token: authToken, hash: authTokenHash } = generateAuthToken();
        user.auth_token_hash = authTokenHash;
        user.last_seen = new Date().toISOString();

        // Re-check whitelist status
        const whitelisted = isWhitelisted(null, null, user.display_name);
        user.patreon_is_whitelisted = whitelisted;

        // Ensure whitelisted users have at least tier 2
        if (whitelisted && (!user.patreon_tier || user.patreon_tier < 2)) {
            user.patreon_tier = 2;
        }

        await redis.set(`user:${unifiedId}`, JSON.stringify(user));

        console.log(`[V2] Password login: ${unifiedId}`);

        res.json({
            success: true,
            is_new_user: false,
            needs_registration: false,
            auth_token: authToken,
            user: {
                unified_id: user.unified_id,
                display_name: user.display_name,
                discord_id: user.discord_id,
                patreon_id: user.patreon_id,
                level: user.level,
                xp: user.xp,
                current_season: user.current_season,
                highest_level_ever: user.highest_level_ever,
                unlocks: user.unlocks,
                achievements: user.achievements,
                is_season0_og: user.is_season0_og,
                patreon_tier: user.patreon_tier,
                patreon_is_active: user.patreon_is_active,
                patreon_is_whitelisted: user.patreon_is_whitelisted
            }
        });
    } catch (error) {
        console.error('V2 login error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/auth/link
 * Link a second provider (Discord or Patreon) to an existing account
 * Body: { unified_id: string, provider: 'discord'|'patreon', access_token: string }
 */
app.post('/v2/auth/link', async (req, res) => {
    try {
        const { unified_id, provider, access_token } = req.body;

        if (!unified_id || !provider || !access_token) {
            return res.status(400).json({ error: 'unified_id, provider, and access_token required' });
        }
        if (!['discord', 'patreon'].includes(provider)) {
            return res.status(400).json({ error: 'provider must be discord or patreon' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Get existing user
        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }
        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Validate auth token
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }

        if (provider === 'discord') {
            // Check if already has Discord linked
            if (user.discord_id) {
                return res.status(409).json({ error: 'Discord already linked to this account' });
            }

            // Get Discord user info
            const discordUser = await getDiscordUser(access_token);
            const discordId = discordUser.id;

            // Check if this Discord ID is already linked to another account
            const existingLink = await redis.get(`discord_index:${discordId}`);
            if (existingLink && existingLink !== unified_id) {
                return res.status(409).json({ error: 'Discord account already linked to a different user' });
            }

            // Link Discord to user
            user.discord_id = discordId;
            user.updated_at = new Date().toISOString();

            // Issue new auth token on link
            const { token: linkAuthToken, hash: linkAuthTokenHash } = generateAuthToken();
            user.auth_token_hash = linkAuthTokenHash;

            // Save user and create index
            await redis.set(`user:${unified_id}`, JSON.stringify(user));
            await redis.set(`discord_index:${discordId}`, unified_id);

            console.log(`[V2] Linked Discord to user ${unified_id}`);

            return res.json({
                success: true,
                unified_id,
                linked_provider: 'discord',
                auth_token: linkAuthToken,
                discord: {
                    id: discordUser.id,
                    username: discordUser.username,
                    global_name: discordUser.global_name,
                    avatar: discordUser.avatar
                }
            });
        } else {
            // Patreon linking
            // Check if already has Patreon linked
            if (user.patreon_id) {
                return res.status(409).json({ error: 'Patreon already linked to this account' });
            }

            // Get Patreon user info
            const identity = await getPatreonIdentity(access_token);
            const patreonId = identity.data.id;
            const patronName = identity.data.attributes?.full_name || 'Unknown';
            const patronEmail = identity.data.attributes?.email;

            // Check if this Patreon ID is already linked to another account
            const existingLink = await redis.get(`patreon_index:${patreonId}`);
            if (existingLink && existingLink !== unified_id) {
                return res.status(409).json({ error: 'Patreon account already linked to a different user' });
            }

            // Get tier info
            const tierInfo = getTierFromMemberships(identity.included || []);

            // Check whitelist
            const whitelisted = isWhitelisted(patronEmail, patronName, user.display_name);

            // Link Patreon to user
            user.patreon_id = patreonId;
            user.email_hash = hashEmail(patronEmail) || user.email_hash;
            user.patreon_tier = whitelisted ? Math.max(tierInfo.tier, 2) : tierInfo.tier;
            user.patreon_is_active = tierInfo.is_active;
            user.patreon_is_whitelisted = whitelisted;
            user.updated_at = new Date().toISOString();

            // Issue new auth token on link
            const { token: linkAuthToken2, hash: linkAuthTokenHash2 } = generateAuthToken();
            user.auth_token_hash = linkAuthTokenHash2;

            // Save user and create index
            await redis.set(`user:${unified_id}`, JSON.stringify(user));
            await redis.set(`patreon_index:${patreonId}`, unified_id);

            console.log(`[V2] Linked Patreon to user ${unified_id}`);

            return res.json({
                success: true,
                unified_id,
                linked_provider: 'patreon',
                auth_token: linkAuthToken2,
                patreon: {
                    id: patreonId,
                    tier: tierInfo.tier,
                    is_active: tierInfo.is_active
                }
            });
        }
    } catch (error) {
        console.error('V2 auth link error:', error.message);
        if (error.message === 'UNAUTHORIZED') {
            return res.status(401).json({ error: 'Token expired or invalid' });
        }
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/auth/restore-session
 * Validate a stored unified_id without OAuth. Updates last_seen.
 * Body: { unified_id: string, client_version?: string }
 * Returns same shape as GET /v2/user/profile on success.
 */
app.post('/v2/auth/restore-session', async (req, res) => {
    try {
        const { unified_id, client_version } = req.body;

        if (!unified_id) {
            return res.status(400).json({ success: false, error: 'unified_id required' });
        }
        if (!redis) return res.status(503).json({ success: false, error: 'Redis not available' });

        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ success: false, error: 'user_not_found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Validate auth token — legacy users without hash must re-authenticate via OAuth
        const clientToken = req.headers['x-auth-token'];
        if (!user.auth_token_hash) {
            // Legacy user with no token ever issued — reject and force OAuth re-auth
            console.warn(`[SECURITY] restore-session rejected legacy user ${unified_id} (no auth_token_hash)`);
            return res.status(401).json({ success: false, error: 'legacy_user_reauth_required', message: 'Please log in again with Patreon or Discord.' });
        }
        if (!clientToken) {
            return res.status(401).json({ success: false, error: 'auth_token_required' });
        }
        const clientHash = crypto.createHash('sha256').update(clientToken).digest('hex');
        if (!timingSafeCompare(clientHash, user.auth_token_hash)) {
            return res.status(401).json({ success: false, error: 'invalid_auth_token' });
        }

        // Acquire a per-user lock to prevent concurrent token rotation race conditions.
        // Without this, two simultaneous restore-session calls can overwrite each other's tokens,
        // locking the user out (client holds token A, but Redis has token B from the other request).
        const lockKey = `restore_lock:${unified_id}`;
        const lockAcquired = await redis.set(lockKey, '1', { nx: true, ex: 5 });
        if (!lockAcquired) {
            return res.status(429).json({ success: false, error: 'concurrent_session_restore', message: 'Session restore already in progress. Please try again.' });
        }

        try {
            // Re-read user data inside the lock to get the freshest state
            const freshUserData = await redis.get(`user:${unified_id}`);
            const freshUser = typeof freshUserData === 'string' ? JSON.parse(freshUserData) : freshUserData;
            if (!freshUser) {
                return res.status(404).json({ success: false, error: 'user_not_found' });
            }

            // Re-validate auth token against fresh data (token may have been rotated by concurrent request)
            const freshClientHash = crypto.createHash('sha256').update(clientToken).digest('hex');
            if (!timingSafeCompare(freshClientHash, freshUser.auth_token_hash)) {
                return res.status(401).json({ success: false, error: 'invalid_auth_token', message: 'Token was rotated by another session. Please log in again.' });
            }

            // Update last_seen + auto-fix missing fields
            freshUser.last_seen = new Date().toISOString();
            if (client_version) {
                freshUser.last_client_version = client_version;
            }
            const currentSeason = getCurrentSeason();
            if (!freshUser.current_season) {
                freshUser.current_season = currentSeason;
                console.log(`[V2] Auto-fixed missing current_season for ${unified_id}`);
            }

            // Re-check whitelist on every session restore so Discord-only users get flagged
            if (!freshUser.patreon_is_whitelisted && isWhitelisted(null, null, freshUser.display_name)) {
                freshUser.patreon_is_whitelisted = true;
                freshUser.patreon_tier = Math.max(freshUser.patreon_tier || 0, 2);
                console.log(`Whitelist applied via restore-session for ${unified_id}`);
            }

            // Issue new auth token on session restore (rotates token)
            const { token: restoreAuthToken, hash: restoreAuthTokenHash } = generateAuthToken();
            freshUser.auth_token_hash = restoreAuthTokenHash;

            await redis.set(`user:${unified_id}`, JSON.stringify(freshUser));

            // Privacy: never expose patron_name as display_name
            let safeName = freshUser.display_name || null;
            if (safeName && freshUser.patron_name && !freshUser.display_name_set_at &&
                safeName.toLowerCase().trim() === freshUser.patron_name.toLowerCase().trim()) {
                safeName = null;
            }

            res.json({
                success: true,
                auth_token: restoreAuthToken,
                user: {
                    unified_id: freshUser.unified_id,
                    display_name: safeName,
                    discord_id: freshUser.discord_id,
                    patreon_id: freshUser.patreon_id,
                    level: freshUser.level,
                    xp: freshUser.xp,
                    current_season: freshUser.current_season,
                    highest_level_ever: freshUser.highest_level_ever,
                    unlocks: freshUser.unlocks,
                    achievements: freshUser.achievements,
                    all_time_stats: freshUser.all_time_stats,
                    is_season0_og: freshUser.is_season0_og,
                    patreon_tier: freshUser.patreon_tier,
                    patreon_is_active: freshUser.patreon_is_active,
                    patreon_is_whitelisted: freshUser.patreon_is_whitelisted,
                    allow_discord_dm: freshUser.allow_discord_dm,
                    show_online_status: freshUser.show_online_status,
                    created_at: freshUser.created_at,
                    last_seen: freshUser.last_seen
                }
            });
        } finally {
            // Always release the lock
            await redis.del(lockKey).catch(() => {});
        }
    } catch (error) {
        console.error('V2 auth restore-session error:', error.message);
        res.status(500).json({ success: false, error: 'Session restore failed' });
    }
});

/**
 * GET /v2/user/profile
 * Get current user's profile (requires unified_id)
 * Query: ?unified_id=XXX
 */
app.get('/v2/user/profile', async (req, res) => {
    try {
        const { unified_id } = req.query;

        if (!unified_id) {
            return res.status(400).json({ error: 'unified_id required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Auth token validation
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }
        if (authCheck.legacy) {
            console.log(`[AUTH] ${authCheck.missing ? 'Old client (token not sent)' : 'Legacy user (no token hash)'} for ${unified_id} on ${req.path}`);
        }

        // Privacy: never expose patron_name as display_name
        let safeName = user.display_name || null;
        if (safeName && user.patron_name && !user.display_name_set_at &&
            safeName.toLowerCase().trim() === user.patron_name.toLowerCase().trim()) {
            safeName = null;
        }

        res.json({
            success: true,
            user: {
                unified_id: user.unified_id,
                display_name: safeName,
                discord_id: user.discord_id,
                patreon_id: user.patreon_id,
                level: user.level,
                xp: user.xp,
                current_season: user.current_season,
                highest_level_ever: user.highest_level_ever,
                unlocks: user.unlocks,
                achievements: user.achievements,
                all_time_stats: user.all_time_stats,
                is_season0_og: user.is_season0_og,
                patreon_tier: user.patreon_tier,
                patreon_is_active: user.patreon_is_active,
                patreon_is_whitelisted: user.patreon_is_whitelisted,
                total_conditioning_minutes: user.total_conditioning_minutes || 0,
                companion_progress: user.companion_progress || {},
                allow_discord_dm: user.allow_discord_dm,
                show_online_status: user.show_online_status,
                created_at: user.created_at,
                last_seen: user.last_seen
            }
        });
    } catch (error) {
        console.error('V2 user profile error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/user/delete-account
 * Delete user account and all associated data (GDPR)
 * Body: { unified_id: string, confirmation: 'DELETE' }
 */
app.post('/v2/user/delete-account', async (req, res) => {
    try {
        const { unified_id, confirmation } = req.body;

        if (!unified_id) {
            return res.status(400).json({ error: 'unified_id required' });
        }
        if (confirmation !== 'DELETE') {
            return res.status(400).json({ error: 'Must confirm deletion by setting confirmation to "DELETE"' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Auth token validation
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }
        if (authCheck.legacy) {
            console.log(`[AUTH] ${authCheck.missing ? 'Old client (token not sent)' : 'Legacy user (no token hash)'} for ${unified_id} on ${req.path}`);
        }

        const deleted = [];

        // Delete user record
        await redis.del(`user:${unified_id}`);
        deleted.push(`user:${unified_id}`);

        // Delete V2 indexes
        if (user.discord_id) {
            await redis.del(`discord_index:${user.discord_id}`);
            deleted.push(`discord_index:${user.discord_id}`);
        }
        if (user.patreon_id) {
            await redis.del(`patreon_index:${user.patreon_id}`);
            deleted.push(`patreon_index:${user.patreon_id}`);
        }
        if (user.display_name) {
            await redis.del(`display_name_index:${user.display_name.toLowerCase()}`);
            deleted.push(`display_name_index:${user.display_name.toLowerCase()}`);
        }
        if (user.email_hash) {
            await redis.del(`email_index:${user.email_hash}`);
            deleted.push(`email_index:${user.email_hash}`);
            await redis.del(`email_login_index:${user.email_hash}`);
            deleted.push(`email_login_index:${user.email_hash}`);
        }

        // Remove from ALL leaderboard seasons (not just current)
        // The user might have entries in previous seasons too
        try {
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: 'leaderboard:*', count: 100 });
                cursor = String(result[0]);
                for (const lbKey of (result[1] || [])) {
                    const removed = await redis.zrem(lbKey, unified_id);
                    if (removed) {
                        deleted.push(`${lbKey} (member removed)`);
                    }
                }
            } while (cursor !== "0");
        } catch (lbErr) {
            console.error('[V2 Delete] Error scanning leaderboards:', lbErr.message);
            // Fallback: at least remove from current season
            if (user.current_season) {
                await redis.zrem(`leaderboard:${user.current_season}`, unified_id);
                deleted.push(`leaderboard:${user.current_season} (member removed, fallback)`);
            }
        }

        // Delete Season 0 backups
        if (user.discord_id) {
            await redis.del(`season0:discord:${user.discord_id}`);
            deleted.push(`season0:discord:${user.discord_id}`);
        }
        if (user.patreon_id) {
            await redis.del(`season0:patreon:${user.patreon_id}`);
            deleted.push(`season0:patreon:${user.patreon_id}`);
        }

        // Delete legacy V1 keys (indexes + profiles)
        if (user.discord_id) {
            await redis.del(`discord_user:${user.discord_id}`);
            deleted.push(`discord_user:${user.discord_id}`);
            await redis.del(`discord_profile:${user.discord_id}`);
            deleted.push(`discord_profile:${user.discord_id}`);
        }
        if (user.patreon_id) {
            await redis.del(`patreon_user:${user.patreon_id}`);
            deleted.push(`patreon_user:${user.patreon_id}`);
            await redis.del(`profile:${user.patreon_id}`);
            deleted.push(`profile:${user.patreon_id}`);
        }

        // Remove from season0_discord_ids set (used for V1 Discord user lookup)
        if (user.discord_id) {
            await redis.srem('season0_discord_ids', user.discord_id);
            deleted.push(`season0_discord_ids (member ${user.discord_id} removed)`);
        }

        // Delete cloud settings backup
        await redis.del(`settings_backup:${unified_id}`);
        deleted.push(`settings_backup:${unified_id}`);

        console.log(`[V2] Deleted user account: ${unified_id}, cleaned ${deleted.length} keys`);

        res.json({
            success: true,
            deleted_unified_id: unified_id,
            deleted_keys_count: deleted.length
        });
    } catch (error) {
        console.error('V2 delete account error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/user/export-data
 * Export all user data (GDPR data access request)
 * Body: { unified_id: string }
 * Headers: X-Auth-Token
 */
app.post('/v2/user/export-data', async (req, res) => {
    try {
        const { unified_id } = req.body;

        if (!unified_id) {
            return res.status(400).json({ error: 'unified_id required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Auth token validation
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }
        if (authCheck.legacy) {
            console.log(`[AUTH] ${authCheck.missing ? 'Old client (token not sent)' : 'Legacy user (no token hash)'} for ${unified_id} on ${req.path}`);
        }

        // Whitelist approach: only include explicitly safe fields for user data export
        const exportData = {
            unified_id: user.unified_id,
            display_name: user.display_name,
            level: user.level,
            xp: user.xp,
            current_season: user.current_season,
            highest_level_ever: user.highest_level_ever,
            is_season0_og: user.is_season0_og,
            patreon_tier: user.patreon_tier,
            patreon_is_active: user.patreon_is_active,
            patreon_is_whitelisted: user.patreon_is_whitelisted,
            patreon_id: user.patreon_id || null,
            discord_id: user.discord_id || null,
            // discord_username intentionally excluded — PII
            achievements: user.achievements || [],
            unlocked_skills: user.unlocked_skills || [],
            skill_points: user.skill_points || {},
            stats: user.stats || {},
            all_time_stats: user.all_time_stats || {},
            quest_stats: user.quest_stats || {},
            companion_progress: user.companion_progress || {},
            total_conditioning_minutes: user.total_conditioning_minutes || 0,
            avatar_url: user.avatar_url || null,
            show_online_status: user.show_online_status,
            allow_discord_dm: user.allow_discord_dm,
            unlocks: user.unlocks || {},
            auth_method: user.auth_method || null,
            display_name_set_at: user.display_name_set_at || null,
            created_at: user.created_at,
            updated_at: user.updated_at,
            last_seen: user.last_seen || null
        };

        // Include cloud settings backup if it exists
        const settingsBackup = await redis.get(`settings_backup:${unified_id}`);
        const parsedSettingsBackup = settingsBackup
            ? (typeof settingsBackup === 'string' ? JSON.parse(settingsBackup) : settingsBackup)
            : null;

        console.log(`[V2] Data export for user: ${unified_id}`);

        res.json({
            success: true,
            exported_at: new Date().toISOString(),
            data: exportData,
            settings_backup: parsedSettingsBackup
        });
    } catch (error) {
        console.error('V2 export data error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /admin/delete-user-by-name
 * Admin endpoint to delete a user by display_name (for testing)
 * Body: { admin_token: string, display_name: string }
 */
app.post('/admin/delete-user-by-name', async (req, res) => {
    try {
        const { display_name } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!display_name) {
            return res.status(400).json({ error: 'display_name required' });
        }

        // Look up unified_id by display_name
        const nameIndexKey = `display_name_index:${display_name.toLowerCase()}`;
        const unifiedId = await redis.get(nameIndexKey);
        if (!unifiedId) {
            return res.status(404).json({ error: `No user found with display_name: ${display_name}` });
        }

        // Get user data
        const userData = await redis.get(`user:${unifiedId}`);
        if (!userData) {
            return res.status(404).json({ error: 'User record not found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
        const deleted = [];

        // Delete user record
        await redis.del(`user:${unifiedId}`);
        deleted.push(`user:${unifiedId}`);

        // Delete indexes (V2 + V1 legacy)
        if (user.discord_id) {
            await redis.del(`discord_index:${user.discord_id}`);
            deleted.push(`discord_index:${user.discord_id}`);
            // V1 legacy index
            await redis.del(`discord_id:${user.discord_id}`);
            deleted.push(`discord_id:${user.discord_id}`);
        }
        if (user.patreon_id) {
            await redis.del(`patreon_index:${user.patreon_id}`);
            deleted.push(`patreon_index:${user.patreon_id}`);
            // V1 legacy index
            await redis.del(`patreon_id:${user.patreon_id}`);
            deleted.push(`patreon_id:${user.patreon_id}`);
        }
        if (user.email_hash) {
            await redis.del(`email_index:${user.email_hash}`);
            deleted.push(`email_index:${user.email_hash}`);
        }
        await redis.del(nameIndexKey);
        deleted.push(nameIndexKey);

        // Remove from leaderboard
        if (user.current_season) {
            await redis.zrem(`leaderboard:${user.current_season}`, unifiedId);
            deleted.push(`leaderboard:${user.current_season} (member removed)`);
        }
        try { await redis.zrem('leaderboard:all-time', unifiedId); deleted.push('leaderboard:all-time (member removed)'); } catch (e) { /* best-effort */ }

        // Delete cloud settings backup
        await redis.del(`settings_backup:${unifiedId}`);
        deleted.push(`settings_backup:${unifiedId}`);

        console.log(`[ADMIN] Deleted user by name: ${display_name} (${unifiedId})`);

        res.json({
            success: true,
            deleted_unified_id: unifiedId,
            deleted_display_name: display_name,
            user_data: filterUserForAdmin(user),
            deleted_keys: deleted
        });
    } catch (error) {
        console.error('Admin delete user error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /admin/set-achievements
 * Admin endpoint to set achievements for a user by display_name
 * Body: { admin_token: string, display_name: string, achievements: string[] }
 */
app.post('/admin/set-achievements', async (req, res) => {
    try {
        const { display_name, achievements } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!display_name || !achievements) {
            return res.status(400).json({ error: 'display_name and achievements array required' });
        }
        const scanBlock = await enforceAdminScanCooldown(res, 'set-achievements');
        if (scanBlock) return;

        // Find user in all profile types (check unified users first — canonical source)
        const patterns = ['user:*', 'profile:*', 'discord_profile:*'];
        let foundKey = null;
        let foundData = null;

        for (const pattern of patterns) {
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: pattern, count: 100 });
                cursor = String(result[0]);
                const keys = result[1] || [];

                for (const key of keys) {
                    const data = await redis.get(key);
                    if (data) {
                        const profile = typeof data === 'string' ? JSON.parse(data) : data;
                        if (profile.display_name && profile.display_name.toLowerCase() === display_name.toLowerCase()) {
                            foundKey = key;
                            foundData = profile;
                            break;
                        }
                    }
                }
                if (foundKey) break;
            } while (cursor !== "0");
            if (foundKey) break;
        }

        if (!foundKey || !foundData) {
            return res.status(404).json({ error: `User not found: ${display_name}` });
        }

        // Update achievements
        const oldAchievements = foundData.achievements || [];
        foundData.achievements = achievements;
        foundData.updated_at = new Date().toISOString();

        await redis.set(foundKey, JSON.stringify(foundData));

        console.log(`[ADMIN] Set achievements for ${display_name}: ${oldAchievements.length} -> ${achievements.length}`);

        res.json({
            success: true,
            display_name,
            key: foundKey,
            old_count: oldAchievements.length,
            new_count: achievements.length,
            achievements
        });
    } catch (error) {
        console.error('Admin set-achievements error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /admin/delete-legacy-profile
 * Admin endpoint to delete legacy profile:* or discord_profile:* keys by display_name
 * Body: { admin_token: string, display_name: string }
 */
app.post('/admin/delete-legacy-profile', async (req, res) => {
    try {
        const { display_name } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!display_name) {
            return res.status(400).json({ error: 'display_name required' });
        }
        const scanBlock = await enforceAdminScanCooldown(res, 'delete-legacy-profile');
        if (scanBlock) return;

        const deleted = [];
        const patterns = ['profile:*', 'discord_profile:*'];

        for (const pattern of patterns) {
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: pattern, count: 100 });
                cursor = String(result[0]);
                const keys = result[1] || [];

                for (const key of keys) {
                    const data = await redis.get(key);
                    if (data) {
                        const profile = typeof data === 'string' ? JSON.parse(data) : data;
                        const name = profile.display_name || profile.username || profile.patron_name;
                        if (name && name.toLowerCase() === display_name.toLowerCase()) {
                            await redis.del(key);
                            deleted.push({ key, name });
                            console.log(`[ADMIN] Deleted legacy key: ${key} (${name})`);
                        }
                    }
                }
            } while (cursor !== "0");
        }

        if (deleted.length === 0) {
            return res.json({ success: false, message: `No legacy profiles found for: ${display_name}` });
        }

        res.json({
            success: true,
            deleted_count: deleted.length,
            deleted_keys: deleted
        });
    } catch (error) {
        console.error('Admin delete legacy profile error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /admin/purge-user-completely
 * Admin endpoint to completely purge ALL data for a discord_id or patreon_id
 * This includes: user:*, indexes, season0:*, legacy profile:*, leaderboard entries
 * Body: { admin_token: string, discord_id?: string, patreon_id?: string }
 */
app.post('/admin/purge-user-completely', async (req, res) => {
    try {
        const { discord_id, patreon_id } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!discord_id && !patreon_id) {
            return res.status(400).json({ error: 'discord_id or patreon_id required' });
        }

        const deleted = [];

        // 1. Delete season0 keys
        if (discord_id) {
            const key = `season0:discord:${discord_id}`;
            const existed = await redis.del(key);
            if (existed) deleted.push(key);
        }
        if (patreon_id) {
            const key = `season0:patreon:${patreon_id}`;
            const existed = await redis.del(key);
            if (existed) deleted.push(key);
        }

        // 2. Delete indexes and find unified_id
        let unifiedId = null;
        if (discord_id) {
            const indexKey = `discord_index:${discord_id}`;
            unifiedId = await redis.get(indexKey);
            const existed = await redis.del(indexKey);
            if (existed) deleted.push(indexKey);
        }
        if (patreon_id) {
            const indexKey = `patreon_index:${patreon_id}`;
            const uid = await redis.get(indexKey);
            if (uid) unifiedId = uid;
            const existed = await redis.del(indexKey);
            if (existed) deleted.push(indexKey);
        }

        // 3. Delete user record and display_name index
        if (unifiedId) {
            const userData = await redis.get(`user:${unifiedId}`);
            if (userData) {
                const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

                // Delete display name index
                if (user.display_name) {
                    const nameKey = `display_name_index:${user.display_name.toLowerCase()}`;
                    await redis.del(nameKey);
                    deleted.push(nameKey);
                }

                // Delete from leaderboard
                if (user.current_season) {
                    await redis.zrem(`leaderboard:${user.current_season}`, unifiedId);
                    deleted.push(`leaderboard:${user.current_season} (member removed)`);
                }
                try { await redis.zrem('leaderboard:all-time', unifiedId); deleted.push('leaderboard:all-time (member removed)'); } catch (e) { /* best-effort */ }

                // Delete user record
                await redis.del(`user:${unifiedId}`);
                deleted.push(`user:${unifiedId}`);
            }
        }

        // 4. Delete legacy profile:* keys
        if (patreon_id) {
            const key = `profile:${patreon_id}`;
            const existed = await redis.del(key);
            if (existed) deleted.push(key);
        }

        // 5. Delete legacy discord_profile:* keys
        if (discord_id) {
            const key = `discord_profile:${discord_id}`;
            const existed = await redis.del(key);
            if (existed) deleted.push(key);
        }

        console.log(`[ADMIN] Purged user completely: discord=${discord_id}, patreon=${patreon_id}, deleted=${deleted.length} keys`);

        res.json({
            success: true,
            deleted_count: deleted.length,
            deleted_keys: deleted,
            unified_id_found: unifiedId
        });
    } catch (error) {
        console.error('Admin purge user error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/user/update
 * Update user profile data (XP, level, stats, achievements, settings)
 * Body: { unified_id: string, ...updates }
 */
app.post('/v2/user/update', async (req, res) => {
    try {
        const { unified_id, xp, level, stats, achievements, settings } = req.body;

        if (!unified_id) {
            return res.status(400).json({ error: 'unified_id required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Auth token validation
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }
        if (authCheck.legacy) {
            console.log(`[AUTH] ${authCheck.missing ? 'Old client (token not sent)' : 'Legacy user (no token hash)'} for ${unified_id} on ${req.path}`);
        }

        // Per-user cooldown: 10s minimum between updates (after auth to prevent unauthenticated abuse)
        // R12/M7: Atomic NX cooldown to prevent TOCTOU race
        const updateCooldownKey = `update_cooldown:${unified_id}`;
        const updateCooldownSet = await redis.set(updateCooldownKey, '1', { nx: true, ex: 10 });
        if (!updateCooldownSet) {
            return res.status(429).json({ error: 'Update cooldown active', retry_after: 10 });
        }

        const currentSeason = getCurrentSeason();

        // Check if user needs season reset (if their current_season doesn't match)
        if (user.current_season && user.current_season !== currentSeason) {
            const oldSeason = user.current_season;
            // Auto-reset for new season
            console.log(`[V2] Auto-resetting user ${unified_id} from ${oldSeason} to ${currentSeason}`);

            // Archive stats to all_time
            if (!user.all_time_stats) user.all_time_stats = {};
            user.all_time_stats.total_flashes = (user.all_time_stats.total_flashes || 0) + (user.stats?.total_flashes || 0);
            user.all_time_stats.total_bubbles_popped = (user.all_time_stats.total_bubbles_popped || 0) + (user.stats?.total_bubbles_popped || 0);
            user.all_time_stats.total_video_minutes = (user.all_time_stats.total_video_minutes || 0) + (user.stats?.total_video_minutes || 0);
            user.all_time_stats.total_lock_cards_completed = (user.all_time_stats.total_lock_cards_completed || 0) + (user.stats?.total_lock_cards_completed || 0);
            user.all_time_stats.seasons_completed = (user.all_time_stats.seasons_completed || 0) + 1;

            // Update highest level if needed
            user.highest_level_ever = Math.max(user.highest_level_ever || 0, user.level || 1);

            // Recalculate unlocks
            user.unlocks = calculateUnlocks(user.highest_level_ever);

            // Reset seasonal data
            user.xp = 0;
            user.level = 1;
            user.stats = {
                total_flashes: 0,
                total_bubbles_popped: 0,
                total_video_minutes: 0,
                total_lock_cards_completed: 0
            };
            user.current_season = currentSeason;
            user.level_reset_at = new Date().toISOString();
            delete user.oopsie_used_season;

            // Move user to new season leaderboard (remove from OLD season, not new)
            await redis.zrem(`leaderboard:${oldSeason}`, unified_id);
            await redis.zadd(`leaderboard:${currentSeason}`, { score: 0, member: unified_id });
            // All-time leaderboard persists across seasons (total_xp_earned is NOT reset)
            try { await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(user.total_xp_earned || 0), member: unified_id }); } catch (e) { console.log(`[WARN] All-time leaderboard update failed for ${unified_id}:`, e.message); }
        }

        // XP, level, stats, and achievements are anti-cheat protected — admin-only via this endpoint.
        // Regular clients must use /v2/user/sync which has delta capping, HMAC verification, and rate limits.
        const admin_token = req.headers['x-admin-token'];
        const isAdmin = admin_token && verifyAdminToken(admin_token);

        if ((typeof xp === 'number' || typeof level === 'number' || stats || achievements) && !isAdmin) {
            return res.status(403).json({ error: 'XP/level/stats/achievements updates require admin authorization. Use /v2/user/sync instead.' });
        }

        // Apply updates (admin-only for progression fields)
        if (isAdmin) {
            if (typeof xp === 'number') {
                const adminXpDelta = xp - (user.xp || 0);
                user.xp = xp;
                if (adminXpDelta > 0) {
                    user.total_xp_earned = (user.total_xp_earned || 0) + adminXpDelta;
                }
                await redis.zadd(`leaderboard:${currentSeason}`, { score: safeLeaderboardScore(xp), member: unified_id });
                try { await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(user.total_xp_earned || 0), member: unified_id }); } catch (e) { console.log(`[WARN] All-time leaderboard update failed for ${unified_id}:`, e.message); }
            }
            if (typeof level === 'number') {
                user.level = level;
                if (level > (user.highest_level_ever || 0)) {
                    user.highest_level_ever = level;
                    user.unlocks = calculateUnlocks(level);
                }
            }
            if (stats) {
                user.stats = { ...(user.stats || {}), ...stats };
            }
            if (achievements && Array.isArray(achievements)) {
                const existingAchievements = new Set(user.achievements || []);
                for (const ach of achievements) {
                    if (typeof ach === 'string' && ach.length <= 100) {
                        existingAchievements.add(ach);
                    }
                }
                user.achievements = [...existingAchievements].slice(0, 500);
            }
        }
        if (settings) {
            if (typeof settings.allow_discord_dm === 'boolean') {
                user.allow_discord_dm = settings.allow_discord_dm;
            }
            if (typeof settings.show_online_status === 'boolean') {
                user.show_online_status = settings.show_online_status;
            }
        }

        user.updated_at = new Date().toISOString();
        user.last_seen = new Date().toISOString();

        await redis.set(`user:${unified_id}`, JSON.stringify(user));

        res.json({
            success: true,
            user: {
                unified_id: user.unified_id,
                display_name: user.display_name,
                level: user.level,
                xp: user.xp,
                current_season: user.current_season,
                highest_level_ever: user.highest_level_ever,
                unlocks: user.unlocks,
                achievements: user.achievements,
                stats: user.stats
            }
        });
    } catch (error) {
        console.error('V2 user update error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/user/change-display-name
 * Allow a user to change their display name. Name must be unique (case-insensitive).
 * Allows case-only changes (e.g. "alice" → "Alice") without uniqueness conflict.
 * Admin can force-rename by passing admin_token (bypasses uniqueness check, cleans up stale indexes).
 * Body: { unified_id: string, new_display_name: string, admin_token?: string }
 */
app.post('/v2/user/change-display-name', async (req, res) => {
    try {
        const { unified_id, new_display_name } = req.body;
        const admin_token = req.headers['x-admin-token'];
        const isAdmin = admin_token && verifyAdminToken(admin_token);

        if (!unified_id) {
            return res.status(400).json({ error: 'unified_id required' });
        }
        if (!new_display_name || typeof new_display_name !== 'string') {
            return res.status(400).json({ error: 'new_display_name required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const trimmed = new_display_name.trim();
        if (!isValidDisplayNameChars(trimmed)) {
            return res.status(400).json({ error: 'Display name contains invalid characters' });
        }
        if (trimmed.length < 3 || trimmed.length > 20) {
            return res.status(400).json({ error: 'Display name must be 3-20 characters' });
        }

        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Auth token validation (admin bypass allowed)
        if (!isAdmin) {
            const authCheck = validateAuthToken(req, user);
            if (!authCheck.valid) {
                return res.status(401).json({ error: 'Invalid or missing auth token' });
            }
            if (authCheck.legacy) {
                console.log(`[AUTH] ${authCheck.missing ? 'Old client (token not sent)' : 'Legacy user (no token hash)'} for ${unified_id} on ${req.path}`);
            }
        }

        // Per-user cooldown: 60s minimum between display name changes (after auth, admin bypass)
        // R12/M8: Atomic NX cooldown to prevent TOCTOU race
        if (!isAdmin) {
            const nameCooldownKey = `name_change_cooldown:${unified_id}`;
            const nameCooldownSet = await redis.set(nameCooldownKey, '1', { nx: true, ex: 60 });
            if (!nameCooldownSet) {
                return res.status(429).json({ error: 'Display name change cooldown active', retry_after: 60 });
            }
        }

        const oldName = user.display_name;

        // Allow case-only changes without uniqueness conflict
        // Admin can force-rename (bypasses uniqueness, cleans up stale indexes)
        const isCaseOnlyChange = oldName && oldName.toLowerCase() === trimmed.toLowerCase();
        if (!isCaseOnlyChange && !isAdmin) {
            const takenBy = await isDisplayNameTaken(trimmed);
            if (takenBy && takenBy !== unified_id) {
                return res.status(409).json({ error: 'This name is already taken' });
            }
        }
        // Admin force: clean up any stale index for the target name
        if (isAdmin) {
            await redis.del(`display_name_index:${trimmed.toLowerCase()}`);
        }

        // Remove old display_name_index
        if (oldName) {
            await redis.del(`display_name_index:${oldName.toLowerCase()}`);
        }

        // Create new display_name_index
        await redis.set(`display_name_index:${trimmed.toLowerCase()}`, unified_id);

        // Update user record
        user.display_name = trimmed;
        user.display_name_changed_at = new Date().toISOString();
        user.updated_at = new Date().toISOString();

        // Check whitelist with new name
        const whitelisted = isWhitelisted(null, null, trimmed);
        user.is_whitelisted = whitelisted;

        await redis.set(`user:${unified_id}`, JSON.stringify(user));

        console.log(`[DisplayName] User ${unified_id} changed name from "${oldName}" to "${trimmed}" (whitelisted: ${whitelisted})`);

        res.json({
            success: true,
            new_display_name: trimmed
        });
    } catch (error) {
        console.error('Change display name error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/user/use-oopsie
 * Server-side oopsie insurance: deduct 500 XP and mark as used for this season.
 * Prevents cheating by validating on server.
 * Body: { unified_id: string, fix_date: string (YYYY-MM-DD) }
 */
app.post('/v2/user/use-oopsie', async (req, res) => {
    try {
        const { unified_id, fix_date } = req.body;

        if (!unified_id) {
            return res.status(400).json({ error: 'unified_id required' });
        }
        if (!fix_date) {
            return res.status(400).json({ error: 'fix_date required' });
        }
        if (typeof fix_date !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(fix_date)) {
            return res.status(400).json({ error: 'fix_date must be in YYYY-MM-DD format' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Auth token validation
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }
        if (authCheck.legacy) {
            console.log(`[AUTH] ${authCheck.missing ? 'Old client (token not sent)' : 'Legacy user (no token hash)'} for ${unified_id} on ${req.path}`);
        }

        const currentSeason = getCurrentSeason();

        // Acquire lock to prevent double-spend race condition
        const lockKey = `oopsie_lock:${unified_id}`;
        const lockAcquired = await redis.set(lockKey, '1', { nx: true, ex: 5 });
        if (!lockAcquired) {
            return res.status(429).json({ error: 'Oopsie request already in progress. Please try again.' });
        }

        try {
            // Re-read user inside lock for freshest state
            const freshData = await redis.get(`user:${unified_id}`);
            const freshUser = typeof freshData === 'string' ? JSON.parse(freshData) : freshData;

            // Check if already used this season
            if (freshUser.oopsie_used_season === currentSeason) {
                return res.status(400).json({ error: 'Already used this season' });
            }

            // Check XP requirement (500 XP)
            if ((freshUser.xp || 0) < 500) {
                return res.status(400).json({ error: 'Not enough XP' });
            }

            // Deduct 500 XP
            freshUser.xp = (freshUser.xp || 0) - 500;
            freshUser.oopsie_used_season = currentSeason;

            // Add fix_date to quest_completion_dates in stats
            if (!freshUser.stats) freshUser.stats = {};
            if (!freshUser.stats.quest_completion_dates) freshUser.stats.quest_completion_dates = [];
            if (Array.isArray(freshUser.stats.quest_completion_dates) && !freshUser.stats.quest_completion_dates.includes(fix_date)) {
                freshUser.stats.quest_completion_dates.push(fix_date);
            }

            freshUser.updated_at = new Date().toISOString();

            // Save user
            await redis.set(`user:${unified_id}`, JSON.stringify(freshUser));

            // Update leaderboard sorted set with new XP
            const season = freshUser.current_season || currentSeason;
            await redis.zadd(`leaderboard:${season}`, { score: safeLeaderboardScore(freshUser.xp), member: unified_id });
            // All-time leaderboard: total_xp_earned does NOT decrease on oopsie (XP was earned then spent)
            try { await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(freshUser.total_xp_earned || 0), member: unified_id }); } catch (e) { console.log(`[WARN] All-time leaderboard update failed for ${unified_id}:`, e.message); }

            console.log(`[Oopsie] User ${unified_id} used oopsie insurance: -500 XP, fixed ${fix_date}, new XP: ${freshUser.xp}`);

            res.json({
                success: true,
                new_xp: freshUser.xp,
                oopsie_used_season: currentSeason
            });
        } finally {
            await redis.del(lockKey).catch(() => {});
        }
    } catch (error) {
        console.error('Oopsie insurance error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * GET /v3/leaderboard
 * New leaderboard endpoint using Redis sorted sets.
 * Query: ?season=YYYY-MM (optional, defaults to current)
 *        &limit=N (default 200, max 5000)
 *        &offset=N (for pagination)
 */
// In-memory leaderboard cache (reduces Redis reads from ~2,260 per request to ~3 per cache refresh)
const leaderboardV3Cache = {};
const LEADERBOARD_V3_CACHE_TTL = 30_000; // 30 seconds
const LEADERBOARD_V3_CACHE_MAX_ENTRIES = 24; // Max cached seasons (2 years worth)

app.get('/v3/leaderboard', async (req, res) => {
    try {
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Per-IP rate limit: 15 leaderboard requests per minute (raised from 5 for monthly/all-time toggling)
        const lbIp = req.ip || 'unknown';
        try {
            const lbRateKey = `lb_rate:${lbIp}`;
            await redis.set(lbRateKey, 0, { nx: true, ex: 60 });
            const lbCount = await redis.incr(lbRateKey);
            if (lbCount > 15) {
                return res.status(429).json({ error: 'Too many leaderboard requests. Please wait.' });
            }
        } catch (e) {
            // R12/M10: Fail-closed on Redis rate limit failure (matches all other endpoints)
            return res.status(503).json({ error: 'Service temporarily unavailable' });
        }

        const season = req.query.season || getCurrentSeason();
        // Allow "all-time" as a special season value (exact match only — prevents injection via "all-time-../../key")
        if (season !== 'all-time') {
            // Validate season format to prevent arbitrary Redis key lookups and cache pollution
            if (!/^\d{4}-\d{2}$/.test(season)) {
                return res.status(400).json({ error: 'Invalid season format. Expected YYYY-MM or all-time.' });
            }
            // Restrict season range to prevent cache pollution (current year +/- 1)
            const seasonYear = parseInt(season.substring(0, 4), 10);
            const seasonMonth = parseInt(season.substring(5, 7), 10);
            const currentYear = new Date().getFullYear();
            if (seasonYear < currentYear - 1 || seasonYear > currentYear + 1 || seasonMonth < 1 || seasonMonth > 12) {
                return res.status(400).json({ error: 'Season out of valid range.' });
            }
        }
        let limit = parseInt(req.query.limit, 10) || 200;
        limit = Math.max(Math.min(limit, 200), 1);
        const offset = parseInt(req.query.offset) || 0;

        // Optional: caller's unified_id for rank lookup (avoids client scanning 200-entry cap)
        const requesterId = req.query.unified_id || null;
        if (requesterId && (typeof requesterId !== 'string' || requesterId.length > 50 || !/^[a-zA-Z0-9_-]+$/.test(requesterId))) {
            return res.status(400).json({ error: 'Invalid unified_id format.' });
        }

        const leaderboardKey = `leaderboard:${season}`;

        // Look up requester's rank + season member count (cheap O(log N) ops, always fresh from Redis)
        let yourRank = null;
        let yourTotal = null;
        if (requesterId) {
            try {
                const [rawRank, cardCount] = await Promise.all([
                    redis.zrevrank(leaderboardKey, requesterId),
                    redis.zcard(leaderboardKey)
                ]);
                yourRank = (rawRank !== null && rawRank !== undefined) ? rawRank + 1 : null;
                yourTotal = cardCount || 0;
            } catch (e) {
                console.error('Rank lookup error:', e.message);
                // Non-fatal — continue without rank
            }
        }

        // Check cache (keyed by season since limit/offset are applied post-fetch)
        const cacheKey = season;
        const cached = leaderboardV3Cache[cacheKey];
        if (cached && (Date.now() - cached.time) < LEADERBOARD_V3_CACHE_TTL) {
            // Serve from cache, applying offset/limit (clone to avoid mutating cached objects)
            const sliced = cached.entries.slice(offset, offset + limit).map(e => ({ ...e }));
            // Re-number ranks for the slice
            sliced.forEach((e, i) => { e.rank = offset + i + 1; });
            const response = {
                season,
                entries: sliced,
                total_users: cached.totalUsers,
                online_users: cached.globalOnlineCount,
                offset,
                limit,
                fetched_at: cached.fetchedAt,
                cached: true
            };
            if (requesterId) {
                response.your_rank = yourRank;
                response.your_total = yourTotal;
            }
            return res.json(response);
        }

        // Get top 5000 ranked users from sorted set (highest XP first) for caching
        // Capped to prevent unbounded Redis fetch on large leaderboards
        const rankedMembers = await redis.zrange(leaderboardKey, 0, 4999, { rev: true, withScores: true });

        // Build list of {unifiedId, xp} from ranked members.
        // Upstash SDK may return objects [{member/value, score}, ...] or a flat array [member, score, ...].
        const ranked = [];
        if (rankedMembers && rankedMembers.length > 0) {
            if (typeof rankedMembers[0] === 'object') {
                for (const item of rankedMembers) {
                    ranked.push({ unifiedId: item.member || item.value, xp: Number(item.score) || 0 });
                }
            } else {
                for (let i = 0; i < rankedMembers.length; i += 2) {
                    ranked.push({ unifiedId: rankedMembers[i], xp: Number(rankedMembers[i + 1]) || 0 });
                }
            }
        }

        // Batch-fetch all user records with MGET instead of individual GETs
        const allEntries = [];
        if (ranked.length > 0) {
            const userKeys = ranked.map(r => `user:${r.unifiedId}`);
            const userDataArray = await redis.mget(...userKeys);

            for (let i = 0; i < ranked.length; i++) {
                const { unifiedId, xp } = ranked[i];
                if (!unifiedId) continue;

                const userData = userDataArray[i];
                if (userData) {
                    const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

                    const lastSeen = user.last_seen ? new Date(user.last_seen) : null;
                    const isOnline = lastSeen && (Date.now() - lastSeen.getTime()) < 180000 && user.show_online_status !== false;

                    const hasTrophyCase = Array.isArray(user.unlocked_skills) && user.unlocked_skills.includes('trophy_case');

                    let safeName = user.display_name || null;
                    if (safeName && user.patron_name && !user.display_name_set_at &&
                        safeName.toLowerCase().trim() === user.patron_name.toLowerCase().trim()) {
                        safeName = null;
                    }

                    allEntries.push({
                        rank: allEntries.length + 1,
                        unified_id: unifiedId,
                        display_name: safeName,
                        level: user.level || 1,
                        xp: xp,
                        is_online: isOnline,
                        is_patreon: user.patreon_is_active || user.patreon_is_whitelisted || (user.patreon_tier > 0),
                        patreon_tier: user.patreon_tier || 0,
                        is_season0_og: user.is_season0_og || false,
                        discord_id: user.allow_discord_dm ? user.discord_id : null,
                        achievements_count: user.achievements?.length || 0,
                        total_bubbles_popped: user.stats?.total_bubbles_popped || 0,
                        total_flashes: user.stats?.total_flashes || 0,
                        total_video_minutes: user.stats?.total_video_minutes || 0,
                        total_lock_cards_completed: user.stats?.total_lock_cards_completed || 0,
                        has_trophy_case: hasTrophyCase,
                        longest_session_minutes: hasTrophyCase ? (user.stats?.longest_session_minutes || 0) : 0,
                        highest_streak: hasTrophyCase ? (user.stats?.consecutive_days || 0) : 0,
                        seasons_completed: user.all_time_stats?.seasons_completed || 0,
                        total_xp_earned: user.total_xp_earned || 0,
                        highest_level_ever: user.highest_level_ever || 0
                    });
                }
            }
        }

        // Total users = all registered users (not just current season)
        const globalTotal = await getTotalUserCount();
        const totalUsers = globalTotal !== null ? globalTotal : await redis.zcard(leaderboardKey);
        // Online count from sorted set (all users, not just leaderboard members)
        const globalOnlineCount = await getOnlineUserCount();
        const fetchedAt = new Date().toISOString();

        // Evict oldest cache entries if at capacity
        const cacheKeys = Object.keys(leaderboardV3Cache);
        if (cacheKeys.length >= LEADERBOARD_V3_CACHE_MAX_ENTRIES) {
            // Remove the oldest entry by timestamp
            let oldestKey = cacheKeys[0], oldestTime = leaderboardV3Cache[cacheKeys[0]]?.time || 0;
            for (const k of cacheKeys) {
                if ((leaderboardV3Cache[k]?.time || 0) < oldestTime) {
                    oldestTime = leaderboardV3Cache[k].time;
                    oldestKey = k;
                }
            }
            delete leaderboardV3Cache[oldestKey];
        }

        // Store full result in cache
        leaderboardV3Cache[cacheKey] = {
            entries: allEntries,
            totalUsers,
            globalOnlineCount,
            time: Date.now(),
            fetchedAt
        };

        // Apply offset/limit for this response
        const sliced = allEntries.slice(offset, offset + limit);
        sliced.forEach((e, i) => { e.rank = offset + i + 1; });

        const response = {
            season,
            entries: sliced,
            total_users: totalUsers,
            online_users: globalOnlineCount,
            offset,
            limit,
            fetched_at: fetchedAt
        };
        if (requesterId) {
            response.your_rank = yourRank;
            response.your_total = yourTotal;
        }
        res.json(response);
    } catch (error) {
        console.error('V3 leaderboard error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * GET /v2/leaderboard
 * DEPRECATED - kept returning 503 during transition.
 * Use /v3/leaderboard instead.
 */
app.get('/v2/leaderboard', async (req, res) => {
    try {
        // Temporarily disabled during new leaderboard transition (2026-02-06)
        return res.status(503).json({ error: 'Leaderboard is temporarily unavailable while we upgrade to the new system. Check back soon!' });

        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const season = req.query.season || getCurrentSeason();
        let limit = parseInt(req.query.limit) || 200;
        limit = Math.min(Math.max(limit, 1), 1000);
        const offset = parseInt(req.query.offset) || 0;

        const leaderboardKey = `leaderboard:${season}`;

        // Get ranked users from sorted set (highest XP first)
        // Upstash uses zrange with rev:true instead of zrevrange
        const rankedMembers = await redis.zrange(leaderboardKey, offset, offset + limit - 1, { rev: true, withScores: true });

        // rankedMembers is an array like [member1, score1, member2, score2, ...]
        // or with upstash it might be array of objects
        const entries = [];
        let rank = offset + 1;

        // Handle both formats (array of objects or flat array)
        if (rankedMembers.length > 0 && typeof rankedMembers[0] === 'object' && 'member' in rankedMembers[0]) {
            // Upstash format: [{member, score}, ...]
            for (const item of rankedMembers) {
                const unifiedId = item.member;
                const xp = item.score;

                // Fetch user data
                const userData = await redis.get(`user:${unifiedId}`);
                if (userData) {
                    const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

                    // Check if online (within last minute)
                    const lastSeen = user.last_seen ? new Date(user.last_seen) : null;
                    const isOnline = lastSeen && (Date.now() - lastSeen.getTime()) < 180000 && user.show_online_status !== false;

                    // Privacy: never expose patron_name on the leaderboard
                    // But if display_name_set_at exists, the user explicitly chose this name — respect it.
                    let safeName = user.display_name || null;
                    if (safeName && user.patron_name && !user.display_name_set_at &&
                        safeName.toLowerCase().trim() === user.patron_name.toLowerCase().trim()) {
                        safeName = null;
                    }

                    entries.push({
                        rank: rank++,
                        unified_id: unifiedId,
                        display_name: safeName,
                        level: user.level || 1,
                        xp: xp,
                        is_online: isOnline,
                        is_patreon: user.patreon_is_active || user.patreon_is_whitelisted || (user.patreon_tier > 0),
                        patreon_tier: user.patreon_tier || 0,
                        is_season0_og: user.is_season0_og || false,
                        discord_id: user.allow_discord_dm ? user.discord_id : null,
                        achievements_count: user.achievements?.length || 0,
                        // Stats (all-time, don't reset)
                        total_bubbles_popped: user.stats?.total_bubbles_popped || 0,
                        total_flashes: user.stats?.total_flashes || 0,
                        total_video_minutes: user.stats?.total_video_minutes || 0,
                        total_lock_cards_completed: user.stats?.total_lock_cards_completed || 0
                    });
                }
            }
        } else {
            // Flat array format: [member1, score1, member2, score2, ...]
            for (let i = 0; i < rankedMembers.length; i += 2) {
                const unifiedId = rankedMembers[i];
                const xp = parseInt(rankedMembers[i + 1]) || 0;

                const userData = await redis.get(`user:${unifiedId}`);
                if (userData) {
                    const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

                    const lastSeen = user.last_seen ? new Date(user.last_seen) : null;
                    const isOnline = lastSeen && (Date.now() - lastSeen.getTime()) < 180000 && user.show_online_status !== false;

                    // Privacy: never expose patron_name on the leaderboard
                    // But if display_name_set_at exists, the user explicitly chose this name — respect it.
                    let safeName2 = user.display_name || null;
                    if (safeName2 && user.patron_name && !user.display_name_set_at &&
                        safeName2.toLowerCase().trim() === user.patron_name.toLowerCase().trim()) {
                        safeName2 = null;
                    }

                    entries.push({
                        rank: rank++,
                        unified_id: unifiedId,
                        display_name: safeName2,
                        level: user.level || 1,
                        xp: xp,
                        is_online: isOnline,
                        is_patreon: user.patreon_is_active || user.patreon_is_whitelisted || (user.patreon_tier > 0),
                        patreon_tier: user.patreon_tier || 0,
                        is_season0_og: user.is_season0_og || false,
                        discord_id: user.allow_discord_dm ? user.discord_id : null,
                        achievements_count: user.achievements?.length || 0,
                        // Stats (all-time, don't reset)
                        total_bubbles_popped: user.stats?.total_bubbles_popped || 0,
                        total_flashes: user.stats?.total_flashes || 0,
                        total_video_minutes: user.stats?.total_video_minutes || 0,
                        total_lock_cards_completed: user.stats?.total_lock_cards_completed || 0
                    });
                }
            }
        }

        // Get total count
        const totalUsers = await redis.zcard(leaderboardKey);
        const onlineCount = entries.filter(e => e.is_online).length;

        res.json({
            season,
            entries,
            total_users: totalUsers,
            online_users: onlineCount,
            offset,
            limit,
            fetched_at: new Date().toISOString()
        });
    } catch (error) {
        console.error('V2 leaderboard error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /admin/leaderboard-remove
 * Batch remove specific users from leaderboard sorted set
 * Body: { admin_token: string, season: string, unified_ids: string[] }
 */
app.post('/admin/leaderboard-remove', async (req, res) => {
    try {
        const { season, unified_ids } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!season || !unified_ids || !Array.isArray(unified_ids)) {
            return res.status(400).json({ error: 'season and unified_ids[] required' });
        }

        const leaderboardKey = `leaderboard:${season}`;
        let removed = 0;
        for (const uid of unified_ids) {
            await redis.zrem(leaderboardKey, uid);
            removed++;
        }

        res.json({ success: true, season, removed });
    } catch (error) {
        console.error('Admin leaderboard-remove error:', error.message);
        res.status(500).json({ error: 'Failed to remove from leaderboard' });
    }
});

/**
 * POST /admin/cleanup-leaderboard
 * Remove orphaned entries from leaderboard (users that no longer exist)
 * Body: { admin_token: string, season?: string, dry_run?: boolean }
 */
app.post('/admin/cleanup-leaderboard', async (req, res) => {
    try {
        const { season, dry_run = true } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const targetSeason = season || getCurrentSeason();
        const leaderboardKey = `leaderboard:${targetSeason}`;

        // Get all members from the sorted set
        const allMembers = await redis.zrange(leaderboardKey, 0, -1, { withScores: true });

        const orphaned = [];
        const valid = [];

        // Check each member
        for (const item of allMembers) {
            const unifiedId = item.member || item;
            const userData = await redis.get(`user:${unifiedId}`);

            if (!userData) {
                orphaned.push(unifiedId);
                if (!dry_run) {
                    await redis.zrem(leaderboardKey, unifiedId);
                }
            } else {
                valid.push(unifiedId);
            }
        }

        res.json({
            season: targetSeason,
            dry_run,
            total_checked: allMembers.length,
            valid_count: valid.length,
            orphaned_count: orphaned.length,
            orphaned_ids: orphaned,
            removed: dry_run ? 0 : orphaned.length
        });
    } catch (error) {
        console.error('Cleanup leaderboard error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /admin/trigger-season-reset
 * Manually trigger a season reset for all users
 * Body: { admin_token: string, new_season?: string, dry_run?: boolean }
 */
app.post('/admin/trigger-season-reset', async (req, res) => {
    try {
        const { new_season, dry_run = true } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock = await enforceAdminScanCooldown(res, 'trigger-season-reset');
        if (scanBlock) return;

        const targetSeason = new_season || getCurrentSeason();
        const resetResults = [];
        const errors = [];

        // Scan all user:* keys
        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = String(result[0]);

            for (const key of (result[1] || [])) {
                try {
                    const userData = await redis.get(key);
                    if (!userData) continue;

                    const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
                    const unifiedId = key.replace('user:', '');

                    // Skip if already in target season
                    if (user.current_season === targetSeason) {
                        continue;
                    }

                    const oldSeason = user.current_season;
                    const oldLevel = user.level;
                    const oldXp = user.xp;

                    if (!dry_run) {
                        // Archive stats to all_time
                        if (!user.all_time_stats) user.all_time_stats = {};
                        user.all_time_stats.total_flashes = (user.all_time_stats.total_flashes || 0) + (user.stats?.total_flashes || 0);
                        user.all_time_stats.total_bubbles_popped = (user.all_time_stats.total_bubbles_popped || 0) + (user.stats?.total_bubbles_popped || 0);
                        user.all_time_stats.total_video_minutes = (user.all_time_stats.total_video_minutes || 0) + (user.stats?.total_video_minutes || 0);
                        user.all_time_stats.total_lock_cards_completed = (user.all_time_stats.total_lock_cards_completed || 0) + (user.stats?.total_lock_cards_completed || 0);
                        user.all_time_stats.seasons_completed = (user.all_time_stats.seasons_completed || 0) + 1;

                        // Update highest level if needed
                        user.highest_level_ever = Math.max(user.highest_level_ever || 0, user.level || 1);

                        // Recalculate unlocks
                        user.unlocks = calculateUnlocks(user.highest_level_ever);

                        // Reset seasonal data
                        user.xp = 0;
                        user.level = 1;
                        user.stats = {
                            total_flashes: 0,
                            total_bubbles_popped: 0,
                            total_video_minutes: 0,
                            total_lock_cards_completed: 0
                        };
                        user.current_season = targetSeason;
                        user.level_reset_at = new Date().toISOString();
                        delete user.oopsie_used_season;
                        user.updated_at = new Date().toISOString();

                        // Save user
                        await redis.set(key, JSON.stringify(user));

                        // Update leaderboard
                        if (oldSeason) {
                            await redis.zrem(`leaderboard:${oldSeason}`, unifiedId);
                        }
                        await redis.zadd(`leaderboard:${targetSeason}`, { score: 0, member: unifiedId });
                        try { await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(user.total_xp_earned || 0), member: unifiedId }); } catch (e) { /* best-effort */ }
                    }

                    resetResults.push({
                        unified_id: unifiedId,
                        display_name: user.display_name,
                        old_season: oldSeason,
                        old_level: oldLevel,
                        old_xp: oldXp,
                        new_season: targetSeason
                    });
                } catch (e) {
                    errors.push({ key, error: e.message });
                }
            }
        } while (cursor !== "0");

        console.log(`[ADMIN] Season reset to ${targetSeason}: ${resetResults.length} users (dry_run: ${dry_run})`);

        res.json({
            dry_run,
            target_season: targetSeason,
            users_reset: resetResults.length,
            results: resetResults,
            errors_count: errors.length,
            errors
        });
    } catch (error) {
        console.error('Admin trigger-season-reset error:', error.message);
        res.status(500).json({ error: 'Failed to trigger season reset' });
    }
});

/**
 * POST /admin/clear-leaderboard
 * Remove all entries from a season's leaderboard
 * Body: { admin_token: string, season?: string }
 */
app.post('/admin/clear-leaderboard', async (req, res) => {
    try {
        const { season } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const targetSeason = season || getCurrentSeason();
        const key = `leaderboard:${targetSeason}`;
        const count = await redis.zcard(key);
        await redis.del(key);

        console.log(`[ADMIN] Cleared leaderboard ${key} (${count} entries)`);
        res.json({ success: true, season: targetSeason, entries_removed: count });
    } catch (error) {
        console.error('Admin clear-leaderboard error:', error.message);
        res.status(500).json({ error: 'Failed to clear leaderboard' });
    }
});

/**
 * POST /admin/reset-all-levels
 * Reset ALL users to level 1, xp 0, highest_level_ever 0
 * Keeps achievements, OG status, and stats
 * Body: { admin_token: string, dry_run?: boolean }
 */
app.post('/admin/reset-all-levels', async (req, res) => {
    try {
        const { dry_run = true } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock3 = await enforceAdminScanCooldown(res, 'reset-all-levels');
        if (scanBlock3) return;

        const currentSeason = getCurrentSeason();
        const results = [];
        const errors = [];

        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = String(result[0]);

            for (const key of (result[1] || [])) {
                try {
                    const userData = await redis.get(key);
                    if (!userData) continue;

                    const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
                    const unifiedId = key.replace('user:', '');
                    const oldLevel = user.level;
                    const oldXp = user.xp;
                    const oldHighest = user.highest_level_ever;

                    if (!dry_run) {
                        // Preserve highest_level_ever so feature unlocks are permanent across seasons
                        user.highest_level_ever = Math.max(user.highest_level_ever || 0, user.level || 1);
                        user.level = 1;
                        user.xp = 0;
                        user.unlocks = calculateUnlocks(user.highest_level_ever);
                        user.skill_points = 1; // level 1 = 1 sparkle point
                        user.unlocked_skills = [];
                        user.force_skills_reset = true; // Tell client to clear local skills
                        user.level_reset_at = new Date().toISOString(); // Prevent client from pushing cached old values
                        delete user.level_reset; // Clean up old boolean flag
                        user.current_season = currentSeason;
                        user.updated_at = new Date().toISOString();

                        await redis.set(key, JSON.stringify(user));
                    }

                    results.push({
                        unified_id: unifiedId,
                        display_name: user.display_name,
                        old_level: oldLevel,
                        old_xp: oldXp,
                        old_highest: oldHighest
                    });
                } catch (e) {
                    errors.push({ key, error: e.message });
                }
            }
        } while (cursor !== "0");

        console.log(`[ADMIN] Reset all levels: ${results.length} users (dry_run: ${dry_run})`);

        res.json({
            dry_run,
            users_reset: results.length,
            results,
            errors_count: errors.length,
            errors
        });
    } catch (error) {
        console.error('Admin reset-all-levels error:', error.message);
        res.status(500).json({ error: 'Failed to reset levels' });
    }
});

/**
 * POST /v2/user/sync
 * Sync user progression (XP, level, achievements, stats) to the V2 system
 * Body: { unified_id: string, xp: number, level: number, achievements: string[], stats: object }
 */
app.post('/v2/user/sync', async (req, res) => {
    try {
        const { unified_id, xp, level, achievements, stats, unlocked_skills, skill_points, allow_discord_dm, show_online_status, share_profile_picture, reset_weekly_quest, reset_daily_quest, force_streak_override: clientForceStreakOverride, force_skills_reset: clientForceSkillsReset, total_conditioning_minutes, companion_progress } = req.body;

        if (!unified_id) {
            return res.status(400).json({ error: 'unified_id required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // --- Anti-cheat: HMAC signature verification (Phase 3A — soft mode) ---
        const ccpTimestamp = req.headers['x-ccp-timestamp'];
        const ccpSignature = req.headers['x-ccp-signature'];
        let hmacValid = false;

        if (ccpTimestamp && ccpSignature) {
            const hmacSecret = process.env.CCP_HMAC_SECRET || '';
            if (!hmacSecret) {
                // Secret not configured — reject request (never bypass HMAC validation)
                console.error('[Anti-cheat] CCP_HMAC_SECRET not configured — rejecting sync. SET THIS ENV VAR.');
                return res.status(503).json({ error: 'Server HMAC configuration error. Contact support.' });
            } else {
                const keyMaterial = `${unified_id}:${hmacSecret}`;
                const rawBody = req.rawBody || JSON.stringify(req.body);
                const payload = `${ccpTimestamp}:${rawBody}`;

                const expectedSig = crypto.createHmac('sha256', keyMaterial).update(payload).digest('hex');

                // Check timestamp within 5-minute window
                const tsAge = Math.abs(Date.now() / 1000 - parseInt(ccpTimestamp));

                const sigMatch = timingSafeCompare(expectedSig, ccpSignature);
                if (sigMatch && tsAge < 300) {
                    hmacValid = true;
                } else {
                    console.log(`[Anti-cheat] HMAC failed for ${unified_id}: sig_match=${sigMatch}, ts_age=${Math.round(tsAge)}s`);
                }
            }
        }
        // Phase 3b (hard): Reject unsigned/invalid requests
        if (!hmacValid) {
            console.warn(`[Anti-cheat] Rejecting unsigned sync from ${unified_id} (ts=${ccpTimestamp ? 'present' : 'missing'}, sig=${ccpSignature ? 'present' : 'missing'})`);
            return res.status(403).json({ error: 'Invalid or missing request signature. Please update your app.' });
        }

        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Auth token validation
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }
        if (authCheck.legacy) {
            console.log(`[AUTH] ${authCheck.missing ? 'Old client (token not sent)' : 'Legacy user (no token hash)'} for ${unified_id} on ${req.path}`);
        }

        // --- Anti-cheat: Sync cooldown (minimum 30s between syncs) ---
        const SYNC_COOLDOWN_SECONDS = 30;
        if (user.last_sync_at) {
            const msSinceLastSync = Date.now() - new Date(user.last_sync_at).getTime();
            const secondsSinceLastSync = msSinceLastSync / 1000;
            if (secondsSinceLastSync < SYNC_COOLDOWN_SECONDS) {
                const retryAfter = Math.ceil(SYNC_COOLDOWN_SECONDS - secondsSinceLastSync);
                console.log(`[Anti-cheat] Sync cooldown for ${unified_id}: ${Math.round(secondsSinceLastSync)}s since last sync, need ${SYNC_COOLDOWN_SECONDS}s`);
                return res.status(429).json({
                    error: 'Sync cooldown active',
                    retry_after: retryAfter,
                    cooldown_seconds: SYNC_COOLDOWN_SECONDS
                });
            }
        }

        // Check if user needs season reset (current_season doesn't match)
        const currentSeason = getCurrentSeason();
        let seasonResetPerformed = false;
        if (user.current_season && user.current_season !== currentSeason) {
            const oldSeason = user.current_season;
            console.log(`[V2 Sync] Auto-resetting user ${unified_id} from ${oldSeason} to ${currentSeason}`);

            // Archive stats to all_time
            if (!user.all_time_stats) user.all_time_stats = {};
            user.all_time_stats.total_flashes = (user.all_time_stats.total_flashes || 0) + (user.stats?.total_flashes || 0);
            user.all_time_stats.total_bubbles_popped = (user.all_time_stats.total_bubbles_popped || 0) + (user.stats?.total_bubbles_popped || 0);
            user.all_time_stats.total_video_minutes = (user.all_time_stats.total_video_minutes || 0) + (user.stats?.total_video_minutes || 0);
            user.all_time_stats.total_lock_cards_completed = (user.all_time_stats.total_lock_cards_completed || 0) + (user.stats?.total_lock_cards_completed || 0);
            user.all_time_stats.seasons_completed = (user.all_time_stats.seasons_completed || 0) + 1;

            // Update highest level before reset
            user.highest_level_ever = Math.max(user.highest_level_ever || 0, user.level || 1);
            user.unlocks = calculateUnlocks(user.highest_level_ever);

            // Reset seasonal data
            user.xp = 0;
            user.level = 1;
            user.skill_points = 1; // level 1 = 1 sparkle point
            user.unlocked_skills = [];
            user.force_skills_reset = true; // Tell client to clear local skills
            user.stats = {
                total_flashes: 0,
                total_bubbles_popped: 0,
                total_video_minutes: 0,
                total_lock_cards_completed: 0
            };
            user.current_season = currentSeason;
            user.level_reset_at = new Date().toISOString();
            delete user.oopsie_used_season;

            // Add to new season leaderboard (don't touch old season — may be snapshotted)
            await redis.zadd(`leaderboard:${currentSeason}`, { score: 0, member: unified_id });
            // All-time leaderboard persists across seasons
            try { await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(user.total_xp_earned || 0), member: unified_id }); } catch (e) { console.log(`[WARN] All-time leaderboard update failed for ${unified_id}:`, e.message); }

            seasonResetPerformed = true;
        }

        // Update progression
        const oldXp = user.xp || 0;
        const oldLevel = user.level || 1;
        const clientXp = xp || 0;
        const clientLevel = level || 1;

        let newXp, newLevel;
        const hadLevelReset = seasonResetPerformed || !!user.level_reset_at || user.level_reset === true;
        if (hadLevelReset) {
            // Client is "in agreement" if it's sending values close to server (accepted the reset)
            const clientAccepted = clientLevel <= oldLevel + 2 && clientXp <= oldXp + 10000;

            if (clientAccepted) {
                // Client has accepted the reset — clear flag, resume normal merge
                delete user.level_reset_at;
                delete user.level_reset;
                newXp = Math.max(oldXp, clientXp);
                newLevel = Math.max(oldLevel, clientLevel);
            } else {
                // Client still has old cached values — server stays authoritative
                newXp = oldXp;
                newLevel = oldLevel;
                // Migrate boolean flag to timestamp if needed
                if (!user.level_reset_at) {
                    user.level_reset_at = new Date().toISOString();
                }
                // Safety: after 7 days, give up and clear (shouldn't normally happen)
                const resetAge = Date.now() - new Date(user.level_reset_at).getTime();
                if (resetAge > 7 * 24 * 60 * 60 * 1000) {
                    delete user.level_reset_at;
                    delete user.level_reset;
                }
            }
        } else {
            newXp = Math.max(oldXp, clientXp);
            newLevel = Math.max(oldLevel, clientLevel);
        }

        // --- Anti-cheat: XP delta rate limiting (Phase 1A) ---
        const now = Date.now();
        const lastSyncAt = user.last_sync_at ? new Date(user.last_sync_at).getTime() : (now - 24 * 60 * 60 * 1000);
        const hoursSinceLastSync = Math.min(168, Math.max(0.01, (now - lastSyncAt) / (1000 * 60 * 60)));
        const MAX_XP_PER_HOUR = 100000;
        const MAX_XP_PER_SYNC = 50000;

        // R8/M23: Pre-cap arrays in case Redis record was manipulated
        if (user.anti_cheat_flags && user.anti_cheat_flags.length > 100) {
            user.anti_cheat_flags = user.anti_cheat_flags.slice(-50);
        }
        if (user.xp_rate && user.xp_rate.hourly_samples && user.xp_rate.hourly_samples.length > 100) {
            user.xp_rate.hourly_samples = user.xp_rate.hourly_samples.slice(-48);
        }

        let xpWasClamped = false;
        const xpDelta = newXp - oldXp;
        if (xpDelta > 0) {
            const maxAllowedDelta = Math.max(MAX_XP_PER_SYNC, MAX_XP_PER_HOUR * Math.min(hoursSinceLastSync, 24));
            if (xpDelta > maxAllowedDelta) {
                const clampedXp = oldXp + maxAllowedDelta;
                console.log(`[Anti-cheat] XP clamped for ${unified_id}: tried +${xpDelta}, allowed +${Math.round(maxAllowedDelta)}, clamped to ${Math.round(clampedXp)} (was ${oldXp})`);
                if (!user.anti_cheat_flags) user.anti_cheat_flags = [];
                user.anti_cheat_flags.push({
                    type: 'xp_delta_exceeded',
                    at: new Date().toISOString(),
                    attempted_xp: newXp,
                    clamped_xp: Math.round(clampedXp),
                    delta: xpDelta,
                    max_allowed: Math.round(maxAllowedDelta),
                    hours_since_sync: Math.round(hoursSinceLastSync * 100) / 100
                });
                // Keep only last 50 flags
                if (user.anti_cheat_flags.length > 50) {
                    user.anti_cheat_flags = user.anti_cheat_flags.slice(-50);
                }
                newXp = Math.round(clampedXp);
                xpWasClamped = true;
            }
        }

        user.last_sync_at = new Date().toISOString();

        // --- Anti-cheat: XP rate tracking (Phase 3B) ---
        if (xpDelta > 0) {
            if (!user.xp_rate) user.xp_rate = { hourly_samples: [] };
            user.xp_rate.hourly_samples.push({
                rate: Math.round(xpDelta / Math.max(hoursSinceLastSync, 0.01)),
                delta: xpDelta,
                hours: Math.round(hoursSinceLastSync * 100) / 100,
                at: new Date().toISOString(),
                hmac: hmacValid || false
            });
            // Keep only last 48 samples
            if (user.xp_rate.hourly_samples.length > 48) {
                user.xp_rate.hourly_samples = user.xp_rate.hourly_samples.slice(-48);
            }
        }

        // --- Anti-cheat: Level/XP consistency check (Phase 1B) ---
        // Always validate level against (possibly clamped) XP to prevent level spoofing
        if (newLevel > 1) {
            const cumulativeForPrevLevel = getCumulativeXPForLevel(newLevel - 1);
            const cumulativeForLevel = getCumulativeXPForLevel(newLevel);
            if (newXp < cumulativeForPrevLevel || newXp > cumulativeForLevel) {
                // Recalculate level from XP (trust XP over level)
                let recalcLevel = 1;
                let cumulativeCheck = 0;
                while (recalcLevel < 999) {
                    cumulativeCheck += getXPForLevel(recalcLevel);
                    if (newXp < cumulativeCheck) break;
                    recalcLevel++;
                }
                if (recalcLevel !== newLevel) {
                    console.log(`[Anti-cheat] Level/XP mismatch for ${unified_id}: level=${newLevel} xp=${newXp}, recalculated level=${recalcLevel}${xpWasClamped ? ' (XP was clamped)' : ''}`);
                    if (!user.anti_cheat_flags) user.anti_cheat_flags = [];
                    user.anti_cheat_flags.push({
                        type: 'level_xp_mismatch',
                        at: new Date().toISOString(),
                        claimed_level: newLevel,
                        xp: newXp,
                        recalculated_level: recalcLevel
                    });
                    if (user.anti_cheat_flags.length > 50) {
                        user.anti_cheat_flags = user.anti_cheat_flags.slice(-50);
                    }
                    newLevel = recalcLevel;
                }
            }
        }

        user.xp = newXp;
        user.level = newLevel;
        user.last_seen = new Date().toISOString();
        user.updated_at = new Date().toISOString();

        // Track all-time XP earned (server-computed, monotonically increasing)
        const acceptedXpDelta = newXp - oldXp;
        if (acceptedXpDelta > 0) {
            user.total_xp_earned = (user.total_xp_earned || 0) + acceptedXpDelta;
        }

        // Update highest_level_ever for unlock tracking
        user.highest_level_ever = Math.min(999, Math.max(user.highest_level_ever || 0, newLevel));

        // Recalculate unlocks based on highest_level_ever
        user.unlocks = calculateUnlocks(user.highest_level_ever);

        // Merge achievements (union of local and cloud, capped at 500)
        if (achievements && Array.isArray(achievements)) {
            const existingAchievements = new Set(user.achievements || []);
            for (const ach of achievements) {
                if (typeof ach === 'string' && ach.length <= 100) {
                    existingAchievements.add(ach);
                }
            }
            user.achievements = Array.from(existingAchievements).slice(0, 500);
        }

        // Stats keys that are controlled by force_streak_override
        const STREAK_STAT_KEYS = new Set([
            'daily_quest_streak', 'last_daily_quest_date', 'quest_completion_dates',
            'total_daily_quests_completed', 'total_weekly_quests_completed', 'total_xp_from_quests'
        ]);
        const hasForceStreakOverride = user.force_streak_override === true;

        // --- Anti-cheat: Stats delta caps (Phase 1C) ---
        const STATS_MAX_PER_HOUR = {
            total_flashes: 700,
            total_bubbles_popped: 600,
            total_video_minutes: 70,
            total_lock_cards_completed: 30,
            completed_sessions: 5,
            longest_session_minutes: 480,
            // Quest stats (R8/C1 — were being silently dropped by unknown-key rejection)
            daily_quest_streak: 7,
            total_daily_quests_completed: 24,
            total_weekly_quests_completed: 4,
            total_xp_from_quests: 50000,
            daily_quests_completed_today: 3
        };

        // Capture old bubbles BEFORE stats merge for server-side skill point awarding
        const oldBubbles = (user.stats && user.stats.total_bubbles_popped) || 0;

        // Merge stats (take HIGHER values, but skip streak stats if force_streak_override is active)
        if (stats && typeof stats === 'object') {
            user.stats = user.stats || {};
            for (const [key, value] of Object.entries(stats)) {
                // Prevent prototype pollution and reserved JS property shadowing
                if (key === '__proto__' || key === 'constructor' || key === 'prototype' || key === 'toString' || key === 'valueOf' || key === 'hasOwnProperty' || key === 'isPrototypeOf') continue;
                // Skip streak stats when admin has force-set them - preserve admin values
                if (hasForceStreakOverride && STREAK_STAT_KEYS.has(key)) {
                    continue;
                }

                // Handle non-numeric stat fields (date strings, arrays) — these can't go through Math.max
                if (key === 'last_daily_quest_date' || key === 'daily_completion_reset_date') {
                    // Take the more recent date string (lexicographic compare works for YYYY-MM-DD)
                    const clientDate = typeof value === 'string' && value.length <= 20 ? value : '';
                    const serverDate = typeof user.stats[key] === 'string' ? user.stats[key] : '';
                    if (clientDate >= serverDate) {
                        user.stats[key] = clientDate;
                    }
                    continue;
                }
                if (key === 'quest_completion_dates') {
                    // Take the longer array (more completion dates = more progress), cap at 100 entries
                    // R8/M24: Filter to valid date strings only
                    const clientDates = Array.isArray(value) ? value.filter(d => typeof d === 'string' && d.length <= 20) : [];
                    const serverDates = Array.isArray(user.stats[key]) ? user.stats[key] : [];
                    const merged = clientDates.length >= serverDates.length ? clientDates : serverDates;
                    user.stats[key] = merged.slice(-100);
                    continue;
                }

                const numValue = Number(value) || 0;
                const existingValue = Number(user.stats[key]) || 0;

                if (Object.prototype.hasOwnProperty.call(STATS_MAX_PER_HOUR, key)) {
                    // Known stat: cap the delta
                    const statDelta = numValue - existingValue;
                    if (statDelta > 0) {
                        const maxStatDelta = STATS_MAX_PER_HOUR[key] * Math.min(hoursSinceLastSync, 24);
                        if (statDelta > maxStatDelta) {
                            user.stats[key] = Math.round(existingValue + maxStatDelta);
                            console.log(`[Anti-cheat] Stat ${key} clamped for ${unified_id}: tried +${statDelta}, allowed +${Math.round(maxStatDelta)}`);
                            if (!user.anti_cheat_flags) user.anti_cheat_flags = [];
                            user.anti_cheat_flags.push({
                                type: 'stat_delta_exceeded',
                                at: new Date().toISOString(),
                                stat: key,
                                attempted_delta: statDelta,
                                max_allowed: Math.round(maxStatDelta)
                            });
                            if (user.anti_cheat_flags.length > 50) {
                                user.anti_cheat_flags = user.anti_cheat_flags.slice(-50);
                            }
                        } else {
                            user.stats[key] = numValue;
                        }
                    }
                    // If statDelta <= 0, keep existing (don't decrease)
                } else {
                    // Unknown keys: reject to prevent unbounded stats growth
                    // If a new stat is needed, add it to STATS_MAX_PER_HOUR
                    continue;
                }
            }
        }

        // Update settings
        if (typeof allow_discord_dm === 'boolean') {
            user.allow_discord_dm = allow_discord_dm;
        }
        if (typeof show_online_status === 'boolean') {
            user.show_online_status = show_online_status;
        }
        if (typeof share_profile_picture === 'boolean') {
            user.share_profile_picture = share_profile_picture;
        }

        // Merge unlocked skills — but NOT during a level reset or skills reset
        // (client may be pushing old cached values from before the reset)
        const pendingSkillsReset = user.force_skills_reset === true;
        if (!hadLevelReset && !pendingSkillsReset) {
            if (unlocked_skills && Array.isArray(unlocked_skills)) {
                const existingSkills = new Set(user.unlocked_skills || []);
                for (const skill of unlocked_skills) {
                    if (typeof skill === 'string' && skill.length <= 50) {
                        existingSkills.add(skill);
                    }
                }
                user.unlocked_skills = Array.from(existingSkills).slice(0, 200);
            }

            // Server-authoritative skill points — award based on level-up and bubble milestones
            // Client value is NOT trusted; server computes points from observed deltas
            const newLevel = user.level || 1;
            const levelDelta = Math.max(0, newLevel - oldLevel);
            if (levelDelta > 0) {
                user.skill_points = (user.skill_points || 0) + (levelDelta * POINTS_PER_LEVEL);
            }

            const newBubbles = (user.stats && user.stats.total_bubbles_popped) || 0;
            const oldMilestones = Math.floor(oldBubbles / 100);
            const newMilestones = Math.floor(newBubbles / 100);
            const bubbleDelta = Math.max(0, newMilestones - oldMilestones);
            if (bubbleDelta > 0) {
                user.skill_points = (user.skill_points || 0) + bubbleDelta;
            }

            // Cap skill_points at 9999
            user.skill_points = Math.min(9999, user.skill_points || 0);
        }

        // Merge total conditioning minutes (lifetime value — always take higher)
        if (typeof total_conditioning_minutes === 'number' && total_conditioning_minutes > 0) {
            const existingMinutes = user.total_conditioning_minutes || 0;
            // R8/M16: Cap at 10M minutes (~19 years)
            user.total_conditioning_minutes = Math.min(10000000, Math.max(existingMinutes, total_conditioning_minutes));
        }

        // Merge companion progress (per-companion, higher level/XP wins, capped at 50 companions)
        const MAX_COMPANION_KEYS = 50;
        if (companion_progress && typeof companion_progress === 'object') {
            user.companion_progress = user.companion_progress || {};
            for (const [companionId, clientData] of Object.entries(companion_progress)) {
                // Prevent prototype pollution
                if (companionId === '__proto__' || companionId === 'constructor' || companionId === 'prototype' || companionId === 'toString' || companionId === 'valueOf' || companionId === 'hasOwnProperty' || companionId === 'isPrototypeOf') continue;
                // R8/M15: Validate key length; R11/M2: Validate key content (alphanumeric/hyphens/underscores only)
                if (typeof companionId !== 'string' || companionId.length > 100 || !/^[a-zA-Z0-9_\-]+$/.test(companionId)) continue;
                // R8/M14: Validate clientData structure
                if (!clientData || typeof clientData !== 'object' || Array.isArray(clientData)) continue;
                // Cap total companion keys
                if (!(companionId in user.companion_progress) && Object.keys(user.companion_progress).length >= MAX_COMPANION_KEYS) continue;
                const existing = user.companion_progress[companionId] || {};
                const rawLevel = Number(clientData?.Level);
                const rawXP = Number(clientData?.TotalXPEarned);
                // R11/C2: Guard against NaN/Infinity and cap numeric values
                const clientLevel = (!isFinite(rawLevel) || rawLevel < 1) ? 1 : Math.min(9999, Math.floor(rawLevel));
                const clientTotalXP = (!isFinite(rawXP) || rawXP < 0) ? 0 : Math.min(1e9, Math.floor(rawXP));
                const existingLevel = Number(existing.Level) || 1;
                const existingTotalXP = Number(existing.TotalXPEarned) || 0;

                // Sanitize: only store known fields (R8/M14)
                const sanitizedData = { Level: clientLevel, TotalXPEarned: clientTotalXP };

                // Take the more progressed version (higher level, or if same level, higher XP)
                if (clientLevel > existingLevel || (clientLevel === existingLevel && clientTotalXP > existingTotalXP)) {
                    user.companion_progress[companionId] = sanitizedData;
                } else if (!existing.Level) {
                    // First time seeing this companion
                    user.companion_progress[companionId] = sanitizedData;
                }
            }
        }

        // Handle quest reset flags - capture before clearing
        const pendingResetWeekly = user.reset_weekly_quest === true;
        const pendingResetDaily = user.reset_daily_quest === true;

        // Clear reset flags when client acknowledges (sends false)
        if (reset_weekly_quest === false) {
            delete user.reset_weekly_quest;
        }
        if (reset_daily_quest === false) {
            delete user.reset_daily_quest;
        }

        // Handle force_streak_override flag - capture before clearing
        const pendingForceStreakOverride = hasForceStreakOverride;

        // Clear force_streak_override when client acknowledges (sends false)
        if (clientForceStreakOverride === false) {
            delete user.force_streak_override;
        }

        // Clear force_skills_reset when client acknowledges (sends false)
        if (clientForceSkillsReset === false) {
            delete user.force_skills_reset;
        }

        // Capture AFTER clearing so the ack response doesn't echo back true
        const pendingForceSkillsReset = user.force_skills_reset === true;

        // Save updated user
        await redis.set(`user:${unified_id}`, JSON.stringify(user));

        // Update leaderboard sorted set with new XP
        const season = user.current_season || getCurrentSeason();
        await redis.zadd(`leaderboard:${season}`, { score: safeLeaderboardScore(newXp), member: unified_id });
        try { await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(user.total_xp_earned || 0), member: unified_id }); } catch (e) { console.log(`[WARN] All-time leaderboard update failed for ${unified_id}:`, e.message); }

        console.log(`[V2 Sync] User ${unified_id}: Level ${oldLevel}->${newLevel}, XP ${oldXp}->${newXp}`);

        res.json({
            success: true,
            user: {
                unified_id: user.unified_id,
                display_name: user.display_name,
                level: user.level,
                xp: user.xp,
                achievements: user.achievements,
                highest_level_ever: user.highest_level_ever,
                unlocks: user.unlocks
            },
            total_conditioning_minutes: user.total_conditioning_minutes || 0,
            companion_progress: user.companion_progress || {},
            merged: oldXp !== newXp || oldLevel !== newLevel,
            // Include pending reset flags so client can process them
            reset_weekly_quest: pendingResetWeekly,
            reset_daily_quest: pendingResetDaily,
            // Include force_streak_override flag and streak stats so client can adopt them
            force_streak_override: pendingForceStreakOverride,
            ...(pendingForceStreakOverride ? {
                streak_stats: {
                    daily_quest_streak: user.stats?.daily_quest_streak || 0,
                    last_daily_quest_date: user.stats?.last_daily_quest_date || '',
                    quest_completion_dates: user.stats?.quest_completion_dates || [],
                    total_daily_quests_completed: user.stats?.total_daily_quests_completed || 0,
                    total_weekly_quests_completed: user.stats?.total_weekly_quests_completed || 0,
                    total_xp_from_quests: user.stats?.total_xp_from_quests || 0
                }
            } : {}),
            // Include OG status so client stays in sync (e.g. if set retroactively by admin)
            is_season0_og: user.is_season0_og || false,
            // Include oopsie insurance season usage
            oopsie_used_season: user.oopsie_used_season || null,
            // Always include skill_points and unlocked_skills so client stays in sync
            skill_points: user.skill_points || 0,
            unlocked_skills: user.unlocked_skills || [],
            // Include force_skills_reset flag
            force_skills_reset: pendingForceSkillsReset,
            // Level reset flag — tells client to accept server level/xp
            level_reset: hadLevelReset,
            // Include server's authoritative level/xp when reset is active
            ...(hadLevelReset ? { server_level: user.level, server_xp: user.xp } : {}),
            // Include whitelist status so Discord-only users get Patreon features without OAuth
            patreon_is_whitelisted: user.patreon_is_whitelisted || false,
            // Include bonus rerolls (admin-granted extra rerolls)
            bonus_daily_rerolls: user.bonus_daily_rerolls || 0,
            bonus_weekly_rerolls: user.bonus_weekly_rerolls || 0
        });
    } catch (error) {
        console.error('V2 sync error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/user/heartbeat
 * Update user's last_seen timestamp (for online status)
 * Body: { unified_id: string }
 */
app.post('/v2/user/heartbeat', async (req, res) => {
    try {
        const { unified_id, is_active, in_session, app_version } = req.body;

        if (!unified_id) {
            return res.status(400).json({ error: 'unified_id required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Soft auth for heartbeat: require valid token if user has auth set up,
        // but allow truly legacy users (no auth_token_hash) through so they show as online.
        const token = req.headers['x-auth-token'];
        if (user.auth_token_hash) {
            // User has auth — require valid token
            if (!token) {
                return res.status(401).json({ error: 'Auth token required' });
            }
            const hash = crypto.createHash('sha256').update(token).digest('hex');
            if (!timingSafeCompare(hash, user.auth_token_hash)) {
                return res.status(401).json({ error: 'Invalid auth token' });
            }
        }
        // Legacy users (no auth_token_hash) pass through — they'll get a token on next login

        // Per-user cooldown: 30s minimum between heartbeats (after auth to prevent unauthenticated abuse)
        // R12/M7: Atomic NX cooldown to prevent TOCTOU race
        const hbCooldownKey = `heartbeat_cooldown:${unified_id}`;
        const hbCooldownSet = await redis.set(hbCooldownKey, '1', { nx: true, ex: 30 });
        if (!hbCooldownSet) {
            return res.status(429).json({ error: 'Heartbeat cooldown active', retry_after: 30 });
        }

        user.last_seen = new Date().toISOString();

        // Store enriched heartbeat data for anti-cheat cross-referencing
        user.last_heartbeat = {
            at: user.last_seen,
            is_active: is_active === true || is_active === false ? is_active : null,
            in_session: in_session === true || in_session === false ? in_session : null,
            app_version: (typeof app_version === 'string' ? app_version.slice(0, 50) : null)
        };

        await redis.set(`user:${unified_id}`, JSON.stringify(user));

        // Track in online_users sorted set for accurate online count
        await trackUserOnline(unified_id, user.show_online_status);

        res.json({ success: true, last_seen: user.last_seen });
    } catch (error) {
        console.error('V2 heartbeat error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/user/backup-settings
 * Store a compressed backup of the user's local app settings in Redis.
 * Body: { unified_id: string, settings_data: string (base64 gzip), app_version: string }
 */
app.post('/v2/user/backup-settings', async (req, res) => {
    try {
        const { unified_id, settings_data, app_version } = req.body;

        if (!unified_id) {
            return res.status(400).json({ error: 'unified_id required' });
        }
        if (!settings_data) {
            return res.status(400).json({ error: 'settings_data required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Enforce 700KB size limit on base64 string
        if (settings_data.length > 700 * 1024) {
            return res.status(400).json({ error: 'settings_data too large (max 700KB)' });
        }

        // Verify user exists and auth BEFORE cooldown check (prevents unauthenticated Redis reads)
        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // Auth token validation (moved before cooldown to prevent unauthenticated Redis abuse)
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }
        if (authCheck.legacy) {
            console.log(`[AUTH] ${authCheck.missing ? 'Old client (token not sent)' : 'Legacy user (no token hash)'} for ${unified_id} on ${req.path}`);
        }

        // R11/M6: Atomic cooldown check-and-set (prevents TOCTOU race)
        const cooldownKey = `backup_cooldown:${unified_id}`;
        const cooldownAcquired = await redis.set(cooldownKey, '1', { nx: true, ex: 30 });
        if (!cooldownAcquired) {
            return res.status(429).json({ error: 'Backup cooldown active', retry_after: 30 });
        }

        const backup = {
            settings_data,
            app_version: app_version || 'unknown',
            backed_up_at: new Date().toISOString(),
            size_bytes: settings_data.length
        };

        await redis.set(`settings_backup:${unified_id}`, JSON.stringify(backup));

        console.log(`[SettingsBackup] User ${unified_id} backed up settings (${settings_data.length} bytes, v${app_version})`);

        res.json({
            success: true,
            backed_up_at: backup.backed_up_at,
            size_bytes: backup.size_bytes
        });
    } catch (error) {
        console.error('Settings backup error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/user/settings-backup
 * Retrieve a user's settings backup from Redis.
 * Body: { unified_id: string }
 */
app.post('/v2/user/settings-backup', async (req, res) => {
    try {
        const { unified_id } = req.body;

        if (!unified_id) {
            return res.status(400).json({ error: 'unified_id required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Auth token validation — always required, even if user record is missing
        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) {
            return res.status(404).json({ error: 'User not found' });
        }
        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }

        const backupData = await redis.get(`settings_backup:${unified_id}`);
        if (!backupData) {
            return res.json({ success: true, backup: null });
        }

        const backup = typeof backupData === 'string' ? JSON.parse(backupData) : backupData;

        console.log(`[SettingsBackup] User ${unified_id} retrieved settings backup (${backup.size_bytes} bytes, v${backup.app_version})`);

        res.json({
            success: true,
            backup: {
                settings_data: backup.settings_data,
                app_version: backup.app_version,
                backed_up_at: backup.backed_up_at,
                size_bytes: backup.size_bytes
            }
        });
    } catch (error) {
        console.error('Settings backup retrieve error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/user/purchase-skill
 * Server-authoritative skill purchase. Validates cost, prereqs, deducts points, adds skill.
 * Body: { unified_id, skill_id, current_level?, total_bubbles_popped? }
 * Response: { success, skill_points, unlocked_skills } or { success: false, error, skill_points }
 */
app.post('/v2/user/purchase-skill', async (req, res) => {
    try {
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Rate limit: 10/min/IP
        const ip = req.ip || 'unknown';
        const purchaseRateKey = `purchase_skill_rate:${ip}`;
        try {
            await redis.set(purchaseRateKey, 0, { nx: true, ex: 60 });
            const rateCount = await redis.incr(purchaseRateKey);
            if (rateCount > 10) {
                return res.status(429).json({ error: 'Too many requests. Try again in a minute.' });
            }
        } catch (e) {
            return res.status(429).json({ error: 'Rate limit check failed' });
        }

        const { unified_id, skill_id } = req.body || {};

        if (!unified_id || !isValidUnifiedId(unified_id)) {
            return res.status(400).json({ error: 'Invalid unified_id' });
        }
        if (typeof skill_id !== 'string' || skill_id.length > 50 || !SKILL_CATALOG[skill_id]) {
            return res.status(400).json({ error: 'Invalid skill_id' });
        }

        // Per-user lock to prevent TOCTOU race condition (concurrent double-purchase)
        const lockKey = `purchase_lock:${unified_id}`;
        const lockAcquired = await redis.set(lockKey, '1', { nx: true, ex: 10 });
        if (!lockAcquired) {
            return res.status(429).json({ error: 'Purchase already in progress. Please wait.' });
        }

        try {
            const userRaw = await redis.get(`user:${unified_id}`);
            if (!userRaw) {
                return res.status(401).json({ error: 'Invalid or missing auth token' });
            }
            const user = typeof userRaw === 'string' ? JSON.parse(userRaw) : userRaw;

            const authCheck = validateAuthToken(req, user);
            if (!authCheck.valid) {
                return res.status(401).json({ error: 'Invalid or missing auth token' });
            }

            // Block purchases during pending skills reset (admin has wiped skills)
            if (user.force_skills_reset === true) {
                return res.json({ success: false, error: 'Skills are being reset. Please sync first.', skill_points: user.skill_points || 0 });
            }

            // Server uses its own stored values — no client-supplied level/bubbles reconciliation.
            // Points are awarded by the sync endpoint when level-ups and bubble milestones occur.

            // Check already owned
            user.unlocked_skills = user.unlocked_skills || [];
            if (user.unlocked_skills.includes(skill_id)) {
                return res.json({ success: false, error: 'Skill already owned', skill_points: user.skill_points || 0 });
            }

            // Check prerequisite
            const skillDef = SKILL_CATALOG[skill_id];
            if (skillDef.prereq && !user.unlocked_skills.includes(skillDef.prereq)) {
                return res.json({ success: false, error: 'Prerequisite not met', skill_points: user.skill_points || 0 });
            }

            // Check sufficient points
            if ((user.skill_points || 0) < skillDef.cost) {
                return res.json({ success: false, error: 'Not enough sparkle points. Try syncing first to receive pending points.', skill_points: user.skill_points || 0 });
            }

            // Purchase: deduct cost and add skill
            user.skill_points = Math.max(0, (user.skill_points || 0) - skillDef.cost);
            user.skill_points = Math.min(9999, user.skill_points);
            user.unlocked_skills.push(skill_id);
            user.updated_at = new Date().toISOString();

            await redis.set(`user:${unified_id}`, JSON.stringify(user));

            console.log(`[PurchaseSkill] User ${unified_id} purchased ${skill_id} for ${skillDef.cost} points, ${user.skill_points} remaining`);

            res.json({
                success: true,
                skill_points: user.skill_points,
                unlocked_skills: user.unlocked_skills
            });
        } finally {
            // Always release the lock
            await redis.del(lockKey).catch(() => {});
        }
    } catch (error) {
        console.error('Purchase skill error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/easter-egg
 * Record a unique easter egg reader and return the total count.
 * If unified_id provided: validates auth, adds user to Set, returns count.
 * If unified_id omitted: returns count only (read-only, no auth).
 * Response: { count: <number> }
 */
app.post('/v2/easter-egg', async (req, res) => {
    try {
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const REDIS_KEY = 'easter_egg_readers';
        const BASE_COUNT = 57;

        // Per-IP rate limit: 10 per minute
        const ip = req.ip || 'unknown';
        const eeRateKey = `easter_egg_rate:${ip}`;
        try {
            await redis.set(eeRateKey, 0, { nx: true, ex: 60 });
            const eeCount = await redis.incr(eeRateKey);
            if (eeCount > 10) {
                return res.status(429).json({ error: 'Too many requests. Try again in a minute.' });
            }
        } catch (e) {
            return res.status(429).json({ error: 'Rate limit check failed' });
        }

        const { unified_id } = req.body || {};

        if (unified_id) {
            // Authenticated path: validate and record the reader
            if (!isValidUnifiedId(unified_id)) {
                return res.status(400).json({ error: 'Invalid unified_id' });
            }

            const userRaw = await redis.get(`user:${unified_id}`);
            if (!userRaw) {
                // Return 401 (not 404) to prevent user enumeration
                return res.status(401).json({ error: 'Invalid or missing auth token' });
            }

            const user = typeof userRaw === 'string' ? JSON.parse(userRaw) : userRaw;
            const authCheck = validateAuthToken(req, user);
            if (!authCheck.valid) {
                return res.status(401).json({ error: 'Invalid or missing auth token' });
            }

            // Add user to the set (idempotent)
            await redis.sadd(REDIS_KEY, unified_id);
        }

        // Return total unique readers + base
        const rawCount = await redis.scard(REDIS_KEY);
        const actualReaders = (typeof rawCount === 'number' && isFinite(rawCount)) ? rawCount : 0;
        res.json({ count: actualReaders + BASE_COUNT });
    } catch (error) {
        console.error('Easter egg endpoint error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /admin/import-patreon-members
 * Import Patreon members from CSV export as Season 0 OG users
 * Body: { admin_token: string, members: Array<{name, email, patreon_id}>, dry_run?: boolean }
 */
app.post('/admin/import-patreon-members', async (req, res) => {
    try {
        const { members, dry_run = true } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!members || !Array.isArray(members)) {
            return res.status(400).json({ error: 'members array required' });
        }

        const imported = [];
        const skipped = [];
        const errors = [];

        for (const member of members) {
            try {
                const { name, email, patreon_id } = member;

                if (!patreon_id) {
                    skipped.push({ name, reason: 'no patreon_id' });
                    continue;
                }

                // Check if season0 entry already exists
                const existingKey = `season0:patreon:${patreon_id}`;
                const existing = await redis.get(existingKey);

                if (existing) {
                    skipped.push({ name, patreon_id, reason: 'already exists' });
                    continue;
                }

                // Create minimal season0 entry for this Patreon member
                // They get OG status even if they never logged into the app
                const season0Data = {
                    display_name: name || null,
                    patreon_id: patreon_id,
                    discord_id: null,
                    email_hash: hashEmail(email) || null,
                    highest_level_ever: 0,  // Never played, but still OG
                    achievements: [],
                    stats: {
                        total_flashes: 0,
                        total_bubbles_popped: 0,
                        total_video_minutes: 0,
                        total_lock_cards_completed: 0
                    },
                    unlocks: calculateUnlocks(0),
                    patreon_tier: 0,
                    patreon_is_active: false,
                    patreon_is_whitelisted: false,
                    is_patreon_member: true,  // Flag that this came from Patreon export
                    captured_at: new Date().toISOString()
                };

                if (!dry_run) {
                    await redis.set(existingKey, JSON.stringify(season0Data));
                }

                imported.push({
                    name,
                    patreon_id,
                    has_email: !!email
                });
            } catch (e) {
                errors.push({ member, error: e.message });
            }
        }

        console.log(`[ADMIN] Import Patreon members: ${imported.length} imported, ${skipped.length} skipped (dry_run: ${dry_run})`);

        res.json({
            dry_run,
            total_input: members.length,
            imported_count: imported.length,
            skipped_count: skipped.length,
            errors_count: errors.length,
            imported,
            skipped,
            errors
        });
    } catch (error) {
        console.error('Admin import-patreon-members error:', error.message);
        res.status(500).json({ error: 'Failed to import Patreon members' });
    }
});

// =============================================================================
// MARQUEE BANNER CONFIG
// =============================================================================

const MARQUEE_KEY = 'config:marquee_message';
const DEFAULT_MARQUEE = 'GOOD GIRLS CONDITION DAILY     ❤️🔒';

/**
 * GET /config/marquee
 * Returns the current marquee banner message (public, no auth required)
 */
app.get('/config/marquee', async (req, res) => {
    try {
        const message = await redis.get(MARQUEE_KEY);
        res.json({ message: message || DEFAULT_MARQUEE });
    } catch (error) {
        console.error('Get marquee error:', error.message);
        res.json({ message: DEFAULT_MARQUEE });
    }
});

/**
 * POST /config/marquee
 * Set the marquee banner message (admin only - requires specific admin token)
 * Body: { message: string, admin_token: string }
 */
app.post('/config/marquee', async (req, res) => {
    try {
        const { message } = req.body;
        const admin_token = req.headers['x-admin-token'];

        // Simple admin token check (set ADMIN_TOKEN in environment)
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        if (!message || typeof message !== 'string') {
            return res.status(400).json({ error: 'Message is required' });
        }

        const trimmedMessage = message.trim();
        if (trimmedMessage.length > 200) {
            return res.status(400).json({ error: 'Message too long (max 200 characters)' });
        }

        await redis.set(MARQUEE_KEY, trimmedMessage);
        console.log(`Marquee message updated to: ${trimmedMessage}`);

        res.json({ success: true, message: trimmedMessage });
    } catch (error) {
        console.error('Set marquee error:', error.message);
        res.status(500).json({ error: 'Failed to update marquee' });
    }
});

// =============================================================================
// UPDATE BANNER CONFIG (Server-controlled update notification fallback)
// =============================================================================

const UPDATE_BANNER_KEY = 'config:update_banner';

/**
 * GET /config/update-banner
 * Returns update banner configuration (public, no auth required)
 * Response: { enabled: boolean, version: string, message: string }
 */
app.get('/config/update-banner', async (req, res) => {
    try {
        const data = await redis.get(UPDATE_BANNER_KEY);
        if (data) {
            const config = typeof data === 'string' ? JSON.parse(data) : data;
            res.json(config);
        } else {
            // Default: no urgent update
            res.json({ enabled: false, version: '', message: '' });
        }
    } catch (error) {
        console.error('Get update-banner error:', error.message);
        res.json({ enabled: false, version: '', message: '' });
    }
});

/**
 * POST /config/update-banner
 * Set the update banner configuration (admin only)
 * Body: { enabled: boolean, version: string, message: string, url: string, admin_token: string }
 *
 * Examples:
 * - Enable: { "enabled": true, "version": "5.3", "message": "v5.3 is out!", "url": "https://example.com", "admin_token": "xxx" }
 * - Disable: { "enabled": false, "admin_token": "xxx" }
 */
app.post('/config/update-banner', async (req, res) => {
    try {
        const { enabled, version, message, url } = req.body;
        const admin_token = req.headers['x-admin-token'];

        // Simple admin token check
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        const config = {
            enabled: !!enabled,
            version: version || '',
            message: message || 'UPDATE AVAILABLE!',
            url: url || ''
        };

        await redis.set(UPDATE_BANNER_KEY, JSON.stringify(config));
        console.log(`Update banner config updated:`, config);

        res.json({ success: true, config });
    } catch (error) {
        console.error('Set update-banner error:', error.message);
        res.status(500).json({ error: 'Failed to update banner config' });
    }
});

// =============================================================================
// SERVER-TRIGGERED ANNOUNCEMENT POPUP
// =============================================================================

const ANNOUNCEMENT_KEY = 'config:announcement';

/**
 * GET /config/announcement
 * Public endpoint to fetch the current announcement.
 * Optional query param: unified_id — if provided, checks for a per-user announcement first.
 * Returns: { enabled, id, title, message, image_url }
 */
app.get('/config/announcement', async (req, res) => {
    try {
        const { unified_id } = req.query;

        // Check for per-user announcement first (validate format to prevent Redis key injection)
        if (unified_id && redis && isValidUnifiedId(unified_id)) {
            const userKey = `user:${unified_id}`;
            const userData = await redis.get(userKey);
            if (userData) {
                const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
                if (user.announcement && user.announcement.enabled) {
                    return res.json({
                        enabled: true,
                        id: user.announcement.id,
                        title: user.announcement.title || '',
                        message: user.announcement.message || '',
                        image_url: user.announcement.image_url || null
                    });
                }
            }
        }

        // Fall back to global announcement
        if (!redis) {
            return res.json({ enabled: false });
        }

        const data = await redis.get(ANNOUNCEMENT_KEY);
        if (!data) {
            return res.json({ enabled: false });
        }

        const config = typeof data === 'string' ? JSON.parse(data) : data;
        res.json({
            enabled: !!config.enabled,
            id: config.id || '',
            title: config.title || '',
            message: config.message || '',
            image_url: config.image_url || null
        });
    } catch (error) {
        console.error('Get announcement error:', error.message);
        res.json({ enabled: false });
    }
});

/**
 * POST /config/announcement
 * Admin-only: set the global announcement shown to all users.
 * Body: { enabled, id?, title, message, image_url?, admin_token }
 * If id is omitted, auto-generates from timestamp.
 */
app.post('/config/announcement', async (req, res) => {
    try {
        const { enabled, id, title, message, image_url } = req.body;
        const admin_token = req.headers['x-admin-token'];

        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        // Validate image_url is HTTPS if provided
        if (image_url && (typeof image_url !== 'string' || !image_url.startsWith('https://'))) {
            return res.status(400).json({ error: 'image_url must be an HTTPS URL' });
        }

        // R11/H5: Length caps on announcement fields
        const safeId = id ? String(id).slice(0, 50) : `ann_${Date.now()}_${crypto.randomBytes(4).toString('hex')}`;
        const safeTitle = title ? String(title).slice(0, 200) : '';
        const safeMessage = message ? String(message).slice(0, 2000) : '';
        if (image_url && image_url.length > 500) {
            return res.status(400).json({ error: 'image_url too long (max 500 chars)' });
        }

        const config = {
            enabled: !!enabled,
            id: safeId,
            title: safeTitle,
            message: safeMessage,
            image_url: image_url || null
        };

        await redis.set(ANNOUNCEMENT_KEY, JSON.stringify(config));
        console.log(`Announcement config updated:`, config);

        res.json({ success: true, config });
    } catch (error) {
        console.error('Set announcement error:', error.message);
        res.status(500).json({ error: 'Failed to update announcement config' });
    }
});

/**
 * POST /admin/user-announcement
 * Admin-only: set a per-user announcement targeting a specific user by display_name.
 * Body: { display_name, enabled, id?, title, message, image_url?, admin_token }
 * Stores announcement object inside the user's profile in Redis.
 */
app.post('/admin/user-announcement', async (req, res) => {
    try {
        const { display_name, enabled, id, title, message, image_url } = req.body;
        const admin_token = req.headers['x-admin-token'];

        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        if (!display_name) {
            return res.status(400).json({ error: 'display_name is required' });
        }

        // Validate image_url is HTTPS if provided
        if (image_url && (typeof image_url !== 'string' || !image_url.startsWith('https://'))) {
            return res.status(400).json({ error: 'image_url must be an HTTPS URL' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        // Find user by display_name index
        const indexKey = `display_name_index:${display_name.trim().toLowerCase()}`;
        const unifiedId = await redis.get(indexKey);

        if (!unifiedId) {
            return res.status(404).json({ error: `User "${display_name}" not found` });
        }

        const userKey = `user:${unifiedId}`;
        const userData = await redis.get(userKey);

        if (!userData) {
            return res.status(404).json({ error: `User data for "${display_name}" not found` });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        // R11/H5: Length caps on announcement fields
        if (image_url && image_url.length > 500) {
            return res.status(400).json({ error: 'image_url too long (max 500 chars)' });
        }
        user.announcement = {
            enabled: !!enabled,
            id: id ? String(id).slice(0, 50) : `ann_user_${Date.now()}_${crypto.randomBytes(4).toString('hex')}`,
            title: title ? String(title).slice(0, 200) : '',
            message: message ? String(message).slice(0, 2000) : '',
            image_url: image_url || null
        };

        await redis.set(userKey, JSON.stringify(user));
        console.log(`[ADMIN] User announcement set for "${display_name}" (${unifiedId}):`, user.announcement);

        res.json({ success: true, unified_id: unifiedId, announcement: user.announcement });
    } catch (error) {
        console.error('Admin user-announcement error:', error.message);
        res.status(500).json({ error: 'Failed to set user announcement' });
    }
});

/**
 * POST /admin/update-user
 * Admin-only: patch specific fields on a user record.
 * Body: { admin_token, display_name, updates: { new_display_name?, patreon_is_whitelisted?, patreon_tier? } }
 */
app.post('/admin/update-user', async (req, res) => {
    try {
        const { display_name, updates } = req.body;
        const admin_token = req.headers['x-admin-token'];

        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        if (!display_name || !updates) {
            return res.status(400).json({ error: 'display_name and updates required' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        // Find user by display_name index
        const indexKey = `display_name_index:${display_name.trim().toLowerCase()}`;
        const unifiedId = await redis.get(indexKey);

        if (!unifiedId) {
            return res.status(404).json({ error: `User "${display_name}" not found` });
        }

        const userKey = `user:${unifiedId}`;
        const userData = await redis.get(userKey);

        if (!userData) {
            return res.status(404).json({ error: `User data for "${display_name}" not found` });
        }

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
        const changes = {};

        // Update display_name (also update index)
        if (updates.new_display_name && updates.new_display_name !== user.display_name) {
            const newName = String(updates.new_display_name).trim();
            if (!isValidDisplayNameChars(newName)) {
                return res.status(400).json({ error: 'new_display_name contains invalid characters' });
            }
            if (newName.length < 3 || newName.length > 20) {
                return res.status(400).json({ error: 'Display name must be 3-20 characters' });
            }

            // R11/M7: Acquire name lock to prevent race condition on concurrent renames
            const nameLockKey = `name_lock:${newName.toLowerCase()}`;
            const nameLockAcquired = await redis.set(nameLockKey, '1', { nx: true, ex: 15 });
            if (!nameLockAcquired) {
                return res.status(409).json({ error: 'Name change in progress, try again' });
            }

            try {
                // Check if new name is already taken
                const existingOwner = await redis.get(`display_name_index:${newName.toLowerCase()}`);
                if (existingOwner && existingOwner !== unifiedId) {
                    await redis.del(nameLockKey);
                    return res.status(409).json({ error: 'Display name already taken' });
                }

                const oldName = user.display_name;
                // Remove old index, add new
                await redis.del(`display_name_index:${oldName.toLowerCase()}`);
                await redis.set(`display_name_index:${newName.toLowerCase()}`, unifiedId);

                changes.display_name = { from: oldName, to: newName };
                user.display_name = newName;
            } finally {
                await redis.del(nameLockKey).catch(() => {});
            }
        }

        // Update whitelist flag
        if (typeof updates.patreon_is_whitelisted === 'boolean') {
            changes.patreon_is_whitelisted = { from: user.patreon_is_whitelisted, to: updates.patreon_is_whitelisted };
            user.patreon_is_whitelisted = updates.patreon_is_whitelisted;
            // Whitelisted users get minimum tier 2
            if (updates.patreon_is_whitelisted && (user.patreon_tier || 0) < 2) {
                changes.patreon_tier = { from: user.patreon_tier || 0, to: 2 };
                user.patreon_tier = 2;
            }
        }

        // Update tier
        if (typeof updates.patreon_tier === 'number') {
            changes.patreon_tier = { from: user.patreon_tier, to: updates.patreon_tier };
            user.patreon_tier = updates.patreon_tier;
        }

        // Update bonus rerolls
        if (typeof updates.bonus_daily_rerolls === 'number') {
            changes.bonus_daily_rerolls = { from: user.bonus_daily_rerolls || 0, to: updates.bonus_daily_rerolls };
            user.bonus_daily_rerolls = updates.bonus_daily_rerolls;
        }
        if (typeof updates.bonus_weekly_rerolls === 'number') {
            changes.bonus_weekly_rerolls = { from: user.bonus_weekly_rerolls || 0, to: updates.bonus_weekly_rerolls };
            user.bonus_weekly_rerolls = updates.bonus_weekly_rerolls;
        }

        // Update level (bounded 0-999 for data integrity)
        if (typeof updates.level === 'number') {
            const boundedLevel = Math.max(0, Math.min(999, Math.floor(updates.level)));
            if (isNaN(boundedLevel)) return res.status(400).json({ error: 'Invalid level value' });
            changes.level = { from: user.level, to: boundedLevel };
            user.level = boundedLevel;
        }

        // Update xp (bounded 0-MAX_SAFE_INTEGER for data integrity)
        if (typeof updates.xp === 'number') {
            const boundedXp = Math.max(0, Math.min(Number.MAX_SAFE_INTEGER, Math.floor(updates.xp)));
            if (isNaN(boundedXp)) return res.status(400).json({ error: 'Invalid xp value' });
            changes.xp = { from: user.xp, to: boundedXp };
            user.xp = boundedXp;
        }

        // Update highest_level_ever (bounded 0-999 for data integrity)
        if (typeof updates.highest_level_ever === 'number') {
            const boundedHighest = Math.max(0, Math.min(999, Math.floor(updates.highest_level_ever)));
            if (isNaN(boundedHighest)) return res.status(400).json({ error: 'Invalid highest_level_ever value' });
            changes.highest_level_ever = { from: user.highest_level_ever, to: boundedHighest };
            user.highest_level_ever = boundedHighest;
        }

        // Update skill_points (bounded, non-negative)
        if (typeof updates.skill_points === 'number') {
            const boundedSP = Math.max(0, Math.min(9999, Math.floor(updates.skill_points)));
            if (isNaN(boundedSP)) return res.status(400).json({ error: 'Invalid skill_points value' });
            changes.skill_points = { from: user.skill_points, to: boundedSP };
            user.skill_points = boundedSP;
        }

        // Update stats (partial merge — only provided keys are overwritten)
        if (updates.stats && typeof updates.stats === 'object') {
            if (!user.stats) user.stats = {};
            const statChanges = {};
            for (const [key, val] of Object.entries(updates.stats)) {
                // Prevent prototype pollution
                if (key === '__proto__' || key === 'constructor' || key === 'prototype') continue;
                statChanges[key] = { from: user.stats[key], to: val };
                user.stats[key] = val;
            }
            changes.stats = statChanges;
        }

        // Clear anti-cheat flags
        if (updates.clear_anti_cheat_flags === true) {
            changes.anti_cheat_flags = { from: (user.anti_cheat_flags || []).length + ' flags', to: 0 };
            user.anti_cheat_flags = [];
        }

        // Force level reset — server stays authoritative until client accepts new values
        if (updates.force_level_reset === true) {
            const resetAt = new Date().toISOString();
            changes.level_reset_at = { from: user.level_reset_at || null, to: resetAt };
            user.level_reset_at = resetAt;
        }

        // Fix leaderboard — set current_season and add to leaderboard sorted set
        if (updates.fix_leaderboard === true) {
            const currentSeason = getCurrentSeason();
            const oldSeason = user.current_season;
            user.current_season = currentSeason;
            const score = safeLeaderboardScore(user.xp);
            await redis.zadd(`leaderboard:${currentSeason}`, { score, member: unifiedId });
            try { await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(user.total_xp_earned || 0), member: unifiedId }); } catch (e) { console.log(`[WARN] All-time leaderboard update failed:`, e.message); }
            changes.fix_leaderboard = { current_season: currentSeason, old_season: oldSeason || null, leaderboard_score: score };
        }

        user.updated_at = new Date().toISOString();
        await redis.set(userKey, JSON.stringify(user));

        console.log(`[ADMIN] Updated user "${user.display_name}" (${unifiedId}):`, changes);

        res.json({ success: true, unified_id: unifiedId, changes });
    } catch (error) {
        console.error('Admin update-user error:', error.message);
        res.status(500).json({ error: 'Failed to update user' });
    }
});

// =============================================================================
// CONTENT PACK DOWNLOAD SYSTEM
// =============================================================================

// (crypto already imported at top of file)

// Bunny.net CDN configuration
const BUNNY_CONFIG = {
    CDN_HOSTNAME: 'ccp-packs.b-cdn.net',
    SECURITY_KEY: process.env.BUNNY_SECURITY_KEY || '',
    URL_EXPIRY_SECONDS: 3600 // 1 hour
};

// Pack download rate limiting
const PACK_RATE_LIMIT = {
    DOWNLOADS_PER_DAY: 4,  // Max downloads per pack per day
    KEY_PREFIX: 'packdownload:'
};


// Available packs
// =============================================================================
// CONTENT PACKS CONFIGURATION
// Server-controlled pack list - can be updated without app release
// Set enabled: false to hide a pack from users
// =============================================================================
const AVAILABLE_PACKS = {
    'basic-bimbo-starter': {
        enabled: true,
        name: 'Basic Bimbo Starter Pack',
        description: 'A starter pack with essential bimbo content. Perfect for new users getting started with their conditioning journey.',
        author: 'CodeBambi',
        version: '1.0.0',
        imageCount: 50,
        videoCount: 10,
        externalUrl: 'https://mega.nz/file/B6BhAJLb#wFFxCBHbm5r1M2Z449cnzmxG8HU0GQHq7vdIJG0t9As',
        sizeBytes: 2397264867,
        previewImageUrl: 'https://ccp-packs.b-cdn.net/previews/basic-bimbo-starter.png',
        patreonUrl: 'https://patreon.com/CodeBambi'
    },
    'enhanced-bimbodoll-video': {
        enabled: true,
        name: 'Enhanced Bimbodoll Video Pack',
        description: 'High quality video content for advanced conditioning. Includes mesmerizing visuals and deep programming.',
        author: 'CodeBambi',
        version: '1.0.0',
        imageCount: 0,
        videoCount: 25,
        externalUrl: 'https://mega.nz/file/gvoDVLIK#255-xsisRWiA_e9tBpG4oHP9p6mJV0UfAy7SvWe_8Cc',
        sizeBytes: 4392954093,
        previewImageUrl: 'https://ccp-packs.b-cdn.net/previews/enhanced-bimbodoll-video.png',
        patreonUrl: 'https://patreon.com/CodeBambi'
    },
    'bambi-core': {
        enabled: true,
        name: 'Bambi Core Collection',
        description: 'The essential Bambi transformation pack. 217 carefully curated images to help awaken your inner Bambi. Perfect for deep conditioning sessions. Special thanks to Issa for providing the pack ❤️',
        author: 'CodeBambi',
        version: '1.0.0',
        imageCount: 217,
        videoCount: 0,
        externalUrl: 'https://mega.nz/file/AjJQ3bpC#yVY-bLupmR2zc_kx8EtWQBZ3ARyA6cCAVt18vX1c99Q',
        sizeBytes: 1289338675,
        previewImageUrl: 'https://ccp-packs.b-cdn.net/previews/bambi-core.png',
        previewUrls: [
            'https://ccp-packs.b-cdn.net/previews/bambi-core-1.gif',
            'https://ccp-packs.b-cdn.net/previews/bambi-core-2.gif',
            'https://ccp-packs.b-cdn.net/previews/bambi-core-3.gif',
            'https://ccp-packs.b-cdn.net/previews/bambi-core-4.gif',
            'https://ccp-packs.b-cdn.net/previews/bambi-core-5.gif',
            'https://ccp-packs.b-cdn.net/previews/bambi-core-6.gif'
        ],
        patreonUrl: 'https://patreon.com/CodeBambi'
    },
    'bimbo-aesthetic': {
        enabled: true,
        name: 'Bimbo Aesthetic Pack',
        description: 'Embrace the bimbo lifestyle with 106 gorgeous images. Pink, pretty, and perfectly vapid. Let these images remind you what you\'re becoming. Special thanks to Issa for providing the pack ❤️',
        author: 'CodeBambi',
        version: '1.0.0',
        imageCount: 106,
        videoCount: 0,
        externalUrl: 'https://mega.nz/file/dvZ2BYJJ#O5UoUqy3AQPubbM1RJbtmueWbtZZsJwZtqHTgYv0bMo',
        sizeBytes: 540674867,
        previewImageUrl: 'https://ccp-packs.b-cdn.net/previews/bimbo-aesthetic.png',
        previewUrls: [
            'https://ccp-packs.b-cdn.net/previews/bimbo-aesthetic-1.gif',
            'https://ccp-packs.b-cdn.net/previews/bimbo-aesthetic-2.jpg',
            'https://ccp-packs.b-cdn.net/previews/bimbo-aesthetic-3.gif',
            'https://ccp-packs.b-cdn.net/previews/bimbo-aesthetic-4.webp',
            'https://ccp-packs.b-cdn.net/previews/bimbo-aesthetic-5.gif',
            'https://ccp-packs.b-cdn.net/previews/bimbo-aesthetic-6.gif'
        ],
        patreonUrl: 'https://patreon.com/CodeBambi'
    },
    'cock-drop': {
        enabled: true,
        name: 'Cock Drop Conditioning',
        description: '89 hypnotic images for devoted cock-loving bambis. Every flash drops you deeper into cock-obsessed bliss. Warning: Highly addictive. Special thanks to Issa for providing the pack ❤️',
        author: 'CodeBambi',
        version: '1.0.0',
        imageCount: 89,
        videoCount: 0,
        externalUrl: 'https://mega.nz/file/xjRxiKKJ#PLGJfsLf0mkgkxQvJlRB32HxKTpAvlAnzkQ34UZc8ik',
        sizeBytes: 421955994,
        previewImageUrl: 'https://ccp-packs.b-cdn.net/previews/cock-drop.png',
        previewUrls: [
            'https://ccp-packs.b-cdn.net/previews/cock-drop-1.gif',
            'https://ccp-packs.b-cdn.net/previews/cock-drop-2.webp',
            'https://ccp-packs.b-cdn.net/previews/cock-drop-3.webp',
            'https://ccp-packs.b-cdn.net/previews/cock-drop-4.gif',
            'https://ccp-packs.b-cdn.net/previews/cock-drop-5.gif',
            'https://ccp-packs.b-cdn.net/previews/cock-drop-6.gif'
        ],
        patreonUrl: 'https://patreon.com/CodeBambi'
    },
    'empty-head': {
        enabled: true,
        name: 'Empty Head Happy Head',
        description: '276 mind-melting images to help empty that pretty little head. No thoughts, just Bambi. The largest pack for maximum brain drain. Special thanks to Issa for providing the pack ❤️',
        author: 'CodeBambi',
        version: '1.0.0',
        imageCount: 276,
        videoCount: 0,
        externalUrl: 'https://mega.nz/file/ZqxSwQiJ#t-d_Q9-0eBb162ggG_S9-0ZPTEZpBGdIIvKqimrhYHg',
        sizeBytes: 1465909698,
        previewImageUrl: 'https://ccp-packs.b-cdn.net/previews/empty-head.png',
        previewUrls: [
            'https://ccp-packs.b-cdn.net/previews/empty-head-1.gif',
            'https://ccp-packs.b-cdn.net/previews/empty-head-2.gif',
            'https://ccp-packs.b-cdn.net/previews/empty-head-3.gif',
            'https://ccp-packs.b-cdn.net/previews/empty-head-4.gif',
            'https://ccp-packs.b-cdn.net/previews/empty-head-5.gif',
            'https://ccp-packs.b-cdn.net/previews/empty-head-6.gif'
        ],
        patreonUrl: 'https://patreon.com/CodeBambi'
    },
    'good-girl': {
        enabled: true,
        name: 'Good Girl Rewards',
        description: '77 images of praise and positive reinforcement. Because every good girl deserves to feel validated. Let Bambi know she\'s doing so well. Special thanks to Issa for providing the pack ❤️',
        author: 'CodeBambi',
        version: '1.0.0',
        imageCount: 77,
        videoCount: 0,
        externalUrl: 'https://mega.nz/file/AjQnxQgL#7ye5gQ9zEkHq0FW5kncb44Dl2Tsn9hiVE5mCeLI_rfo',
        sizeBytes: 382876098,
        previewImageUrl: 'https://ccp-packs.b-cdn.net/previews/good-girl.png',
        previewUrls: [
            'https://ccp-packs.b-cdn.net/previews/good-girl-1.gif',
            'https://ccp-packs.b-cdn.net/previews/good-girl-2.gif',
            'https://ccp-packs.b-cdn.net/previews/good-girl-3.gif',
            'https://ccp-packs.b-cdn.net/previews/good-girl-4.gif',
            'https://ccp-packs.b-cdn.net/previews/good-girl-5.gif',
            'https://ccp-packs.b-cdn.net/previews/good-girl-6.gif'
        ],
        patreonUrl: 'https://patreon.com/CodeBambi'
    },
    'pretty-girl': {
        enabled: true,
        name: 'Pretty Girl Dreams',
        description: '68 beautiful feminine images to inspire your transformation. Soft, pretty, and undeniably girly. Be the girl you were always meant to be. Special thanks to Issa for providing the pack ❤️',
        author: 'CodeBambi',
        version: '1.0.0',
        imageCount: 68,
        videoCount: 0,
        externalUrl: 'https://mega.nz/file/03wwGbgB#kKlcyq4N7NOUAqCHapibU4Bfr6oN0_SAyGbLNNPXzC0',
        sizeBytes: 489951068,
        previewImageUrl: 'https://ccp-packs.b-cdn.net/previews/pretty-girl.png',
        previewUrls: [
            'https://ccp-packs.b-cdn.net/previews/pretty-girl-1.gif',
            'https://ccp-packs.b-cdn.net/previews/pretty-girl-2.gif',
            'https://ccp-packs.b-cdn.net/previews/pretty-girl-3.gif',
            'https://ccp-packs.b-cdn.net/previews/pretty-girl-4.gif',
            'https://ccp-packs.b-cdn.net/previews/pretty-girl-5.gif',
            'https://ccp-packs.b-cdn.net/previews/pretty-girl-6.gif'
        ],
        patreonUrl: 'https://patreon.com/CodeBambi'
    },
    'bimbo-moo': {
        enabled: true,
        name: 'Bimbo Cow Collection',
        description: '140 hucow-themed images for bambis who love to moo. Big, dumb, and happy. Perfect for those special udderly mindless sessions. Special thanks to Issa for providing the pack ❤️',
        author: 'CodeBambi',
        version: '1.0.0',
        imageCount: 140,
        videoCount: 0,
        externalUrl: 'https://mega.nz/file/B6plRJpK#zoI5ISVo05JQ6C0Tfu8xRTVxaldR8eJX0Q8_mzr6_D8',
        sizeBytes: 670223564,
        previewImageUrl: 'https://ccp-packs.b-cdn.net/previews/bimbo-moo.png',
        previewUrls: [
            'https://ccp-packs.b-cdn.net/previews/bimbo-moo-1.webp',
            'https://ccp-packs.b-cdn.net/previews/bimbo-moo-2.gif',
            'https://ccp-packs.b-cdn.net/previews/bimbo-moo-3.gif',
            'https://ccp-packs.b-cdn.net/previews/bimbo-moo-4.gif',
            'https://ccp-packs.b-cdn.net/previews/bimbo-moo-5.png',
            'https://ccp-packs.b-cdn.net/previews/bimbo-moo-6.jpeg'
        ],
        patreonUrl: 'https://patreon.com/CodeBambi'
    },
    'toon-bimbos': {
        enabled: true,
        name: 'Toon Bimbo Fantasy',
        description: '61 cartoon and animated bimbo images. Exaggerated curves, bright colors, and impossible proportions. Perfect fantasy fuel for your conditioning. Special thanks to Issa for providing the pack ❤️',
        author: 'CodeBambi',
        version: '1.0.0',
        imageCount: 61,
        videoCount: 0,
        externalUrl: 'https://mega.nz/file/IvQVnCqI#D_B5avzh0E_55sseNOTbyvy1A4fNvCZ81J0lUceYUdA',
        sizeBytes: 365567795,
        previewImageUrl: 'https://ccp-packs.b-cdn.net/previews/toon-bimbos.png',
        previewUrls: [
            'https://ccp-packs.b-cdn.net/previews/toon-bimbos-1.gif',
            'https://ccp-packs.b-cdn.net/previews/toon-bimbos-2.gif',
            'https://ccp-packs.b-cdn.net/previews/toon-bimbos-3.jpg',
            'https://ccp-packs.b-cdn.net/previews/toon-bimbos-4.gif',
            'https://ccp-packs.b-cdn.net/previews/toon-bimbos-5.gif',
            'https://ccp-packs.b-cdn.net/previews/toon-bimbos-6.gif'
        ],
        patreonUrl: 'https://patreon.com/CodeBambi'
    }
};

/**
 * Generate Bunny.net download URL
 * Token auth disabled for now - using rate limiting via Patreon auth instead
 */
function generateDownloadUrl(filePath) {
    // Direct URL without token (token auth disabled on Bunny pull zone)
    // Security is handled by requiring Patreon auth + rate limiting
    return `https://${BUNNY_CONFIG.CDN_HOSTNAME}${filePath}`;
}

/**
 * GET /packs/manifest
 * Returns the list of available content packs (server-controlled)
 * Only returns enabled packs - allows adding/removing/disabling without app update
 * No auth required - just returns the pack catalog
 */
app.get('/packs/manifest', (req, res) => {
    try {
        // Build pack list from AVAILABLE_PACKS, only including enabled packs
        const packs = [];

        for (const [packId, pack] of Object.entries(AVAILABLE_PACKS)) {
            // Skip disabled packs
            if (pack.enabled === false) {
                continue;
            }

            packs.push({
                id: packId,
                name: pack.name,
                description: pack.description || '',
                author: pack.author || 'CodeBambi',
                version: pack.version || '1.0.0',
                imageCount: pack.imageCount || 0,
                videoCount: pack.videoCount || 0,
                sizeBytes: pack.sizeBytes || 0,
                downloadUrl: pack.externalUrl ? '' : `https://${BUNNY_CONFIG.CDN_HOSTNAME}${pack.path}`,
                isExternal: !!pack.externalUrl,
                // externalUrl intentionally NOT exposed — clients must use authenticated /pack/download-url endpoint
                previewImageUrl: pack.previewImageUrl || '',
                previewUrls: pack.previewUrls || [],
                patreonUrl: pack.patreonUrl || null,
                upgradeUrl: pack.upgradeUrl || null
            });
        }

        console.log(`Packs manifest requested: returning ${packs.length} enabled packs`);

        res.json({
            version: '1.0',
            packs: packs
        });

    } catch (error) {
        console.error('Packs manifest error:', error.message);
        res.status(500).json({
            version: '1.0',
            packs: [],
            error: 'Failed to load packs manifest'
        });
    }
});

/**
 * Check pack download rate limit
 * Returns { allowed: boolean, remaining: number, resetTime: Date }
 */
async function checkAndIncrementPackDownload(userId, packId) {
    if (!redis) {
        // No Redis = fail-closed, deny download
        return { allowed: false, remaining: 0, used: 0, error: true };
    }

    const todayKey = getTodayKey();
    const key = `${PACK_RATE_LIMIT.KEY_PREFIX}${userId}:${packId}:${todayKey}`;

    try {
        // R11/H2: Atomic check-and-increment to prevent TOCTOU race
        // Ensure key exists with TTL before incrementing
        await redis.set(key, 0, { nx: true, ex: 172800 });
        const newCount = await redis.incr(key);

        // Calculate reset time (midnight UTC)
        const now = new Date();
        const resetTime = new Date(Date.UTC(
            now.getUTCFullYear(),
            now.getUTCMonth(),
            now.getUTCDate() + 1,
            0, 0, 0
        ));

        if (newCount > PACK_RATE_LIMIT.DOWNLOADS_PER_DAY) {
            return {
                allowed: false,
                remaining: 0,
                used: newCount,
                resetTime: resetTime.toISOString()
            };
        }

        console.log(`Pack download recorded: user=${userId}, pack=${packId}, count=${newCount}`);
        return {
            allowed: true,
            remaining: PACK_RATE_LIMIT.DOWNLOADS_PER_DAY - newCount,
            used: newCount,
            resetTime: resetTime.toISOString()
        };
    } catch (error) {
        console.error('Pack rate limit check error:', error.message);
        // On Redis error, DENY download (match main rate limiter fail-closed behavior)
        return { allowed: false, remaining: 0, used: PACK_RATE_LIMIT.DOWNLOADS_PER_DAY, error: true };
    }
}

/**
 * Read-only pack download count check (for status endpoints, does NOT increment).
 */
async function checkPackDownloadLimit(userId, packId) {
    if (!redis) return { allowed: false, remaining: 0, used: 0, error: true };
    const todayKey = getTodayKey();
    const key = `${PACK_RATE_LIMIT.KEY_PREFIX}${userId}:${packId}:${todayKey}`;
    try {
        const count = parseInt(await redis.get(key)) || 0;
        const now = new Date();
        const resetTime = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1, 0, 0, 0));
        return {
            allowed: count < PACK_RATE_LIMIT.DOWNLOADS_PER_DAY,
            remaining: Math.max(0, PACK_RATE_LIMIT.DOWNLOADS_PER_DAY - count),
            used: count,
            resetTime: resetTime.toISOString()
        };
    } catch (error) {
        return { allowed: false, remaining: 0, used: PACK_RATE_LIMIT.DOWNLOADS_PER_DAY, error: true };
    }
}

/**
 * POST /pack/download-url
 * Get a download URL for a content pack
 * External/Mega packs: Requires V2 auth (X-Auth-Token + unified_id) — any authenticated user
 * CDN-hosted packs: Requires Authorization: Bearer <patreon_access_token> + active tier
 * Body: { packId: string, unified_id?: string (required for external packs) }
 */
app.post('/pack/download-url', async (req, res) => {
    try {
        // Validate pack ID first (before auth — determines which auth path to use)
        const { packId } = req.body;
        if (!packId || !AVAILABLE_PACKS[packId]) {
            return res.status(400).json({
                error: 'Invalid pack',
                message: 'The requested content pack does not exist.'
            });
        }

        const pack = AVAILABLE_PACKS[packId];

        // External URL packs (Mega-hosted) — any authenticated user can download
        if (pack.externalUrl) {
            // Try V2 auth first (X-Auth-Token + unified_id) — works for all auth methods
            const { unified_id } = req.body;
            if (unified_id && isValidUnifiedId(unified_id)) {
                if (!redis) return res.status(503).json({ error: 'Service unavailable' });
                const userData = await redis.get(`user:${unified_id}`);
                if (!userData) {
                    return res.status(404).json({ error: 'User not found' });
                }
                const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
                const authCheck = validateAuthToken(req, user);
                if (!authCheck.valid) {
                    return res.status(401).json({ error: 'Invalid or missing auth token' });
                }

                console.log(`Pack external download (v2): user=${unified_id}, pack=${packId}`);
                return res.json({
                    success: true,
                    downloadUrl: pack.externalUrl,
                    packId: packId,
                    packName: pack.name,
                    sizeBytes: pack.sizeBytes
                });
            }

            // Fallback: Patreon Bearer token (legacy clients without V2 auth)
            const authHeader = req.headers.authorization;
            if (authHeader?.startsWith('Bearer ')) {
                try {
                    const accessToken = authHeader.substring(7);
                    const identity = await getPatreonIdentity(accessToken);
                    const patreonUserId = identity.data?.id;
                    if (patreonUserId) {
                        console.log(`Pack external download (patreon): user=${patreonUserId}, pack=${packId}`);
                        return res.json({
                            success: true,
                            downloadUrl: pack.externalUrl,
                            packId: packId,
                            packName: pack.name,
                            sizeBytes: pack.sizeBytes
                        });
                    }
                } catch (e) {
                    // Fall through to error below
                }
            }

            return res.status(401).json({
                error: 'Authorization required',
                message: 'Please log in to download content packs.'
            });
        }

        // CDN-hosted packs — require Patreon auth + tier check + rate limit
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({
                error: 'Authorization required',
                message: 'Please log in with Patreon to download content packs.'
            });
        }

        const accessToken = authHeader.substring(7);

        // Validate Patreon token and get user info
        let identity;
        try {
            identity = await getPatreonIdentity(accessToken);
        } catch (error) {
            return res.status(401).json({
                error: 'Invalid token',
                message: 'Your Patreon session has expired. Please log in again.'
            });
        }

        const userId = identity.data?.id;
        if (!userId) {
            return res.status(401).json({
                error: 'Invalid user',
                message: 'Could not identify Patreon user.'
            });
        }

        // Verify user has a paid Patreon tier (free followers cannot download packs)
        const tierInfo = determineTier(identity);
        const isWhitelisted = tierInfo.is_whitelisted || false;
        if (!isWhitelisted && (!tierInfo.tier || tierInfo.tier < 1)) {
            return res.status(403).json({
                error: 'Subscription required',
                message: 'Content packs require an active Patreon subscription.'
            });
        }

        // CDN-hosted packs — apply rate limit
        // R11/H2: Atomic check-and-increment (prevents TOCTOU race on concurrent downloads)
        const rateLimit = await checkAndIncrementPackDownload(userId, packId);
        if (!rateLimit.allowed) {
            return res.status(429).json({
                error: 'Rate limit exceeded',
                message: `You've reached the download limit for this pack today (${PACK_RATE_LIMIT.DOWNLOADS_PER_DAY} per day). Try again after midnight UTC.`,
                resetTime: rateLimit.resetTime,
                remaining: 0
            });
        }

        // Generate signed download URL from Bunny CDN
        const downloadUrl = generateDownloadUrl(pack.path);

        console.log(`Pack download URL generated: user=${userId}, pack=${packId}, remaining=${rateLimit.remaining}`);

        res.json({
            success: true,
            downloadUrl: downloadUrl,
            packId: packId,
            packName: pack.name,
            sizeBytes: pack.sizeBytes,
            rateLimit: {
                remaining: rateLimit.remaining,
                limit: PACK_RATE_LIMIT.DOWNLOADS_PER_DAY,
                resetTime: rateLimit.resetTime
            }
        });

    } catch (error) {
        console.error('Pack download URL error:', error.message);
        res.status(500).json({
            error: 'Server error',
            message: 'Failed to generate download URL. Please try again.'
        });
    }
});

/**
 * GET /pack/status
 * Get download status for all packs (rate limits)
 * Requires: Authorization: Bearer <patreon_access_token>
 */
app.get('/pack/status', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization required' });
        }

        const accessToken = authHeader.substring(7);

        let identity;
        try {
            identity = await getPatreonIdentity(accessToken);
        } catch (error) {
            return res.status(401).json({ error: 'Invalid token' });
        }

        const userId = identity.data?.id;
        if (!userId) {
            return res.status(401).json({ error: 'Invalid user' });
        }

        // Get rate limit status for all enabled packs
        const packStatus = {};
        for (const [packId, pack] of Object.entries(AVAILABLE_PACKS)) {
            if (pack.enabled === false) continue;
            const rateLimit = await checkPackDownloadLimit(userId, packId);
            packStatus[packId] = {
                name: pack.name,
                sizeBytes: pack.sizeBytes,
                canDownload: rateLimit.allowed,
                downloadsRemaining: rateLimit.remaining,
                downloadsUsed: rateLimit.used,
                resetTime: rateLimit.resetTime
            };
        }

        res.json({
            packs: packStatus,
            dailyLimit: PACK_RATE_LIMIT.DOWNLOADS_PER_DAY
        });

    } catch (error) {
        console.error('Pack status error:', error.message);
        res.status(500).json({ error: 'Server error' });
    }
});

/**
 * GET /discord/pack/status
 * Get download status for all packs using Discord auth
 * If the Discord user's display name is linked to a Patreon account, they inherit Patreon benefits
 * Requires: Authorization: Bearer <discord_access_token>
 */
app.get('/discord/pack/status', async (req, res) => {
    try {
        const authHeader = req.headers.authorization;
        if (!authHeader?.startsWith('Bearer ')) {
            return res.status(401).json({ error: 'Authorization required' });
        }

        const accessToken = authHeader.substring(7);

        // Validate Discord token and get user
        let discordUser;
        try {
            discordUser = await getDiscordUser(accessToken);
        } catch (error) {
            return res.status(401).json({ error: 'Invalid Discord token' });
        }

        if (!discordUser?.id) {
            return res.status(401).json({ error: 'Invalid Discord user' });
        }

        const discordUserId = `discord_${discordUser.id}`;

        // Get rate limit status for all enabled packs
        const packStatus = {};
        for (const [packId, pack] of Object.entries(AVAILABLE_PACKS)) {
            if (pack.enabled === false) continue;
            const rateLimit = await checkPackDownloadLimit(discordUserId, packId);
            packStatus[packId] = {
                name: pack.name,
                sizeBytes: pack.sizeBytes,
                canDownload: rateLimit.allowed,
                downloadsRemaining: rateLimit.remaining,
                downloadsUsed: rateLimit.used,
                resetTime: rateLimit.resetTime
            };
        }

        res.json({
            packs: packStatus,
            dailyLimit: PACK_RATE_LIMIT.DOWNLOADS_PER_DAY
        });

    } catch (error) {
        console.error('Discord pack status error:', error.message);
        res.status(500).json({ error: 'Server error' });
    }
});

// =============================================================================
// QUEST DEFINITIONS SYSTEM
// =============================================================================

/**
 * Default quest definitions (fallback if Redis not configured or empty)
 * These match the hardcoded quests in the client
 */
const DEFAULT_QUEST_DEFINITIONS = {
    daily: [
        { id: "flash_flood_d", name: "Flash Flood", description: "View 50 flash images", type: "daily", category: "flash", targetValue: 50, xpReward: 150, icon: "⚡", imageUrl: "https://bambi-cdn.b-cdn.net/quests/flash.png" },
        { id: "spiral_submersion_d", name: "Spiral Submersion", description: "Spend 10 minutes with spiral overlay", type: "daily", category: "spiral", targetValue: 10, xpReward: 200, icon: "🌀", imageUrl: "https://bambi-cdn.b-cdn.net/quests/spiral_overlay.png" },
        { id: "bubble_brain_d", name: "Bubble Brain", description: "Pop 30 bubbles", type: "daily", category: "bubbles", targetValue: 30, xpReward: 150, icon: "🫧", imageUrl: "https://bambi-cdn.b-cdn.net/quests/bubble_pop.png" },
        { id: "pink_vision_d", name: "Pink Vision", description: "Use pink filter for 15 minutes", type: "daily", category: "pinkfilter", targetValue: 15, xpReward: 175, icon: "💗", imageUrl: "https://bambi-cdn.b-cdn.net/quests/pink_filter.png" },
        { id: "deep_trance_d", name: "Deep Trance Training", description: "Watch 10 minutes of video", type: "daily", category: "video", targetValue: 10, xpReward: 200, icon: "🎬", imageUrl: "https://bambi-cdn.b-cdn.net/quests/mandatory_videos.png" },
        { id: "devotion_display_d", name: "Devotion Display", description: "Complete 1 session", type: "daily", category: "session", targetValue: 1, xpReward: 250, icon: "🙏", imageUrl: "https://bambi-cdn.b-cdn.net/quests/session.png" },
        { id: "obedience_drill_d", name: "Obedience Drill", description: "Complete 2 lock cards", type: "daily", category: "lockcard", targetValue: 2, xpReward: 200, icon: "🔒", imageUrl: "https://bambi-cdn.b-cdn.net/quests/phrase_lock.png" },
        { id: "bimbo_basics_d", name: "Bimbo Basics", description: "View 25 flash images", type: "daily", category: "flash", targetValue: 25, xpReward: 100, icon: "✨", imageUrl: "https://bambi-cdn.b-cdn.net/quests/flash.png" },
        { id: "mindless_minutes_d", name: "Mindless Minutes", description: "Spend 20 minutes with any overlay active", type: "daily", category: "combined", targetValue: 20, xpReward: 175, icon: "🧠", imageUrl: "https://bambi-cdn.b-cdn.net/quests/brain_drain.png" },
        { id: "thought_pop_d", name: "Thought Pop", description: "Pop 50 bubbles", type: "daily", category: "bubbles", targetValue: 50, xpReward: 175, icon: "💭", imageUrl: "https://bambi-cdn.b-cdn.net/quests/bubble_pop.png" }
    ],
    weekly: [
        { id: "flash_monsoon_w", name: "Flash Monsoon", description: "View 500 flash images", type: "weekly", category: "flash", targetValue: 500, xpReward: 600, icon: "⚡", imageUrl: "https://bambi-cdn.b-cdn.net/quests/flash.png" },
        { id: "spiral_abyss_w", name: "Spiral Abyss", description: "Spend 120 minutes with spiral overlay", type: "weekly", category: "spiral", targetValue: 120, xpReward: 750, icon: "🌀", imageUrl: "https://bambi-cdn.b-cdn.net/quests/spiral_overlay.png" },
        { id: "bubble_tsunami_w", name: "Bubble Tsunami", description: "Pop 400 bubbles", type: "weekly", category: "bubbles", targetValue: 400, xpReward: 600, icon: "🌊", imageUrl: "https://bambi-cdn.b-cdn.net/quests/bubble_pop.png" },
        { id: "pink_immersion_w", name: "Pink Immersion", description: "Use pink filter for 180 minutes", type: "weekly", category: "pinkfilter", targetValue: 180, xpReward: 700, icon: "💗", imageUrl: "https://bambi-cdn.b-cdn.net/quests/pink_filter.png" },
        { id: "marathon_trance_w", name: "Marathon Trance", description: "Watch 90 minutes of video", type: "weekly", category: "video", targetValue: 90, xpReward: 800, icon: "🎬", imageUrl: "https://bambi-cdn.b-cdn.net/quests/mandatory_videos.png" },
        { id: "weekly_devotion_w", name: "Weekly Devotion", description: "Complete 7 sessions", type: "weekly", category: "session", targetValue: 7, xpReward: 1000, icon: "🙏", imageUrl: "https://bambi-cdn.b-cdn.net/quests/session.png" },
        { id: "phrase_mastery_w", name: "Phrase Mastery", description: "Complete 15 lock cards", type: "weekly", category: "lockcard", targetValue: 15, xpReward: 750, icon: "🔒", imageUrl: "https://bambi-cdn.b-cdn.net/quests/phrase_lock.png" },
        { id: "conditioning_champion_w", name: "Conditioning Champion", description: "Earn 2000 XP from activities", type: "weekly", category: "combined", targetValue: 2000, xpReward: 500, icon: "🏆", imageUrl: "https://bambi-cdn.b-cdn.net/quests/logo.png" },
        { id: "streak_keeper_w", name: "Streak Keeper", description: "Maintain a 7-day streak", type: "weekly", category: "streak", targetValue: 7, xpReward: 600, icon: "🔥", imageUrl: "https://bambi-cdn.b-cdn.net/quests/daily_maintenance.png" },
        { id: "total_submission_w", name: "Total Submission", description: "Complete 15 bubble count games", type: "weekly", category: "bubblecount", targetValue: 15, xpReward: 700, icon: "🎯", imageUrl: "https://bambi-cdn.b-cdn.net/quests/bubble_count.png" }
    ],
    seasonal: []
};

const QUEST_REDIS_KEY = 'quest_definitions';

/**
 * Get quest definitions from Redis or return defaults
 */
async function getQuestDefinitions() {
    if (!redis) {
        return DEFAULT_QUEST_DEFINITIONS;
    }

    try {
        const stored = await redis.get(QUEST_REDIS_KEY);
        if (stored) {
            return typeof stored === 'string' ? JSON.parse(stored) : stored;
        }
        return DEFAULT_QUEST_DEFINITIONS;
    } catch (error) {
        console.error('Error fetching quest definitions:', error.message);
        return DEFAULT_QUEST_DEFINITIONS;
    }
}

/**
 * Filter seasonal quests by current date
 */
function filterActiveQuests(quests) {
    const now = new Date();
    const today = now.toISOString().split('T')[0]; // YYYY-MM-DD

    return {
        daily: quests.daily || [],
        weekly: quests.weekly || [],
        seasonal: (quests.seasonal || []).filter(quest => {
            // If no date restrictions, always include
            if (!quest.activeFrom && !quest.activeUntil) return true;

            // Check date range
            const from = quest.activeFrom || '1970-01-01';
            const until = quest.activeUntil || '2099-12-31';
            return today >= from && today <= until;
        })
    };
}

/**
 * GET /quests/definitions
 * Returns all active quest definitions (daily, weekly, seasonal)
 * Seasonal quests are filtered by current date
 */
// In-memory cache for quest definitions (prevents per-request Redis reads on public endpoint)
let questDefinitionsCache = null;
let questDefinitionsCacheTime = 0;
const QUEST_DEFINITIONS_CACHE_TTL = 60_000; // 60 seconds

app.get('/quests/definitions', async (req, res) => {
    try {
        // Serve from cache if fresh
        const now = Date.now();
        if (questDefinitionsCache && (now - questDefinitionsCacheTime) < QUEST_DEFINITIONS_CACHE_TTL) {
            return res.json(questDefinitionsCache);
        }

        const allQuests = await getQuestDefinitions();
        const activeQuests = filterActiveQuests(allQuests);

        // Load season config for title
        let seasonTitle = null;
        if (redis) {
            try {
                const seasonConfig = await redis.get('season_config');
                if (seasonConfig) {
                    const config = typeof seasonConfig === 'string' ? JSON.parse(seasonConfig) : seasonConfig;
                    seasonTitle = config.title || null;
                }
            } catch (e) {
                console.error('Error loading season_config:', e.message);
            }
        }

        const response = {
            success: true,
            version: allQuests.version || 1,
            updatedAt: allQuests.updatedAt || new Date().toISOString(),
            seasonTitle: seasonTitle,
            quests: activeQuests
        };

        // Cache the response
        questDefinitionsCache = response;
        questDefinitionsCacheTime = now;

        res.json(response);
    } catch (error) {
        console.error('Error getting quest definitions:', error.message);
        res.status(500).json({ error: 'Failed to fetch quest definitions' });
    }
});

/**
 * POST /admin/quests/update
 * Update quest definitions (requires admin token)
 * Body: { admin_token, daily: [...], weekly: [...], seasonal: [...] }
 */
app.post('/admin/quests/update', async (req, res) => {
    const { daily, weekly, seasonal } = req.body;
    const admin_token = req.headers['x-admin-token'];

    // Verify admin token
    if (!verifyAdminToken(admin_token)) {
        return res.status(401).json({ error: 'Invalid admin token' });
    }

    if (!redis) {
        return res.status(500).json({ error: 'Redis not configured' });
    }

    try {
        // Get current definitions to merge
        const current = await getQuestDefinitions();

        const updated = {
            daily: daily || current.daily || DEFAULT_QUEST_DEFINITIONS.daily,
            weekly: weekly || current.weekly || DEFAULT_QUEST_DEFINITIONS.weekly,
            seasonal: seasonal || current.seasonal || [],
            version: (current.version || 0) + 1,
            updatedAt: new Date().toISOString()
        };

        await redis.set(QUEST_REDIS_KEY, JSON.stringify(updated));

        res.json({
            success: true,
            message: 'Quest definitions updated',
            version: updated.version,
            counts: {
                daily: updated.daily.length,
                weekly: updated.weekly.length,
                seasonal: updated.seasonal.length
            }
        });
    } catch (error) {
        console.error('Error updating quest definitions:', error.message);
        res.status(500).json({ error: 'Failed to update quest definitions' });
    }
});

/**
 * GET /admin/quests/get
 * Get all quest definitions including inactive seasonal (requires admin token)
 */
app.get('/admin/quests/get', async (req, res) => {
    const admin_token = req.headers['x-admin-token'];

    if (!verifyAdminToken(admin_token)) {
        return res.status(401).json({ error: 'Invalid admin token' });
    }

    try {
        const quests = await getQuestDefinitions();
        res.json({
            success: true,
            quests
        });
    } catch (error) {
        console.error('Error getting quest definitions:', error.message);
        res.status(500).json({ error: 'Failed to fetch quest definitions' });
    }
});

/**
 * POST /admin/quests/add-seasonal
 * Add a single seasonal quest (requires admin token)
 * Body: { admin_token, quest: { id, name, description, ... } }
 */
app.post('/admin/quests/add-seasonal', async (req, res) => {
    const { quest } = req.body;
    const admin_token = req.headers['x-admin-token'];

    if (!verifyAdminToken(admin_token)) {
        return res.status(401).json({ error: 'Invalid admin token' });
    }

    if (!quest || !quest.id || !quest.name) {
        return res.status(400).json({ error: 'Quest must have id and name' });
    }

    if (!redis) {
        return res.status(500).json({ error: 'Redis not configured' });
    }

    try {
        const current = await getQuestDefinitions();

        // Ensure seasonal array exists
        if (!current.seasonal) current.seasonal = [];

        // Check for duplicate ID
        if (current.seasonal.find(q => q.id === quest.id)) {
            return res.status(400).json({ error: `Quest with id "${quest.id}" already exists` });
        }

        // Add the quest
        current.seasonal.push({
            ...quest,
            type: quest.type || 'daily' // default to daily if not specified
        });

        current.version = (current.version || 0) + 1;
        current.updatedAt = new Date().toISOString();

        await redis.set(QUEST_REDIS_KEY, JSON.stringify(current));

        res.json({
            success: true,
            message: `Seasonal quest "${quest.name}" added`,
            version: current.version,
            seasonalCount: current.seasonal.length
        });
    } catch (error) {
        console.error('Error adding seasonal quest:', error.message);
        res.status(500).json({ error: 'Failed to add seasonal quest' });
    }
});

/**
 * POST /admin/quests/remove-seasonal
 * Remove a seasonal quest by ID (requires admin token)
 * Body: { admin_token, questId }
 */
app.post('/admin/quests/remove-seasonal', async (req, res) => {
    const { questId } = req.body;
    const admin_token = req.headers['x-admin-token'];

    if (!verifyAdminToken(admin_token)) {
        return res.status(401).json({ error: 'Invalid admin token' });
    }

    if (!questId) {
        return res.status(400).json({ error: 'questId required' });
    }

    if (!redis) {
        return res.status(500).json({ error: 'Redis not configured' });
    }

    try {
        const current = await getQuestDefinitions();

        if (!current.seasonal) {
            return res.status(404).json({ error: 'No seasonal quests found' });
        }

        const index = current.seasonal.findIndex(q => q.id === questId);
        if (index === -1) {
            return res.status(404).json({ error: `Quest with id "${questId}" not found` });
        }

        const removed = current.seasonal.splice(index, 1)[0];
        current.version = (current.version || 0) + 1;
        current.updatedAt = new Date().toISOString();

        await redis.set(QUEST_REDIS_KEY, JSON.stringify(current));

        res.json({
            success: true,
            message: `Seasonal quest "${removed.name}" removed`,
            version: current.version,
            seasonalCount: current.seasonal.length
        });
    } catch (error) {
        console.error('Error removing seasonal quest:', error.message);
        res.status(500).json({ error: 'Failed to remove seasonal quest' });
    }
});

/**
 * POST /admin/reset-quest
 * Set reset_weekly_quest or reset_daily_quest flag on a user's profile.
 * The client will pick this up on next sync and regenerate the quest.
 * Body: { admin_token, display_name, type: 'weekly'|'daily'|'both' }
 */
app.post('/admin/reset-quest', async (req, res) => {
    const { display_name, type } = req.body;
    const admin_token = req.headers['x-admin-token'];

    if (!verifyAdminToken(admin_token)) {
        return res.status(401).json({ error: 'Invalid admin token' });
    }

    if (!display_name || typeof display_name !== 'string') {
        return res.status(400).json({ error: 'display_name required' });
    }

    const validTypes = ['weekly', 'daily', 'both'];
    if (!type || !validTypes.includes(type)) {
        return res.status(400).json({ error: 'type must be "weekly", "daily", or "both"' });
    }

    if (!redis) {
        return res.status(500).json({ error: 'Redis not configured' });
    }

    try {
        // Find user by display_name
        const targetName = display_name.toLowerCase();
        let foundUser = null;
        let foundKey = null;

        // Check display_name index first (fast path)
        const indexKey = `display_name_index:${targetName}`;
        const unifiedId = await redis.get(indexKey);

        if (unifiedId) {
            const userKey = `user:${unifiedId}`;
            const userData = await redis.get(userKey);
            if (userData) {
                foundUser = typeof userData === 'string' ? JSON.parse(userData) : userData;
                foundKey = userKey;
            }
        }

        // Fallback: scan user:* keys
        if (!foundUser) {
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
                cursor = String(result[0]);
                const keys = result[1] || [];
                for (const key of keys) {
                    try {
                        const data = await redis.get(key);
                        if (data) {
                            const user = typeof data === 'string' ? JSON.parse(data) : data;
                            if (user.display_name && user.display_name.toLowerCase() === targetName) {
                                foundUser = user;
                                foundKey = key;
                                break;
                            }
                        }
                    } catch (e) { /* ignore */ }
                }
                if (foundUser) break;
            } while (cursor !== "0");
        }

        if (!foundUser || !foundKey) {
            return res.status(404).json({ error: `User "${display_name}" not found` });
        }

        // Set reset flags
        if (type === 'weekly' || type === 'both') {
            foundUser.reset_weekly_quest = true;
        }
        if (type === 'daily' || type === 'both') {
            foundUser.reset_daily_quest = true;
        }

        await redis.set(foundKey, JSON.stringify(foundUser));

        console.log(`[Admin] Set quest reset flag (${type}) for user "${foundUser.display_name}"`);

        res.json({
            success: true,
            message: `Quest reset flag (${type}) set for "${foundUser.display_name}". Will take effect on next client sync.`,
            user: {
                display_name: foundUser.display_name,
                unified_id: foundUser.unified_id,
                reset_weekly_quest: foundUser.reset_weekly_quest || false,
                reset_daily_quest: foundUser.reset_daily_quest || false
            }
        });
    } catch (error) {
        console.error('Error setting quest reset flag:', error.message);
        res.status(500).json({ error: 'Failed to set quest reset flag' });
    }
});

/**
 * POST /admin/set-streak
 * Set/fix a user's quest streak data. Supports force-override (even lowering values).
 * Body: { admin_token, display_name, daily_quest_streak?: number, last_daily_quest_date?: string,
 *         quest_completion_dates?: string[], total_daily_quests_completed?: number,
 *         total_weekly_quests_completed?: number, total_xp_from_quests?: number }
 */
app.post('/admin/set-streak', async (req, res) => {
    const { display_name, daily_quest_streak, last_daily_quest_date,
            quest_completion_dates, total_daily_quests_completed,
            total_weekly_quests_completed, total_xp_from_quests } = req.body;
    const admin_token = req.headers['x-admin-token'];

    if (!verifyAdminToken(admin_token)) {
        return res.status(401).json({ error: 'Invalid admin token' });
    }

    if (!display_name || typeof display_name !== 'string') {
        return res.status(400).json({ error: 'display_name required' });
    }

    if (!redis) {
        return res.status(500).json({ error: 'Redis not configured' });
    }

    try {
        // Find user by display_name
        const targetName = display_name.toLowerCase();
        let foundUser = null;
        let foundKey = null;

        // Check display_name index first (fast path)
        const indexKey = `display_name_index:${targetName}`;
        const unifiedId = await redis.get(indexKey);

        if (unifiedId) {
            const userKey = `user:${unifiedId}`;
            const userData = await redis.get(userKey);
            if (userData) {
                foundUser = typeof userData === 'string' ? JSON.parse(userData) : userData;
                foundKey = userKey;
            }
        }

        // Fallback: scan user:* keys
        if (!foundUser) {
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
                cursor = String(result[0]);
                const keys = result[1] || [];
                for (const key of keys) {
                    try {
                        const data = await redis.get(key);
                        if (data) {
                            const user = typeof data === 'string' ? JSON.parse(data) : data;
                            if (user.display_name && user.display_name.toLowerCase() === targetName) {
                                foundUser = user;
                                foundKey = key;
                                break;
                            }
                        }
                    } catch (e) { /* ignore */ }
                }
                if (foundUser) break;
            } while (cursor !== "0");
        }

        if (!foundUser || !foundKey) {
            return res.status(404).json({ error: `User "${display_name}" not found` });
        }

        // Ensure stats object exists
        foundUser.stats = foundUser.stats || {};

        const changes = [];

        // Apply streak changes (force-set, no Math.max)
        if (typeof daily_quest_streak === 'number') {
            foundUser.stats.daily_quest_streak = daily_quest_streak;
            changes.push(`daily_quest_streak=${daily_quest_streak}`);
        }
        if (typeof last_daily_quest_date === 'string') {
            foundUser.stats.last_daily_quest_date = last_daily_quest_date;
            changes.push(`last_daily_quest_date=${last_daily_quest_date}`);
        }
        if (Array.isArray(quest_completion_dates)) {
            const filteredDates = quest_completion_dates.filter(d => typeof d === 'string' && d.length <= 20).slice(-100);
            foundUser.stats.quest_completion_dates = filteredDates;
            changes.push(`quest_completion_dates=[${filteredDates.length} dates]`);
        }
        if (typeof total_daily_quests_completed === 'number') {
            foundUser.stats.total_daily_quests_completed = total_daily_quests_completed;
            changes.push(`total_daily_quests_completed=${total_daily_quests_completed}`);
        }
        if (typeof total_weekly_quests_completed === 'number') {
            foundUser.stats.total_weekly_quests_completed = total_weekly_quests_completed;
            changes.push(`total_weekly_quests_completed=${total_weekly_quests_completed}`);
        }
        if (typeof total_xp_from_quests === 'number') {
            foundUser.stats.total_xp_from_quests = total_xp_from_quests;
            changes.push(`total_xp_from_quests=${total_xp_from_quests}`);
        }

        if (changes.length === 0) {
            return res.status(400).json({ error: 'No changes specified. Provide at least one field to update.' });
        }

        // Set force override flag so client adopts these values even if lower
        foundUser.force_streak_override = true;

        await redis.set(foundKey, JSON.stringify(foundUser));

        console.log(`[Admin] Set streak for "${foundUser.display_name}": ${changes.join(', ')}`);

        res.json({
            success: true,
            message: `Streak updated for "${foundUser.display_name}": ${changes.join(', ')}. Will force-override on next client sync.`,
            user: {
                display_name: foundUser.display_name,
                unified_id: foundUser.unified_id,
                stats: {
                    daily_quest_streak: foundUser.stats.daily_quest_streak,
                    last_daily_quest_date: foundUser.stats.last_daily_quest_date,
                    quest_completion_dates: foundUser.stats.quest_completion_dates,
                    total_daily_quests_completed: foundUser.stats.total_daily_quests_completed,
                    total_weekly_quests_completed: foundUser.stats.total_weekly_quests_completed,
                    total_xp_from_quests: foundUser.stats.total_xp_from_quests
                }
            }
        });
    } catch (error) {
        console.error('Error setting streak:', error.message);
        res.status(500).json({ error: 'Failed to set streak' });
    }
});

/**
 * POST /admin/reset-skills
 * Reset a user's skill tree (refund all points, clear unlocked skills).
 * Body: { admin_token, display_name }
 */
app.post('/admin/reset-skills', async (req, res) => {
    const { display_name } = req.body;
    const admin_token = req.headers['x-admin-token'];

    if (!verifyAdminToken(admin_token)) {
        return res.status(401).json({ error: 'Invalid admin token' });
    }
    if (!display_name || typeof display_name !== 'string') {
        return res.status(400).json({ error: 'display_name required' });
    }
    if (!redis) {
        return res.status(500).json({ error: 'Redis not configured' });
    }

    try {
        const targetName = display_name.toLowerCase();
        let foundUser = null;
        let foundKey = null;

        const indexKey = `display_name_index:${targetName}`;
        const unifiedId = await redis.get(indexKey);
        if (unifiedId) {
            const userKey = `user:${unifiedId}`;
            const userData = await redis.get(userKey);
            if (userData) {
                foundUser = typeof userData === 'string' ? JSON.parse(userData) : userData;
                foundKey = userKey;
            }
        }

        if (!foundUser) {
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
                cursor = String(result[0]);
                const keys = result[1] || [];
                for (const key of keys) {
                    try {
                        const data = await redis.get(key);
                        if (data) {
                            const user = typeof data === 'string' ? JSON.parse(data) : data;
                            if (user.display_name && user.display_name.toLowerCase() === targetName) {
                                foundUser = user;
                                foundKey = key;
                                break;
                            }
                        }
                    } catch (e) { /* ignore */ }
                }
                if (foundUser) break;
            } while (cursor !== "0");
        }

        if (!foundUser || !foundKey) {
            return res.status(404).json({ error: `User "${display_name}" not found` });
        }

        const oldSkillPoints = foundUser.skill_points || 0;
        const oldSkills = foundUser.unlocked_skills || [];
        const POINTS_PER_LEVEL = 1;
        const refundedPoints = (foundUser.level || 1) * POINTS_PER_LEVEL;

        foundUser.skill_points = refundedPoints;
        foundUser.unlocked_skills = [];
        foundUser.force_skills_reset = true;

        await redis.set(foundKey, JSON.stringify(foundUser));

        console.log(`[Admin] Reset skills for "${foundUser.display_name}": ${oldSkills.length} skills cleared, ${oldSkillPoints} -> ${refundedPoints} points`);

        res.json({
            success: true,
            message: `Skills reset for "${foundUser.display_name}". ${oldSkills.length} skills cleared, ${refundedPoints} points refunded (level ${foundUser.level} × ${POINTS_PER_LEVEL}).`,
            user: {
                display_name: foundUser.display_name,
                level: foundUser.level,
                old_skill_points: oldSkillPoints,
                new_skill_points: refundedPoints,
                cleared_skills: oldSkills
            }
        });
    } catch (error) {
        console.error('Error resetting skills:', error.message);
        res.status(500).json({ error: 'Failed to reset skills' });
    }
});

/**
 * POST /admin/reset-all-skills
 * Reset ALL users' skill trees (refund points based on level, clear unlocked skills, set force_skills_reset).
 * Body: { admin_token, dry_run?: boolean }
 */
app.post('/admin/reset-all-skills', async (req, res) => {
    const { dry_run = true } = req.body;
    const admin_token = req.headers['x-admin-token'];

    if (!verifyAdminToken(admin_token)) {
        return res.status(401).json({ error: 'Invalid admin token' });
    }
    if (!redis) {
        return res.status(500).json({ error: 'Redis not configured' });
    }

    try {
        const POINTS_PER_LEVEL = 1;
        const affected = [];
        const skipped = [];
        let cursor = "0";

        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = String(result[0]);
            const keys = result[1] || [];

            for (const key of keys) {
                try {
                    const data = await redis.get(key);
                    if (!data) continue;
                    const user = typeof data === 'string' ? JSON.parse(data) : data;

                    const oldSkills = user.unlocked_skills || [];
                    const oldPoints = user.skill_points || 0;
                    const level = user.level || 1;
                    const refundedPoints = level * POINTS_PER_LEVEL;

                    if (oldSkills.length === 0 && oldPoints === refundedPoints) {
                        skipped.push({ display_name: user.display_name || key, level, reason: 'already clean' });
                        continue;
                    }

                    if (!dry_run) {
                        user.skill_points = refundedPoints;
                        user.unlocked_skills = [];
                        user.force_skills_reset = true;
                        await redis.set(key, JSON.stringify(user));
                    }

                    affected.push({
                        display_name: user.display_name || key,
                        level,
                        old_skill_points: oldPoints,
                        new_skill_points: refundedPoints,
                        skills_cleared: oldSkills.length
                    });
                } catch (e) {
                    skipped.push({ key, reason: e.message });
                }
            }
        } while (cursor !== "0");

        console.log(`[Admin] reset-all-skills (dry_run=${dry_run}): ${affected.length} users affected, ${skipped.length} skipped`);

        res.json({
            success: true,
            dry_run,
            summary: {
                affected_count: affected.length,
                skipped_count: skipped.length
            },
            affected,
            skipped
        });
    } catch (error) {
        console.error('Error resetting all skills:', error.message);
        res.status(500).json({ error: 'Failed to reset all skills' });
    }
});

/**
 * POST /admin/retroactive-og-check
 * Cross-reference a list of discord IDs (from V1 leaderboard backup) against current V2 users.
 * Any V2 user whose discord_id matches a V1 entry gets flagged as is_season0_og = true.
 * Body: { admin_token: string, discord_ids: string[], dry_run?: boolean }
 */
app.post('/admin/retroactive-og-check', async (req, res) => {
    try {
        const { discord_ids, dry_run = true } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!discord_ids || !Array.isArray(discord_ids)) {
            return res.status(400).json({ error: 'discord_ids array required' });
        }

        const results = {
            checked: discord_ids.length,
            found_in_v2: 0,
            already_og: 0,
            newly_flagged: 0,
            not_in_v2: 0,
            flagged_users: [],
            not_found: []
        };

        for (const discordId of discord_ids) {
            if (!discordId) continue;

            // Look up V2 user by discord index
            const indexKey = `discord_index:${discordId}`;
            const unifiedId = await redis.get(indexKey);

            if (!unifiedId) {
                results.not_in_v2++;
                results.not_found.push(discordId);
                continue;
            }

            results.found_in_v2++;
            const userData = await redis.get(`user:${unifiedId}`);
            if (!userData) {
                results.not_in_v2++;
                continue;
            }

            const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

            if (user.is_season0_og) {
                results.already_og++;
                continue;
            }

            // This user was in V1 but not flagged as OG
            results.newly_flagged++;
            results.flagged_users.push({
                unified_id: unifiedId,
                display_name: user.display_name,
                discord_id: discordId,
                level: user.level,
                achievements_count: (user.achievements || []).length
            });

            if (!dry_run) {
                user.is_season0_og = true;
                await redis.set(`user:${unifiedId}`, JSON.stringify(user));
                console.log(`[Admin] Retroactive OG flag set for ${user.display_name} (${unifiedId})`);
            }
        }

        res.json({
            success: true,
            dry_run,
            results
        });
    } catch (error) {
        console.error('Retroactive OG check error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /admin/import-v1-discord-ids
 * Import discord IDs from V1 leaderboard backup into a Redis set for OG detection fallback.
 * The auth endpoints use this set when season0: keys are missing.
 * Body: { admin_token: string, discord_ids: string[] }
 */
app.post('/admin/import-v1-discord-ids', async (req, res) => {
    try {
        const { discord_ids } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!discord_ids || !Array.isArray(discord_ids) || discord_ids.length === 0) {
            return res.status(400).json({ error: 'discord_ids array required' });
        }

        // Filter valid IDs
        const validIds = discord_ids.filter(id => id && typeof id === 'string' && id.length > 0);

        // Add all to the set
        if (validIds.length > 0) {
            await redis.sadd('season0_discord_ids', ...validIds);
        }

        const totalInSet = await redis.scard('season0_discord_ids');

        console.log(`[Admin] Imported ${validIds.length} discord IDs into season0_discord_ids set (total: ${totalInSet})`);
        res.json({
            success: true,
            imported: validIds.length,
            total_in_set: totalInSet
        });
    } catch (error) {
        console.error('Import V1 discord IDs error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /admin/start-new-season
 * Snapshot current leaderboard, reset all users, and update season config.
 * Body: { admin_token: string, new_season: string, season_title?: string, dry_run?: boolean }
 */
app.post('/admin/start-new-season', async (req, res) => {
    try {
        const { new_season, season_title, dry_run = true } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!new_season) {
            return res.status(400).json({ error: 'new_season required (e.g. "2026-03")' });
        }
        const scanBlock = await enforceAdminScanCooldown(res, 'start-new-season');
        if (scanBlock) return;

        const currentSeason = getCurrentSeason();
        const snapshotResult = { season: currentSeason, total_users: 0, stored_as: null };
        const resetResult = { users_reset: 0, target_season: new_season };

        // Step 1: Snapshot current leaderboard
        const leaderboardKey = `leaderboard:${currentSeason}`;
        const leaderboardData = await redis.zrange(leaderboardKey, 0, -1, { withScores: true, rev: true });

        const rankings = [];
        if (leaderboardData && leaderboardData.length > 0) {
            // leaderboardData is an array of { value, score } or flat [member, score, ...]
            // Upstash returns array of { value, score } when withScores is used
            for (let i = 0; i < leaderboardData.length; i++) {
                const entry = leaderboardData[i];
                const unifiedId = typeof entry === 'object' ? entry.value : entry;
                const score = typeof entry === 'object' ? entry.score : leaderboardData[++i];

                // Look up user display name
                let displayName = unifiedId;
                let level = 1;
                try {
                    const uData = await redis.get(`user:${unifiedId}`);
                    if (uData) {
                        const u = typeof uData === 'string' ? JSON.parse(uData) : uData;
                        displayName = u.display_name || unifiedId;
                        level = u.level || 1;
                    }
                } catch (e) { /* skip lookup errors */ }

                rankings.push({
                    rank: rankings.length + 1,
                    unified_id: unifiedId,
                    display_name: displayName,
                    level: level,
                    xp: Number(score) || 0
                });
            }
        }

        snapshotResult.total_users = rankings.length;

        if (!dry_run) {
            // Store snapshot
            const snapshotKey = `leaderboard_snapshot:${currentSeason}`;
            const snapshotData = {
                season: currentSeason,
                snapshot_date: new Date().toISOString(),
                rankings: rankings
            };
            await redis.set(snapshotKey, JSON.stringify(snapshotData));
            snapshotResult.stored_as = snapshotKey;
        }

        // Step 2: Reset all users
        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = String(result[0]);

            for (const key of (result[1] || [])) {
                try {
                    const userData = await redis.get(key);
                    if (!userData) continue;

                    const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
                    const unifiedId = key.replace('user:', '');

                    if (user.current_season === new_season) continue;

                    if (!dry_run) {
                        // Archive stats
                        if (!user.all_time_stats) user.all_time_stats = {};
                        user.all_time_stats.total_flashes = (user.all_time_stats.total_flashes || 0) + (user.stats?.total_flashes || 0);
                        user.all_time_stats.total_bubbles_popped = (user.all_time_stats.total_bubbles_popped || 0) + (user.stats?.total_bubbles_popped || 0);
                        user.all_time_stats.total_video_minutes = (user.all_time_stats.total_video_minutes || 0) + (user.stats?.total_video_minutes || 0);
                        user.all_time_stats.total_lock_cards_completed = (user.all_time_stats.total_lock_cards_completed || 0) + (user.stats?.total_lock_cards_completed || 0);
                        user.all_time_stats.seasons_completed = (user.all_time_stats.seasons_completed || 0) + 1;

                        user.highest_level_ever = Math.max(user.highest_level_ever || 0, user.level || 1);
                        user.unlocks = calculateUnlocks(user.highest_level_ever);

                        const oldSeason = user.current_season;

                        user.xp = 0;
                        user.level = 1;
                        user.stats = {
                            total_flashes: 0,
                            total_bubbles_popped: 0,
                            total_video_minutes: 0,
                            total_lock_cards_completed: 0
                        };
                        user.current_season = new_season;
                        user.level_reset_at = new Date().toISOString();
                        // Clear oopsie insurance for new season
                        delete user.oopsie_used_season;
                        user.updated_at = new Date().toISOString();

                        await redis.set(key, JSON.stringify(user));

                        if (oldSeason) {
                            await redis.zrem(`leaderboard:${oldSeason}`, unifiedId);
                        }
                        await redis.zadd(`leaderboard:${new_season}`, { score: 0, member: unifiedId });
                        try { await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(user.total_xp_earned || 0), member: unifiedId }); } catch (e) { /* best-effort */ }
                    }

                    resetResult.users_reset++;
                } catch (e) {
                    console.error(`Season reset error for ${key}:`, e.message);
                }
            }
        } while (cursor !== "0");

        // Step 3: Update season config
        if (!dry_run) {
            const seasonConfig = {
                season: new_season,
                title: season_title || null,
                started_at: new Date().toISOString()
            };
            await redis.set('season_config', JSON.stringify(seasonConfig));
        }

        console.log(`[ADMIN] start-new-season: ${new_season} (dry_run: ${dry_run}), snapshot: ${snapshotResult.total_users} users, reset: ${resetResult.users_reset} users`);

        res.json({
            success: true,
            dry_run,
            snapshot: snapshotResult,
            reset: resetResult,
            season_config: {
                title: season_title || null
            }
        });
    } catch (error) {
        console.error('Admin start-new-season error:', error.message);
        res.status(500).json({ error: 'Failed to start new season' });
    }
});

/**
 * POST /admin/update-season-title
 * Update the season_config title without resetting users.
 * Body: { season_title: string }
 */
app.post('/admin/update-season-title', async (req, res) => {
    try {
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const { season_title } = req.body;
        if (!season_title || typeof season_title !== 'string') {
            return res.status(400).json({ error: 'season_title required (string)' });
        }
        // Sanitize: trim, strip control chars, cap length
        const sanitized = season_title.replace(/[\x00-\x1f\x7f]/g, '').trim();
        if (sanitized.length === 0 || sanitized.length > 100) {
            return res.status(400).json({ error: 'season_title must be 1-100 printable characters' });
        }

        // Read existing config, update title only (proto-pollution safe)
        let seasonConfig = Object.create(null);
        try {
            const existing = await redis.get('season_config');
            if (existing) {
                const parsed = typeof existing === 'string' ? JSON.parse(existing) : existing;
                if (parsed.season) seasonConfig.season = String(parsed.season);
                if (parsed.started_at) seasonConfig.started_at = String(parsed.started_at);
            }
        } catch (e) { /* start fresh */ }

        seasonConfig.title = sanitized;
        await redis.set('season_config', JSON.stringify(seasonConfig));

        // Bust quest definitions cache so clients pick up new title
        questDefinitionsCache = null;
        questDefinitionsCacheTime = 0;

        console.log(`[ADMIN] update-season-title: "${sanitized}"`);
        res.json({ success: true, title: sanitized });
    } catch (error) {
        console.error('Admin update-season-title error:', error.message);
        res.status(500).json({ error: 'Failed to update season title' });
    }
});

/**
 * GET /admin/leaderboard-snapshot/:season
 * Retrieve a stored leaderboard snapshot for a given season.
 * Query: ?admin_token=xxx
 */
app.get('/admin/leaderboard-snapshot/:season', async (req, res) => {
    try {
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const season = req.params.season;
        // Validate season format to prevent arbitrary Redis key lookups
        if (!/^\d{4}-\d{2}$/.test(season)) {
            return res.status(400).json({ error: 'Invalid season format. Expected YYYY-MM.' });
        }
        const snapshotKey = `leaderboard_snapshot:${season}`;
        const data = await redis.get(snapshotKey);

        if (!data) {
            return res.status(404).json({ error: `No snapshot found for season ${season}` });
        }

        const snapshot = typeof data === 'string' ? JSON.parse(data) : data;
        res.json(snapshot);
    } catch (error) {
        console.error('Admin leaderboard-snapshot error:', error.message);
        res.status(500).json({ error: 'Failed to fetch leaderboard snapshot' });
    }
});

/**
 * POST /admin/season/update
 * Update season config (title) and optionally quest definitions.
 * Body: { admin_token: string, title?: string, quests?: { daily: [...], weekly: [...], seasonal: [...] } }
 */
app.post('/admin/season/update', async (req, res) => {
    try {
        const { title, quests } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const changes = [];

        // Update season config title
        if (title !== undefined) {
            let seasonConfig = {};
            try {
                const existing = await redis.get('season_config');
                if (existing) {
                    seasonConfig = typeof existing === 'string' ? JSON.parse(existing) : existing;
                }
            } catch (e) { /* start fresh */ }

            seasonConfig.title = title;
            seasonConfig.season = seasonConfig.season || getCurrentSeason();
            await redis.set('season_config', JSON.stringify(seasonConfig));
            changes.push(`title updated to "${title}"`);
        }

        // Update quest definitions if provided
        if (quests) {
            const current = await getQuestDefinitions();
            const updated = {
                daily: quests.daily || current.daily || DEFAULT_QUEST_DEFINITIONS.daily,
                weekly: quests.weekly || current.weekly || DEFAULT_QUEST_DEFINITIONS.weekly,
                seasonal: quests.seasonal || current.seasonal || [],
                version: (current.version || 0) + 1,
                updatedAt: new Date().toISOString()
            };
            await redis.set(QUEST_REDIS_KEY, JSON.stringify(updated));
            changes.push(`quests updated (v${updated.version}): ${updated.daily.length} daily, ${updated.weekly.length} weekly, ${updated.seasonal.length} seasonal`);
        }

        console.log(`[ADMIN] season/update: ${changes.join(', ')}`);

        res.json({
            success: true,
            changes
        });
    } catch (error) {
        console.error('Admin season/update error:', error.message);
        res.status(500).json({ error: 'Failed to update season config' });
    }
});

/**
 * POST /admin/merge-accounts
 * Merge two user accounts into one. Source account is merged INTO target, then deleted.
 * Body: {
 *   admin_token: string,
 *   source_id: string,              // account to merge FROM (will be deleted)
 *   target_id: string,              // account to keep
 *   override_display_name?: string, // force display name on target
 *   override_level?: number,        // force level
 *   override_xp?: number,           // force xp
 *   set_whitelisted?: boolean,      // set patreon_is_whitelisted
 *   set_og?: boolean                // set is_season0_og
 * }
 */
app.post('/admin/merge-accounts', async (req, res) => {
    try {
        const {
            source_id, target_id,
            override_display_name, override_level, override_xp,
            set_whitelisted, set_og, dry_run = false
        } = req.body;
        const admin_token = req.headers['x-admin-token'];

        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!source_id || !target_id || typeof source_id !== 'string' || typeof target_id !== 'string') {
            return res.status(400).json({ error: 'source_id and target_id required' });
        }
        // Validate unified_id format: alphanumeric + hyphens/underscores, max 64 chars
        const ID_FORMAT = /^[a-zA-Z0-9_-]{1,64}$/;
        if (!ID_FORMAT.test(source_id) || !ID_FORMAT.test(target_id)) {
            return res.status(400).json({ error: 'Invalid account ID format' });
        }
        if (source_id === target_id) {
            return res.status(400).json({ error: 'source_id and target_id must be different' });
        }

        // Fetch both accounts
        const [sourceData, targetData] = await Promise.all([
            redis.get(`user:${source_id}`),
            redis.get(`user:${target_id}`)
        ]);

        if (!sourceData) return res.status(404).json({ error: `Source account not found: ${source_id}` });
        if (!targetData) return res.status(404).json({ error: `Target account not found: ${target_id}` });

        const source = typeof sourceData === 'string' ? JSON.parse(sourceData) : sourceData;
        const target = typeof targetData === 'string' ? JSON.parse(targetData) : targetData;
        const changes = [];
        const deletedKeys = [];

        // 1. Merge achievements (union, deduplicated)
        const mergedAchievements = [...new Set([
            ...(target.achievements || []),
            ...(source.achievements || [])
        ])];
        if (mergedAchievements.length !== (target.achievements || []).length) {
            changes.push(`achievements: ${(target.achievements || []).length} -> ${mergedAchievements.length}`);
        }
        target.achievements = mergedAchievements;

        // 2. Merge stats into all_time_stats
        // Take max of each stat from source's stats + source's all_time_stats, accumulate into target's all_time_stats
        const sourceAllTimeStats = { ...(source.all_time_stats || {}) };
        const sourceCurrentStats = source.stats || {};
        // Combine source's current stats and all_time_stats (take max for each key)
        const sourceCombined = {};
        for (const key of new Set([...Object.keys(sourceAllTimeStats), ...Object.keys(sourceCurrentStats)])) {
            sourceCombined[key] = Math.max(sourceAllTimeStats[key] || 0, sourceCurrentStats[key] || 0);
        }
        // Now accumulate into target's all_time_stats
        const targetAllTime = { ...(target.all_time_stats || {}) };
        for (const [key, value] of Object.entries(sourceCombined)) {
            targetAllTime[key] = (targetAllTime[key] || 0) + value;
        }
        if (Object.keys(sourceCombined).length > 0) {
            changes.push(`all_time_stats: merged ${Object.keys(sourceCombined).length} stat keys from source`);
        }
        target.all_time_stats = targetAllTime;

        // 3. Transfer linked accounts if target doesn't have them
        if (source.discord_id && !target.discord_id) {
            target.discord_id = source.discord_id;
            // discord_username intentionally not transferred — PII
            // Update discord index to point to target
            if (!dry_run) await redis.set(`discord_index:${source.discord_id}`, target_id);
            changes.push(`discord_id transferred: ${source.discord_id}`);
        } else if (source.discord_id) {
            // Source discord index needs cleanup (target already has one)
            if (!dry_run) await redis.del(`discord_index:${source.discord_id}`);
            deletedKeys.push(`discord_index:${source.discord_id}`);
        }

        if (source.patreon_id && !target.patreon_id) {
            target.patreon_id = source.patreon_id;
            target.patreon_tier = source.patreon_tier || target.patreon_tier;
            target.patreon_is_active = source.patreon_is_active || target.patreon_is_active;
            // Update patreon index to point to target
            if (!dry_run) await redis.set(`patreon_index:${source.patreon_id}`, target_id);
            changes.push(`patreon_id transferred: ${source.patreon_id}`);
        } else if (source.patreon_id) {
            if (!dry_run) await redis.del(`patreon_index:${source.patreon_id}`);
            deletedKeys.push(`patreon_index:${source.patreon_id}`);
        }

        if (source.email_hash && !target.email_hash) {
            target.email_hash = source.email_hash;
            if (!dry_run) await redis.set(`email_index:${source.email_hash}`, target_id);
            changes.push(`email_hash transferred`);
        } else if (source.email_hash) {
            if (!dry_run) await redis.del(`email_index:${source.email_hash}`);
            deletedKeys.push(`email_index:${source.email_hash}`);
        }

        // 4. Preserve highest_level_ever (take max from both)
        const sourceHighest = source.highest_level_ever || source.level || 1;
        const targetHighest = target.highest_level_ever || target.level || 1;
        target.highest_level_ever = Math.max(sourceHighest, targetHighest);
        if (target.highest_level_ever !== targetHighest) {
            changes.push(`highest_level_ever: ${targetHighest} -> ${target.highest_level_ever}`);
        }

        // 5. Apply overrides
        if (override_display_name !== undefined) {
            const oldName = target.display_name;
            // Delete old display_name_index
            if (oldName) {
                if (!dry_run) await redis.del(`display_name_index:${oldName.toLowerCase()}`);
                deletedKeys.push(`display_name_index:${oldName.toLowerCase()}`);
            }
            target.display_name = override_display_name;
            if (!dry_run) await redis.set(`display_name_index:${override_display_name.toLowerCase()}`, target_id);
            changes.push(`display_name: "${oldName}" -> "${override_display_name}"`);
        }

        if (override_level !== undefined) {
            changes.push(`level: ${target.level} -> ${override_level}`);
            target.level = override_level;
        }

        if (override_xp !== undefined) {
            changes.push(`xp: ${target.xp} -> ${override_xp}`);
            target.xp = override_xp;
        }

        if (set_whitelisted !== undefined) {
            target.patreon_is_whitelisted = set_whitelisted;
            changes.push(`patreon_is_whitelisted: ${set_whitelisted}`);
        }

        if (set_og !== undefined) {
            target.is_season0_og = set_og;
            changes.push(`is_season0_og: ${set_og}`);
        }

        // 6. Recalculate unlocks
        target.unlocks = calculateUnlocks(target.highest_level_ever || 0);

        // 7. Update timestamp
        target.updated_at = new Date().toISOString();

        // 8. Save merged target
        if (!dry_run) await redis.set(`user:${target_id}`, JSON.stringify(target));

        // 9. Update leaderboard — add/update target, remove source
        const season = target.current_season || getCurrentSeason();
        const leaderboardXp = safeLeaderboardScore(target.xp);
        if (!dry_run) await redis.zadd(`leaderboard:${season}`, { score: leaderboardXp, member: target_id });
        if (!dry_run) { try { await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(target.total_xp_earned || 0), member: target_id }); } catch (e) { /* best-effort */ } }
        changes.push(`leaderboard:${season} updated for target (xp: ${leaderboardXp})`);

        if (source.current_season) {
            if (!dry_run) await redis.zrem(`leaderboard:${source.current_season}`, source_id);
            if (!dry_run) { try { await redis.zrem('leaderboard:all-time', source_id); } catch (e) { /* best-effort */ } }
            deletedKeys.push(`leaderboard:${source.current_season} (source removed)`);
        }

        // 10. Delete source account and its indexes
        // Delete source display_name_index
        if (source.display_name) {
            const sourceNameKey = `display_name_index:${source.display_name.toLowerCase()}`;
            // Only delete if it still points to source (might have been overridden above)
            const currentMapping = await redis.get(sourceNameKey);
            if (currentMapping === source_id) {
                if (!dry_run) await redis.del(sourceNameKey);
                deletedKeys.push(sourceNameKey);
            }
        }

        // Delete source user record
        if (!dry_run) await redis.del(`user:${source_id}`);
        deletedKeys.push(`user:${source_id}`);

        // 11. Clean up legacy profile keys (profile:<patreon_id>, discord_profile:<discord_id>)
        // Delete source's legacy keys
        if (source.patreon_id) {
            const sourceProfileKey = `profile:${source.patreon_id}`;
            if (await redis.get(sourceProfileKey)) {
                if (!dry_run) await redis.del(sourceProfileKey);
                deletedKeys.push(sourceProfileKey);
            }
        }
        if (source.discord_id) {
            const sourceDiscordKey = `discord_profile:${source.discord_id}`;
            if (await redis.get(sourceDiscordKey)) {
                if (!dry_run) await redis.del(sourceDiscordKey);
                deletedKeys.push(sourceDiscordKey);
            }
        }

        // Update target's legacy keys to match merged state
        if (target.patreon_id) {
            const targetProfileKey = `profile:${target.patreon_id}`;
            const existing = await redis.get(targetProfileKey);
            if (existing) {
                const profile = typeof existing === 'string' ? JSON.parse(existing) : existing;
                profile.level = target.level;
                profile.xp = target.xp;
                profile.display_name = target.display_name;
                profile.achievements = target.achievements;
                profile.stats = target.stats;
                profile.updated_at = target.updated_at;
                if (!dry_run) await redis.set(targetProfileKey, JSON.stringify(profile));
                changes.push(`updated legacy key ${targetProfileKey}`);
            }
        }
        if (target.discord_id) {
            const targetDiscordKey = `discord_profile:${target.discord_id}`;
            const existing = await redis.get(targetDiscordKey);
            if (existing) {
                const profile = typeof existing === 'string' ? JSON.parse(existing) : existing;
                profile.level = target.level;
                profile.xp = target.xp;
                profile.display_name = target.display_name;
                profile.achievements = target.achievements;
                profile.stats = target.stats;
                profile.updated_at = target.updated_at;
                if (!dry_run) await redis.set(targetDiscordKey, JSON.stringify(profile));
                changes.push(`updated legacy key ${targetDiscordKey}`);
            }
        }

        console.log(`[ADMIN] ${dry_run ? 'DRY RUN ' : ''}Merged accounts: ${source_id} -> ${target_id} | Changes: ${changes.join(', ')}`);

        res.json({
            success: true,
            dry_run: !!dry_run,
            merged_from: source_id,
            merged_into: target_id,
            changes,
            deleted_keys: deletedKeys,
            merged_user: {
                unified_id: target.unified_id,
                display_name: target.display_name,
                level: target.level,
                xp: target.xp,
                achievements_count: target.achievements?.length || 0,
                discord_id: target.discord_id,
                patreon_id: target.patreon_id,
                is_season0_og: target.is_season0_og,
                patreon_is_whitelisted: target.patreon_is_whitelisted,
                highest_level_ever: target.highest_level_ever,
                all_time_stats: target.all_time_stats,
                stats: target.stats,
                current_season: target.current_season,
                unlocks: target.unlocks
            }
        });
    } catch (error) {
        console.error('Admin merge-accounts error:', error.message);
        res.status(500).json({ error: 'Failed to merge accounts' });
    }
});

/**
 * POST /admin/clear-patron-display-names
 * Find all users whose display_name matches their patron_name (auto-populated, not user-chosen)
 * and null out their display_name so they get prompted to pick a new one on next login.
 * Body: { admin_token: string, dry_run?: boolean }
 */
app.post('/admin/clear-patron-display-names', async (req, res) => {
    try {
        const { dry_run = true } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock = await enforceAdminScanCooldown(res, 'clear-patron-display-names');
        if (scanBlock) return;

        const affected = [];
        const skipped = [];

        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                try {
                    const data = await redis.get(key);
                    if (!data) continue;
                    const user = typeof data === 'string' ? JSON.parse(data) : data;

                    // Skip users without both fields
                    if (!user.display_name || !user.patron_name) continue;

                    // Check if display_name matches patron_name (case-insensitive)
                    if (user.display_name.toLowerCase().trim() !== user.patron_name.toLowerCase().trim()) {
                        continue;
                    }

                    affected.push({
                        unified_id: user.unified_id,
                        display_name: user.display_name,
                        level: user.level
                    });

                    if (!dry_run) {
                        // Delete display_name_index
                        await redis.del(`display_name_index:${user.display_name.toLowerCase()}`);

                        // Null out display_name on user record
                        user.display_name = null;
                        user.display_name_set_at = null;
                        await redis.set(key, JSON.stringify(user));

                        // Also null on legacy profile keys
                        if (user.patreon_id) {
                            const profileKey = `profile:${user.patreon_id}`;
                            const profileData = await redis.get(profileKey);
                            if (profileData) {
                                const profile = typeof profileData === 'string' ? JSON.parse(profileData) : profileData;
                                profile.display_name = null;
                                await redis.set(profileKey, JSON.stringify(profile));
                            }
                        }
                        if (user.discord_id) {
                            const discordKey = `discord_profile:${user.discord_id}`;
                            const discordData = await redis.get(discordKey);
                            if (discordData) {
                                const profile = typeof discordData === 'string' ? JSON.parse(discordData) : discordData;
                                profile.display_name = null;
                                await redis.set(discordKey, JSON.stringify(profile));
                            }
                        }
                    }
                } catch (e) {
                    skipped.push({ key, error: e.message });
                }
            }
        } while (cursor !== "0");

        console.log(`[ADMIN] clear-patron-display-names: ${affected.length} affected, ${skipped.length} skipped (dry_run=${dry_run})`);

        res.json({
            success: true,
            dry_run,
            affected_count: affected.length,
            skipped_count: skipped.length,
            affected,
            skipped
        });
    } catch (error) {
        console.error('Admin clear-patron-display-names error:', error.message);
        res.status(500).json({ error: 'Failed to clear patron display names' });
    }
});

/**
 * POST /admin/hash-emails
 * Migrate all plaintext email fields to email_hash (HMAC-SHA256).
 * - Scans user:*, profile:*, discord_profile:* records
 * - For each with `email` field: creates `email_hash`, deletes `email`
 * - Recreates email_index keys with hashed values
 * - Migrates whitelist:emails Set to hashed values
 * Body: { admin_token: string, dry_run?: boolean }
 */
app.post('/admin/hash-emails', async (req, res) => {
    // Migration completed Feb 24 2026 — endpoint disabled to prevent accidental re-run
    return res.status(410).json({ error: 'Email hashing migration completed. Endpoint disabled.' });
    /* Original migration code preserved below for reference
        const stats = {
            users_scanned: 0, users_migrated: 0,
            profiles_scanned: 0, profiles_migrated: 0,
            discord_profiles_scanned: 0, discord_profiles_migrated: 0,
            email_indexes_created: 0, email_indexes_deleted: 0,
            whitelist_emails_migrated: 0,
            errors: []
        };

        // Helper: migrate a single record
        const migrateRecord = async (key, prefix) => {
            try {
                const data = await redis.get(key);
                if (!data) return;
                const record = typeof data === 'string' ? JSON.parse(data) : data;

                if (record.email && !record.email_hash) {
                    const hashed = hashEmail(record.email);
                    record.email_hash = hashed;
                    delete record.email;

                    if (!dry_run) {
                        await redis.set(key, JSON.stringify(record));
                    }

                    // Create new hashed email_index, delete old plaintext one
                    const unifiedId = record.unified_id || key.replace(`${prefix}:`, '');
                    if (record.email_hash && isValidUnifiedId(unifiedId)) {
                        if (!dry_run) {
                            await redis.set(`email_index:${hashed}`, unifiedId);
                        }
                        stats.email_indexes_created++;
                    }

                    return true; // migrated
                }
                return false; // already migrated or no email
            } catch (e) {
                stats.errors.push({ key, error: e.message });
                return false;
            }
        };

        // Scan user:* records
        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                stats.users_scanned++;
                if (await migrateRecord(key, 'user')) stats.users_migrated++;
            }
        } while (cursor !== "0");

        // Scan profile:* records
        cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'profile:*', count: 100 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                stats.profiles_scanned++;
                if (await migrateRecord(key, 'profile')) stats.profiles_migrated++;
            }
        } while (cursor !== "0");

        // Scan discord_profile:* records
        cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'discord_profile:*', count: 100 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                stats.discord_profiles_scanned++;
                if (await migrateRecord(key, 'discord_profile')) stats.discord_profiles_migrated++;
            }
        } while (cursor !== "0");

        // Delete old plaintext email_index:* keys (those containing @ are plaintext)
        cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'email_index:*', count: 100 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                const suffix = key.replace('email_index:', '');
                // Plaintext emails contain @, hashed ones are 64-char hex
                if (suffix.includes('@')) {
                    if (!dry_run) {
                        await redis.del(key);
                    }
                    stats.email_indexes_deleted++;
                }
            }
        } while (cursor !== "0");

        // Migrate whitelist:emails Set to hashed values
        const whitelistEmailsRaw = await redis.smembers('whitelist:emails');
        if (whitelistEmailsRaw && whitelistEmailsRaw.length > 0) {
            const plaintextEmails = whitelistEmailsRaw.filter(e => e.includes('@'));
            if (plaintextEmails.length > 0) {
                const hashedEmails = plaintextEmails.map(e => hashEmail(e));
                if (!dry_run) {
                    // Remove plaintext, add hashed
                    await redis.srem('whitelist:emails', ...plaintextEmails);
                    await redis.sadd('whitelist:emails', ...hashedEmails);
                    // Reload in-memory cache
                    await loadWhitelistFromRedis();
                }
                stats.whitelist_emails_migrated = plaintextEmails.length;
            }
        }

        console.log(`[ADMIN] hash-emails: dry_run=${dry_run}, users=${stats.users_migrated}/${stats.users_scanned}, profiles=${stats.profiles_migrated}/${stats.profiles_scanned}, discord=${stats.discord_profiles_migrated}/${stats.discord_profiles_scanned}, indexes_created=${stats.email_indexes_created}, indexes_deleted=${stats.email_indexes_deleted}, whitelist=${stats.whitelist_emails_migrated}`);

        res.json({ dry_run, ...stats });
    } catch (error) {
        console.error('Admin hash-emails error:', error.message);
        res.status(500).json({ error: 'Failed to hash emails' });
    }
    */
});

/**
 * POST /admin/purge-patron-names
 * Remove patron_name field from ALL records (user:*, profile:*, discord_profile:*).
 * If a user's display_name matches their patron_name (and was never explicitly set),
 * clear display_name too so they get prompted to pick a new one.
 * Body: { admin_token: string, dry_run?: boolean }
 */
app.post('/admin/purge-patron-names', async (req, res) => {
    try {
        const { dry_run = true } = req.body;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock = await enforceAdminScanCooldown(res, 'purge-patron-names');
        if (scanBlock) return;

        let purgedCount = 0;
        let displayNameClearedCount = 0;
        const errors = [];
        const byPrefix = {};

        // Scan all three key patterns that may contain patron_name
        const patterns = ['user:*', 'profile:*', 'discord_profile:*'];
        for (const pattern of patterns) {
            let prefixCount = 0;
            let cursor = "0";
            do {
                const result = await redis.scan(cursor, { match: pattern, count: 100 });
                cursor = String(result[0]);
                for (const key of (result[1] || [])) {
                    try {
                        const data = await redis.get(key);
                        if (!data) continue;
                        const record = typeof data === 'string' ? JSON.parse(data) : data;
                        if (!record.patron_name) continue;

                        // If display_name matches patron_name and was never explicitly set, clear it
                        if (record.display_name && !record.display_name_set_at &&
                            record.display_name.toLowerCase().trim() === record.patron_name.toLowerCase().trim()) {
                            if (!dry_run) {
                                // Clean up display_name index before clearing
                                const indexKey = `display_name_index:${record.display_name.toLowerCase().trim()}`;
                                await redis.del(indexKey);
                                record.display_name = null;
                            }
                            displayNameClearedCount++;
                        }

                        if (!dry_run) {
                            delete record.patron_name;
                            record.updated_at = new Date().toISOString();
                            await redis.set(key, JSON.stringify(record));
                        }
                        purgedCount++;
                        prefixCount++;
                    } catch (e) {
                        errors.push({ key, error: e.message });
                    }
                }
            } while (cursor !== "0");
            byPrefix[pattern] = prefixCount;
        }

        console.log(`[ADMIN] purge-patron-names: ${purgedCount} purged, ${displayNameClearedCount} display_names cleared (dry_run=${dry_run}), by prefix: ${JSON.stringify(byPrefix)}`);

        res.json({
            success: true,
            dry_run,
            purged_count: purgedCount,
            display_name_cleared_count: displayNameClearedCount,
            by_prefix: byPrefix,
            error_count: errors.length,
            errors: errors.slice(0, 20)
        });
    } catch (error) {
        console.error('Admin purge-patron-names error:', error.message);
        res.status(500).json({ error: 'Failed to purge patron names' });
    }
});

/**
 * POST /admin/migrate-total-xp
 * Backfill total_xp_earned from existing user XP data and populate leaderboard:all-time sorted set.
 * Body: { dry_run?: boolean }
 */
app.post('/admin/migrate-total-xp', async (req, res) => {
    try {
        if (!verifyAdminToken(req.headers['x-admin-token'])) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // SCAN cooldown to prevent spamming
        const cooldownKey = 'admin_scan_cooldown:migrate-total-xp';
        const cooldownSet = await redis.set(cooldownKey, '1', { nx: true, ex: 30 });
        if (!cooldownSet) {
            return res.status(429).json({ error: 'Migration cooldown active. Please wait 30 seconds.' });
        }

        const { dry_run = true } = req.body;
        let cursor = "0";
        let migrated = 0;
        let skipped = 0;
        let errors = 0;

        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = String(result[0]);

            for (const key of (result[1] || [])) {
                try {
                    const userData = await redis.get(key);
                    if (!userData) continue;

                    const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
                    const unifiedId = user.unified_id || key.replace('user:', '');

                    // Set total_xp_earned from current XP (historical XP from past seasons is not recoverable)
                    const totalXp = user.xp || 0;

                    if (!dry_run) {
                        user.total_xp_earned = totalXp;
                        await redis.set(key, JSON.stringify(user));
                        await redis.zadd('leaderboard:all-time', { score: safeLeaderboardScore(totalXp), member: unifiedId });
                    }

                    migrated++;
                } catch (e) {
                    errors++;
                }
            }
        } while (cursor !== "0");

        res.json({
            success: true,
            dry_run,
            migrated,
            skipped,
            errors
        });
    } catch (error) {
        console.error('migrate-total-xp error:', error.message);
        res.status(500).json({ error: 'Migration failed' });
    }
});

/**
 * POST /admin/purge-discord-usernames
 * Remove discord_username field from ALL records (user:*, discord_profile:*).
 * Body: { dry_run?: boolean }
 */
app.post('/admin/purge-discord-usernames', async (req, res) => {
    try {
        const { dry_run = true } = req.body;
        if (!verifyAdminToken(req.headers['x-admin-token'])) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlock = await enforceAdminScanCooldown(res, 'purge-discord-usernames');
        if (scanBlock) return;

        let purgedCount = 0;
        const byPrefix = {};
        const prefixes = ['user:', 'discord_profile:'];

        for (const prefix of prefixes) {
            let cursor = "0";
            let prefixCount = 0;
            do {
                const result = await redis.scan(cursor, { match: `${prefix}*`, count: 100 });
                cursor = String(result[0]);
                for (const key of (result[1] || [])) {
                    try {
                        const raw = await redis.get(key);
                        if (!raw) continue;
                        const record = typeof raw === 'string' ? JSON.parse(raw) : raw;
                        if (record.discord_username !== undefined) {
                            delete record.discord_username;
                            if (!dry_run) {
                                await redis.set(key, JSON.stringify(record));
                            }
                            prefixCount++;
                            purgedCount++;
                        }
                    } catch (e) { /* skip corrupt records */ }
                }
            } while (cursor !== "0");
            byPrefix[prefix.replace(':', '')] = prefixCount;
        }

        console.log(`[ADMIN] purge-discord-usernames: ${purgedCount} purged (dry_run=${dry_run}), by prefix: ${JSON.stringify(byPrefix)}`);

        res.json({
            dry_run,
            total_purged: purgedCount,
            by_prefix: byPrefix
        });
    } catch (error) {
        console.error('Admin purge-discord-usernames error:', error.message);
        res.status(500).json({ error: 'Failed to purge discord usernames' });
    }
});

/**
 * POST /admin/set-highest-level
 * Set a user's highest_level_ever without changing their current level/xp.
 * This is the key "unstick a user" tool — lets admins grant feature unlocks
 * without altering the user's actual progression.
 * Body: { display_name: string, highest_level: number, admin_token: string }
 */
app.post('/admin/set-highest-level', async (req, res) => {
    try {
        const { display_name, highest_level } = req.body;
        const admin_token = req.headers['x-admin-token'];

        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }

        if (!display_name || typeof display_name !== 'string') {
            return res.status(400).json({ error: 'display_name is required' });
        }

        if (typeof highest_level !== 'number' || highest_level < 0 || highest_level > 999) {
            return res.status(400).json({ error: 'highest_level must be a number between 0 and 999' });
        }

        if (!redis) {
            return res.status(503).json({ error: 'Redis not available' });
        }

        // Try display_name_index first (O(1) lookup instead of SCAN)
        const indexKey = `display_name_index:${display_name.trim().toLowerCase()}`;
        const indexedId = await redis.get(indexKey);
        if (indexedId) {
            const userData = await redis.get(`user:${indexedId}`);
            if (userData) {
                const profile = typeof userData === 'string' ? JSON.parse(userData) : userData;
                const oldLevel = profile.highest_level_ever || 0;
                profile.highest_level_ever = highest_level;
                await redis.set(`user:${indexedId}`, JSON.stringify(profile));
                return res.json({
                    success: true,
                    unified_id: profile.unified_id,
                    display_name: profile.display_name,
                    old_highest_level: oldLevel,
                    new_highest_level: highest_level
                });
            }
        }

        // Fallback: Scan for V2 user:* record by display_name
        const scanBlock = await enforceAdminScanCooldown(res, 'set-highest-level');
        if (scanBlock) return;
        const targetName = display_name.toLowerCase();
        let foundKey = null;
        let foundProfile = null;

        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = String(result[0]);
            const keys = result[1] || [];

            for (const key of keys) {
                try {
                    const data = await redis.get(key);
                    if (data) {
                        const profile = typeof data === 'string' ? JSON.parse(data) : data;
                        if (profile.display_name && profile.display_name.toLowerCase() === targetName) {
                            foundKey = key;
                            foundProfile = profile;
                            break;
                        }
                    }
                } catch (e) { /* ignore parse errors */ }
            }
            if (foundKey) break;
        } while (cursor !== "0");

        if (!foundKey) {
            return res.status(404).json({ error: `No V2 user found with display_name "${display_name}"` });
        }

        const oldHighest = foundProfile.highest_level_ever || 0;
        foundProfile.highest_level_ever = highest_level;
        foundProfile.unlocks = calculateUnlocks(highest_level);
        foundProfile.updated_at = new Date().toISOString();

        await redis.set(foundKey, JSON.stringify(foundProfile));

        console.log(`[ADMIN] Set highest_level_ever for "${display_name}" (${foundKey}): ${oldHighest} -> ${highest_level}`);

        res.json({
            success: true,
            display_name: display_name,
            profile_key: foundKey,
            old_highest_level_ever: oldHighest,
            new_highest_level_ever: highest_level,
            current_level: foundProfile.level || 1,
            unlocks: foundProfile.unlocks
        });
    } catch (error) {
        console.error('Admin set-highest-level error:', error.message);
        res.status(500).json({ error: 'Failed to set highest level' });
    }
});

/**
 * GET /admin/suspicious-accounts
 * List accounts with anti-cheat flags for manual review
 * Query: admin_token, min_flags (default 1)
 */
app.get('/admin/suspicious-accounts', async (req, res) => {
    try {
        const { min_flags = 1 } = req.query;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlockSusp = await enforceAdminScanCooldown(res, 'suspicious-accounts');
        if (scanBlockSusp) return;

        const minFlagsNum = parseInt(min_flags) || 1;
        const suspicious = [];

        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = result[0] || result.cursor || "0";
            const keys = result[1] || result.keys || [];

            for (const key of keys) {
                try {
                    const data = await redis.get(key);
                    const user = typeof data === 'string' ? JSON.parse(data) : data;
                    if (user?.anti_cheat_flags && user.anti_cheat_flags.length >= minFlagsNum) {
                        suspicious.push({
                            unified_id: user.unified_id,
                            display_name: user.display_name,
                            level: user.level,
                            xp: user.xp,
                            flag_count: user.anti_cheat_flags.length,
                            flags: user.anti_cheat_flags,
                            last_sync_at: user.last_sync_at,
                            last_seen: user.last_seen
                        });
                    }
                } catch (e) {
                    // Skip unparseable entries
                }
            }
        } while (cursor !== "0" && cursor !== 0);

        // Sort by flag count descending
        suspicious.sort((a, b) => b.flag_count - a.flag_count);

        res.json({
            total: suspicious.length,
            min_flags: minFlagsNum,
            accounts: suspicious
        });
    } catch (error) {
        console.error('Admin suspicious-accounts error:', error.message);
        res.status(500).json({ error: 'Failed to query suspicious accounts' });
    }
});

/**
 * GET /admin/xp-rate-anomalies
 * List accounts with sustained XP rates above threshold
 * Query: admin_token, min_rate (default 30000 XP/hr), min_samples (default 3)
 */
app.get('/admin/xp-rate-anomalies', async (req, res) => {
    try {
        const { min_rate = 30000, min_samples = 3 } = req.query;
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlockXp = await enforceAdminScanCooldown(res, 'xp-rate-anomalies');
        if (scanBlockXp) return;

        const minRateNum = parseInt(min_rate) || 30000;
        const minSamplesNum = parseInt(min_samples) || 3;
        const anomalies = [];

        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = result[0] || result.cursor || "0";
            const keys = result[1] || result.keys || [];

            for (const key of keys) {
                try {
                    const data = await redis.get(key);
                    const user = typeof data === 'string' ? JSON.parse(data) : data;
                    const samples = user?.xp_rate?.hourly_samples;
                    if (!samples || samples.length < minSamplesNum) continue;

                    // Count samples above threshold
                    const highRateSamples = samples.filter(s => s.rate >= minRateNum);
                    if (highRateSamples.length >= minSamplesNum) {
                        const avgRate = Math.round(highRateSamples.reduce((sum, s) => sum + s.rate, 0) / highRateSamples.length);
                        anomalies.push({
                            unified_id: user.unified_id,
                            display_name: user.display_name,
                            level: user.level,
                            xp: user.xp,
                            high_rate_count: highRateSamples.length,
                            total_samples: samples.length,
                            avg_high_rate: avgRate,
                            recent_samples: samples.slice(-5)
                        });
                    }
                } catch (e) {
                    // Skip
                }
            }
        } while (cursor !== "0" && cursor !== 0);

        anomalies.sort((a, b) => b.avg_high_rate - a.avg_high_rate);

        res.json({
            total: anomalies.length,
            min_rate: minRateNum,
            min_samples: minSamplesNum,
            accounts: anomalies
        });
    } catch (error) {
        console.error('Admin xp-rate-anomalies error:', error.message);
        res.status(500).json({ error: 'Failed to query XP rate anomalies' });
    }
});

/**
 * GET /admin/export-all-users
 * Full backup: all user records, index keys, and leaderboard entries.
 * Query: ?admin_token=xxx
 */
app.get('/admin/export-all-users', async (req, res) => {
    try {
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        const scanBlockExport = await enforceAdminScanCooldown(res, 'export-all-users');
        if (scanBlockExport) return;

        // 1. Scan all user:* keys and collect full records
        const users = [];
        let cursor = "0";
        do {
            const result = await redis.scan(cursor, { match: 'user:*', count: 100 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                try {
                    const raw = await redis.get(key);
                    if (raw) {
                        const data = typeof raw === 'string' ? JSON.parse(raw) : raw;
                        users.push({ key, data: filterUserForAdmin(data) });
                    }
                } catch (e) { /* skip corrupt keys */ }
            }
        } while (cursor !== "0");

        // 2. Collect index keys (patreon_index, discord_index, display_name_index, plus V1 variants)
        // email_index intentionally excluded from export — contains email hashes (PII)
        const indexes = {};
        const indexPrefixes = ['patreon_index:', 'discord_index:', 'display_name_index:', 'patreon_user:', 'discord_user:'];
        for (const prefix of indexPrefixes) {
            const entries = {};
            let idxCursor = "0";
            do {
                const result = await redis.scan(idxCursor, { match: `${prefix}*`, count: 100 });
                idxCursor = String(result[0]);
                for (const key of (result[1] || [])) {
                    try {
                        const val = await redis.get(key);
                        if (val) entries[key] = val;
                    } catch (e) { /* skip */ }
                }
            } while (idxCursor !== "0");
            indexes[prefix.replace(':', '')] = entries;
        }

        // 3. Collect leaderboard sorted sets
        const leaderboards = {};
        let lbCursor = "0";
        do {
            const result = await redis.scan(lbCursor, { match: 'leaderboard:*', count: 100 });
            lbCursor = String(result[0]);
            for (const key of (result[1] || [])) {
                try {
                    const members = await redis.zrange(key, 0, -1, { withScores: true });
                    // zrange with withScores returns [member, score, member, score, ...]
                    const entries = [];
                    if (Array.isArray(members)) {
                        for (let i = 0; i < members.length; i += 2) {
                            entries.push({ member: members[i], score: parseFloat(members[i + 1]) || 0 });
                        }
                    }
                    leaderboards[key] = entries;
                } catch (e) { /* skip */ }
            }
        } while (lbCursor !== "0");

        res.json({
            users,
            indexes,
            leaderboards,
            exported_at: new Date().toISOString(),
            user_count: users.length
        });
    } catch (error) {
        console.error('Admin export-all-users error:', error.message);
        res.status(500).json({ error: 'Failed to export users' });
    }
});

/**
 * POST /admin/import-all-users
 * Restore from a full backup. Writes user records, indexes, and leaderboard entries.
 * Body: { admin_token: string, backup: <export JSON> }
 */
app.post('/admin/import-all-users', (req, res, next) => {
    // R8/M8: Verify admin auth BEFORE parsing 50MB body
    const admin_token = req.headers['x-admin-token'];
    if (!verifyAdminToken(admin_token)) {
        return res.status(403).json({ error: 'Unauthorized' });
    }
    next();
}, express.json({ limit: '50mb' }), async (req, res) => {
    try {
        const { backup } = req.body;
        if (!redis) return res.status(503).json({ error: 'Redis not available' });
        if (!backup || !backup.users) {
            return res.status(400).json({ error: 'Invalid backup format: missing users array' });
        }

        const ALLOWED_USER_PREFIX = 'user:';
        const ALLOWED_INDEX_PREFIXES = ['patreon_index:', 'discord_index:', 'display_name_index:'];
        const ALLOWED_LEADERBOARD_PREFIX = 'leaderboard:';

        const results = { users_written: 0, indexes_written: 0, leaderboard_entries: 0, skipped: 0, errors: [] };

        // 1. Write user records (only user:* keys)
        for (const { key, data } of backup.users) {
            if (!key.startsWith(ALLOWED_USER_PREFIX)) {
                results.skipped++;
                results.errors.push(`skipped non-user key: ${key}`);
                continue;
            }
            try {
                await redis.set(key, JSON.stringify(data));
                results.users_written++;
            } catch (e) {
                results.errors.push(`user ${key}: ${e.message}`);
            }
        }

        // 2. Write index keys (only allowed index prefixes)
        if (backup.indexes) {
            for (const [prefix, entries] of Object.entries(backup.indexes)) {
                for (const [key, value] of Object.entries(entries)) {
                    if (!ALLOWED_INDEX_PREFIXES.some(p => key.startsWith(p))) {
                        results.skipped++;
                        results.errors.push(`skipped non-index key: ${key}`);
                        continue;
                    }
                    try {
                        await redis.set(key, value);
                        results.indexes_written++;
                    } catch (e) {
                        results.errors.push(`index ${key}: ${e.message}`);
                    }
                }
            }
        }

        // 3. Write leaderboard entries (only leaderboard:* keys)
        if (backup.leaderboards) {
            for (const [key, entries] of Object.entries(backup.leaderboards)) {
                if (!key.startsWith(ALLOWED_LEADERBOARD_PREFIX)) {
                    results.skipped++;
                    results.errors.push(`skipped non-leaderboard key: ${key}`);
                    continue;
                }
                for (const { member, score } of entries) {
                    try {
                        await redis.zadd(key, { score, member });
                        results.leaderboard_entries++;
                    } catch (e) {
                        results.errors.push(`leaderboard ${key}/${member}: ${e.message}`);
                    }
                }
            }
        }

        res.json({
            success: true,
            ...results,
            imported_at: new Date().toISOString()
        });
    } catch (error) {
        console.error('Admin import-all-users error:', error.message);
        res.status(500).json({ error: 'Failed to import users' });
    }
});

// =============================================================================
// ADMIN: INVITE CODES
// =============================================================================

/**
 * POST /admin/invite-codes/generate
 * Generate single-use invite codes for account registration.
 * Body: { count?: number (1-20, default 1), expires_hours?: number (default 72) }
 * Header: X-Admin-Token
 */
app.post('/admin/invite-codes/generate', async (req, res) => {
    try {
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const count = Math.min(Math.max(parseInt(req.body.count) || 1, 1), 20);
        const expiresHours = Math.max(parseInt(req.body.expires_hours) || 72, 1);

        const codes = [];
        const now = new Date();
        const expiresAt = new Date(now.getTime() + expiresHours * 3600 * 1000);
        const ttlSeconds = expiresHours * 3600;

        for (let i = 0; i < count; i++) {
            const INVITE_CHARSET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
            const bytes = crypto.randomBytes(8);
            let code = '';
            for (let j = 0; j < 8; j++) code += INVITE_CHARSET[bytes[j] % INVITE_CHARSET.length];
            const codeData = {
                created_at: now.toISOString(),
                expires_at: expiresAt.toISOString(),
                used: false
            };

            await redis.set(`invite_code:${code}`, JSON.stringify(codeData), { ex: ttlSeconds });
            codes.push(code);
        }

        console.log(`[Admin] Generated ${count} invite code(s), expires in ${expiresHours}h`);

        res.json({
            success: true,
            codes,
            expires_at: expiresAt.toISOString(),
            expires_hours: expiresHours
        });
    } catch (error) {
        console.error('Admin invite-codes generate error:', error.message);
        res.status(500).json({ error: 'Failed to generate invite codes' });
    }
});

/**
 * GET /admin/invite-codes/list
 * List all active invite codes with their status.
 * Header: X-Admin-Token
 */
app.get('/admin/invite-codes/list', async (req, res) => {
    try {
        const admin_token = req.headers['x-admin-token'];
        if (!verifyAdminToken(admin_token)) {
            return res.status(403).json({ error: 'Unauthorized' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const codes = [];
        let cursor = '0';
        do {
            const result = await redis.scan(cursor, { match: 'invite_code:*', count: 100 });
            cursor = String(result[0]);
            for (const key of (result[1] || [])) {
                const data = await redis.get(key);
                if (data) {
                    const parsed = typeof data === 'string' ? JSON.parse(data) : data;
                    const code = key.replace('invite_code:', '');
                    const expired = parsed.expires_at && new Date(parsed.expires_at) < new Date();
                    codes.push({
                        code,
                        status: parsed.used ? 'used' : (expired ? 'expired' : 'active'),
                        created_at: parsed.created_at,
                        expires_at: parsed.expires_at,
                        used_by: parsed.used_by || null,
                        used_at: parsed.used_at || null
                    });
                }
            }
        } while (cursor !== '0');

        res.json({ success: true, count: codes.length, codes });
    } catch (error) {
        console.error('Admin invite-codes list error:', error.message);
        res.status(500).json({ error: 'Failed to list invite codes' });
    }
});

// =============================================================================
// REMOTE CONTROL ENDPOINTS
// =============================================================================

const REMOTE_CODE_CHARSET = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789'; // No 0/O/1/I/L
const REMOTE_SESSION_TTL = 4 * 60 * 60; // 4 hours in seconds
const REMOTE_CONTROLLER_STALE_SECONDS = 30;

const REMOTE_TIER_ACTIONS = {
    light: [
        'trigger_flash', 'trigger_subliminal',
        'start_flash', 'stop_flash',
        'start_subliminal', 'stop_subliminal',
        'trigger_custom_subliminal',
        'show_pink_filter', 'stop_pink_filter',
        'show_spiral', 'stop_spiral',
        'set_pink_opacity', 'set_spiral_opacity',
        'start_bubbles', 'stop_bubbles',
        'trigger_panic'
    ],
    standard: [
        'trigger_flash', 'trigger_subliminal',
        'start_flash', 'stop_flash',
        'start_subliminal', 'stop_subliminal',
        'trigger_custom_subliminal',
        'show_pink_filter', 'stop_pink_filter',
        'show_spiral', 'stop_spiral',
        'set_pink_opacity', 'set_spiral_opacity',
        'start_bubbles', 'stop_bubbles',
        'trigger_panic',
        'trigger_video', 'start_video', 'stop_video',
        'trigger_haptic',
        'duck_audio', 'unduck_audio',
        'trigger_lock_card', 'start_lock_card', 'stop_lock_card'
    ],
    full: [
        'trigger_flash', 'trigger_subliminal',
        'start_flash', 'stop_flash',
        'start_subliminal', 'stop_subliminal',
        'trigger_custom_subliminal',
        'show_pink_filter', 'stop_pink_filter',
        'show_spiral', 'stop_spiral',
        'set_pink_opacity', 'set_spiral_opacity',
        'start_bubbles', 'stop_bubbles',
        'trigger_panic',
        'trigger_video', 'start_video', 'stop_video',
        'trigger_haptic',
        'duck_audio', 'unduck_audio',
        'trigger_lock_card', 'start_lock_card', 'stop_lock_card',
        'start_autonomy', 'stop_autonomy',
        'trigger_bubble_count',
        'trigger_mind_wipe', 'start_mind_wipe', 'stop_mind_wipe',
        'start_bounce_text', 'stop_bounce_text',
        'enable_strict_lock', 'disable_strict_lock',
        'disable_panic', 'enable_panic',
        'start_session', 'stop_session', 'pause_session', 'resume_session'
    ]
};

function generateRemoteCode() {
    const bytes = crypto.randomBytes(8);
    let code = '';
    for (let i = 0; i < 8; i++) {
        code += REMOTE_CODE_CHARSET[bytes[i] % REMOTE_CODE_CHARSET.length];
    }
    return code;
}

/**
 * POST /v2/remote/start
 * Sub starts a remote control session. Generates a 6-char code.
 * Body: { unified_id: string, tier: "light"|"standard"|"full" }
 */
app.post('/v2/remote/start', async (req, res) => {
    try {
        const { unified_id, tier, connect_pin } = req.body;

        if (!unified_id) return res.status(400).json({ error: 'unified_id required' });
        if (!tier || !REMOTE_TIER_ACTIONS[tier]) {
            return res.status(400).json({ error: 'Invalid tier. Must be light, standard, or full' });
        }
        // Require a 4-digit PIN for controller auth
        if (!connect_pin || typeof connect_pin !== 'string' || !/^\d{4}$/.test(connect_pin)) {
            return res.status(400).json({ error: 'A 4-digit connect_pin is required' });
        }
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Verify user exists and validate auth
        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) return res.status(404).json({ error: 'User not found' });

        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;

        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }

        // Check for existing session and clean it up
        const existingCode = await redis.get(`remote:lookup:${unified_id}`);
        if (existingCode) {
            await redis.del(`remote:${existingCode}`);
            await redis.del(`remote:commands:${existingCode}`);
            await redis.del(`remote:status:${existingCode}`);
            await redis.del(`remote:lookup:${unified_id}`);
        }

        // Generate unique code (retry if collision)
        let code;
        for (let attempt = 0; attempt < 10; attempt++) {
            code = generateRemoteCode();
            const existing = await redis.get(`remote:${code}`);
            if (!existing) break;
            if (attempt === 9) return res.status(500).json({ error: 'Failed to generate unique code' });
        }

        const now = new Date().toISOString();
        const expiresAt = new Date(Date.now() + REMOTE_SESSION_TTL * 1000).toISOString();

        // Privacy: never expose display_name if it matches the real patron_name
        let safeName = user.display_name || null;
        if (safeName && user.patron_name && !user.display_name_set_at &&
            safeName.toLowerCase().trim() === user.patron_name.toLowerCase().trim()) {
            safeName = null;
        }

        // Hash PIN for storage (SHA-256, timing-safe comparison on connect)
        const pinHash = crypto.createHash('sha256').update(connect_pin).digest('hex');

        const session = {
            unified_id,
            display_name: safeName || 'Anonymous',
            tier,
            connect_pin_hash: pinHash,
            created_at: now,
            controller_connected: false,
            controller_id: null,
            last_controller_ping: null
        };

        // Store session data + lookup + initial status, all with TTL
        await redis.set(`remote:${code}`, JSON.stringify(session), { ex: REMOTE_SESSION_TTL });
        await redis.set(`remote:lookup:${unified_id}`, code, { ex: REMOTE_SESSION_TTL });
        await redis.set(`remote:status:${code}`, JSON.stringify({
            online: true,
            last_poll: now,
            last_executed: null,
            active_services: [],
            level: user.level || 1
        }), { ex: REMOTE_SESSION_TTL });

        console.log(`[Remote] Session started: ${code.slice(0,3)}*** by uid:${unified_id}, tier: ${tier}`);

        res.json({ code, expires_at: expiresAt });
    } catch (error) {
        console.error('Remote start error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/remote/stop
 * Sub ends the remote control session.
 * Body: { unified_id: string }
 */
app.post('/v2/remote/stop', async (req, res) => {
    try {
        const { unified_id } = req.body;
        if (!unified_id) return res.status(400).json({ error: 'unified_id required' });
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Validate auth token
        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) return res.status(404).json({ error: 'User not found' });
        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }

        const code = await redis.get(`remote:lookup:${unified_id}`);
        if (!code) return res.status(404).json({ error: 'No active session' });

        await redis.del(`remote:${code}`);
        await redis.del(`remote:commands:${code}`);
        await redis.del(`remote:status:${code}`);
        await redis.del(`remote:lookup:${unified_id}`);

        console.log(`[Remote] Session stopped: ${code.slice(0,3)}*** by uid:${unified_id}`);

        res.json({ ok: true });
    } catch (error) {
        console.error('Remote stop error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/remote/poll
 * Sub polls for pending commands from the controller.
 * Body: { unified_id: string }
 */
app.post('/v2/remote/poll', async (req, res) => {
    try {
        const { unified_id } = req.body;
        if (!unified_id) return res.status(400).json({ error: 'unified_id required' });
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Validate auth token
        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) return res.status(404).json({ error: 'User not found' });
        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }

        const code = await redis.get(`remote:lookup:${unified_id}`);
        if (!code) return res.status(404).json({ error: 'No active session' });

        // Pop up to 5 commands from the queue (RPOP from right end of list)
        const commands = [];
        for (let i = 0; i < 5; i++) {
            const cmd = await redis.rpop(`remote:commands:${code}`);
            if (!cmd) break;
            commands.push(typeof cmd === 'string' ? JSON.parse(cmd) : cmd);
        }

        // Update status with last_poll timestamp
        const statusRaw = await redis.get(`remote:status:${code}`);
        const status = statusRaw ? (typeof statusRaw === 'string' ? JSON.parse(statusRaw) : statusRaw) : {};
        status.online = true;
        status.last_poll = new Date().toISOString();
        await redis.set(`remote:status:${code}`, JSON.stringify(status), { ex: REMOTE_SESSION_TTL });

        // Refresh lookup key TTL so sessions don't expire while actively polling
        await redis.expire(`remote:lookup:${unified_id}`, REMOTE_SESSION_TTL);

        // Get session to check controller status
        const sessionRaw = await redis.get(`remote:${code}`);
        const session = sessionRaw ? (typeof sessionRaw === 'string' ? JSON.parse(sessionRaw) : sessionRaw) : {};

        // controller_connected stays true until the controller explicitly disconnects
        // via POST /remote/disconnect.  We no longer flip it to false based on ping
        // staleness because sessions can last hours and controllers may be idle for
        // 10-20+ minutes (mobile browsers also throttle background-tab timers heavily).
        // Instead, we report a separate controller_idle flag so the client can show
        // a subtle "controller may be idle" indicator without killing effects.
        const controllerConnected = session.controller_connected || false;
        let controllerIdle = false;
        if (controllerConnected && session.last_controller_ping) {
            const pingAge = (Date.now() - new Date(session.last_controller_ping).getTime()) / 1000;
            controllerIdle = pingAge > REMOTE_CONTROLLER_STALE_SECONDS;
        }

        res.json({ commands, controller_connected: controllerConnected, controller_idle: controllerIdle });
    } catch (error) {
        console.error('Remote poll error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /v2/remote/status
 * Sub pushes current app status for the controller to see.
 * Body: { unified_id: string, active_services: string[], level: number, last_executed: object|null }
 */
app.post('/v2/remote/status', async (req, res) => {
    try {
        const { unified_id, active_services, level, last_executed, available_sessions, session_info } = req.body;
        if (!unified_id) return res.status(400).json({ error: 'unified_id required' });
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        // Validate auth token
        const userData = await redis.get(`user:${unified_id}`);
        if (!userData) return res.status(404).json({ error: 'User not found' });
        const user = typeof userData === 'string' ? JSON.parse(userData) : userData;
        const authCheck = validateAuthToken(req, user);
        if (!authCheck.valid) {
            return res.status(401).json({ error: 'Invalid or missing auth token' });
        }

        const code = await redis.get(`remote:lookup:${unified_id}`);
        if (!code) return res.status(404).json({ error: 'No active session' });

        const status = {
            online: true,
            last_poll: new Date().toISOString(),
            last_executed: last_executed || null,
            active_services: active_services || [],
            level: level || 1,
            available_sessions: available_sessions || null,
            session_info: session_info || null
        };

        await redis.set(`remote:status:${code}`, JSON.stringify(status), { ex: REMOTE_SESSION_TTL });

        res.json({ ok: true });
    } catch (error) {
        console.error('Remote status error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

const REMOTE_CONNECT_MAX_ATTEMPTS = 5; // per minute per IP

/**
 * POST /remote/connect
 * Controller connects to a Sub's session using the 6-char code.
 * Body: { code: string }
 */
app.post('/remote/connect', async (req, res) => {
    try {
        // Per-IP rate limit to prevent brute-force code guessing (Redis-backed, shared across instances)
        const ip = req.ip || 'unknown';
        if (!redis) return res.status(503).json({ error: 'Service temporarily unavailable' });
        try {
            const connectRateKey = `remote_connect_rate:${ip}`;
            await redis.set(connectRateKey, 0, { nx: true, ex: 60 });
            const connectCount = await redis.incr(connectRateKey);
            if (connectCount > REMOTE_CONNECT_MAX_ATTEMPTS) {
                console.log(`[SECURITY] Remote connect rate limit hit: ${ip} (${connectCount} attempts/min)`);
                return res.status(429).json({ error: 'Too many connection attempts. Try again in a minute.' });
            }
        } catch (e) {
            // Redis failure on unauthenticated brute-force-sensitive endpoint = fail closed
            return res.status(503).json({ error: 'Service temporarily unavailable' });
        }

        const { code, pin } = req.body;
        if (!code || code.length < 6 || code.length > 8) return res.status(400).json({ error: 'Valid session code required' });
        // Validate character set to prevent Redis key injection
        if (!/^[A-Za-z0-9]{6,8}$/.test(code)) return res.status(400).json({ error: 'Invalid code format' });
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const normalizedCode = code.toUpperCase();
        const sessionRaw = await redis.get(`remote:${normalizedCode}`);
        // Use same error for not-found and wrong PIN to prevent session enumeration
        if (!sessionRaw) return res.status(404).json({ error: 'Invalid code or PIN' });

        // Validate connect PIN (required for sessions that have one)
        const sessionPeek = typeof sessionRaw === 'string' ? JSON.parse(sessionRaw) : sessionRaw;
        if (sessionPeek.connect_pin_hash) {
            if (!pin || typeof pin !== 'string') {
                return res.status(404).json({ error: 'Invalid code or PIN' });
            }
            const providedHash = crypto.createHash('sha256').update(pin).digest('hex');
            // Timing-safe comparison to prevent timing oracle on PIN
            const expected = Buffer.from(sessionPeek.connect_pin_hash, 'hex');
            const provided = Buffer.from(providedHash, 'hex');
            if (expected.length !== provided.length || !crypto.timingSafeEqual(expected, provided)) {
                return res.status(404).json({ error: 'Invalid code or PIN' });
            }
        }

        const session = sessionPeek;

        // If another controller is connected, replace it (they may have closed the page)
        if (session.controller_connected && session.controller_id) {
            console.log(`[Remote] Replacing previous controller on ${normalizedCode.slice(0,3)}***`);
        }

        const controllerId = crypto.randomUUID();
        const connectedAt = new Date().toISOString();

        session.controller_connected = true;
        session.controller_id = controllerId;
        session.last_controller_ping = connectedAt;

        await redis.set(`remote:${normalizedCode}`, JSON.stringify(session), { ex: REMOTE_SESSION_TTL });

        // Get status for initial data
        const statusRaw = await redis.get(`remote:status:${normalizedCode}`);
        const status = statusRaw ? (typeof statusRaw === 'string' ? JSON.parse(statusRaw) : statusRaw) : {};

        console.log(`[Remote] Controller connected to ${normalizedCode.slice(0,3)}***`);

        res.json({
            controller_id: controllerId,
            tier: session.tier,
            display_name: session.display_name,
            level: status.level || 1,
            available_sessions: status.available_sessions || null
        });
    } catch (error) {
        console.error('Remote connect error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /remote/command
 * Controller sends a command to the Sub's app.
 * Body: { code: string, controller_id: string, action: string, params: object }
 */
app.post('/remote/command', async (req, res) => {
    try {
        // Per-IP rate limit: 30 commands per minute (fail-closed on Redis error)
        const cmdIp = req.ip || 'unknown';
        if (!redis) return res.status(503).json({ error: 'Service temporarily unavailable' });
        try {
            const cmdRateKey = `remote_cmd_rate:${cmdIp}`;
            await redis.set(cmdRateKey, 0, { nx: true, ex: 60 });
            const cmdCount = await redis.incr(cmdRateKey);
            if (cmdCount > 30) {
                return res.status(429).json({ error: 'Too many commands. Please slow down.' });
            }
        } catch (e) { return res.status(503).json({ error: 'Service temporarily unavailable' }); }

        const { code, controller_id, action, params } = req.body;
        if (!code || !controller_id || !action) {
            return res.status(400).json({ error: 'code, controller_id, and action required' });
        }
        // R12/M1: Validate code format to prevent Redis key injection (matches /remote/connect)
        if (!/^[A-Za-z0-9]{6,8}$/.test(code)) return res.status(400).json({ error: 'Invalid code format' });
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const normalizedCode = code.toUpperCase();
        const sessionRaw = await redis.get(`remote:${normalizedCode}`);
        if (!sessionRaw) return res.status(404).json({ error: 'Session not found or expired' });

        const session = typeof sessionRaw === 'string' ? JSON.parse(sessionRaw) : sessionRaw;

        // Validate controller identity
        if (session.controller_id !== controller_id) {
            return res.status(403).json({ error: 'Invalid controller_id' });
        }

        // Validate action against tier
        const allowedActions = REMOTE_TIER_ACTIONS[session.tier] || [];
        if (!allowedActions.includes(action)) {
            return res.status(403).json({ error: `Action '${action}' not permitted for tier '${session.tier}'` });
        }

        // Rate limit: max 1 command per second per session
        const rateLimitKey = `remote:ratelimit:${normalizedCode}`;
        const lastCmd = await redis.get(rateLimitKey);
        if (lastCmd) {
            return res.status(429).json({ error: 'Rate limit: max 1 command per second' });
        }
        await redis.set(rateLimitKey, '1', { ex: 1 });

        // Update controller ping and re-mark as connected if resumed from stale
        session.last_controller_ping = new Date().toISOString();
        if (!session.controller_connected) {
            session.controller_connected = true;
        }
        await redis.set(`remote:${normalizedCode}`, JSON.stringify(session), { ex: REMOTE_SESSION_TTL });

        // Validate params: must be a plain object, max 4KB serialized, no proto pollution
        const BANNED_KEYS = ['__proto__', 'constructor', 'prototype'];
        const rawParams = (params && typeof params === 'object' && !Array.isArray(params)) ? params : {};
        const sanitizedParams = {};
        for (const [k, v] of Object.entries(rawParams)) {
            if (!BANNED_KEYS.includes(k)) sanitizedParams[k] = v;
        }
        const paramsJson = JSON.stringify(sanitizedParams);
        if (paramsJson.length > 4096) {
            return res.status(400).json({ error: 'params too large (max 4KB)' });
        }

        // Build command and push to queue
        const command = {
            id: crypto.randomUUID(),
            action,
            params: sanitizedParams,
            sent_at: new Date().toISOString()
        };

        await redis.lpush(`remote:commands:${normalizedCode}`, JSON.stringify(command));
        // Ensure the command list has a TTL
        await redis.expire(`remote:commands:${normalizedCode}`, REMOTE_SESSION_TTL);

        res.json({ ok: true, command_id: command.id });
    } catch (error) {
        console.error('Remote command error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * GET /remote/status/:code
 * Controller polls the Sub's current status.
 * Query: ?controller_id=uuid
 */
app.get('/remote/status/:code', async (req, res) => {
    try {
        // Per-IP rate limit: 20 status polls per minute (fail-closed on Redis error)
        const statusIp = req.ip || 'unknown';
        if (!redis) return res.status(503).json({ error: 'Service temporarily unavailable' });
        try {
            const statusRateKey = `remote_status_rate:${statusIp}`;
            await redis.set(statusRateKey, 0, { nx: true, ex: 60 });
            const statusCount = await redis.incr(statusRateKey);
            if (statusCount > 20) {
                return res.status(429).json({ error: 'Too many status requests. Please slow down.' });
            }
        } catch (e) { return res.status(503).json({ error: 'Service temporarily unavailable' }); }

        const { code } = req.params;
        const { controller_id } = req.query;
        if (!code || !controller_id) {
            return res.status(400).json({ error: 'code and controller_id required' });
        }
        // R12/M3: Validate code format to prevent Redis key injection (matches /remote/connect)
        if (!/^[A-Za-z0-9]{6,8}$/.test(code)) return res.status(400).json({ error: 'Invalid code format' });
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const normalizedCode = code.toUpperCase();
        const sessionRaw = await redis.get(`remote:${normalizedCode}`);
        if (!sessionRaw) return res.status(404).json({ error: 'Session not found or expired' });

        const session = typeof sessionRaw === 'string' ? JSON.parse(sessionRaw) : sessionRaw;

        if (session.controller_id !== controller_id) {
            return res.status(403).json({ error: 'Invalid controller_id' });
        }

        // Update controller ping and re-mark as connected if they resumed after
        // being stale (e.g. browser came back from background/sleep)
        session.last_controller_ping = new Date().toISOString();
        if (!session.controller_connected) {
            session.controller_connected = true;
            console.log(`[Remote] Controller resumed on ${normalizedCode.slice(0,3)}***`);
        }
        await redis.set(`remote:${normalizedCode}`, JSON.stringify(session), { ex: REMOTE_SESSION_TTL });

        // Get sub status
        const statusRaw = await redis.get(`remote:status:${normalizedCode}`);
        const status = statusRaw ? (typeof statusRaw === 'string' ? JSON.parse(statusRaw) : statusRaw) : {};

        // Check if sub is still online (last poll within 15 seconds)
        let online = false;
        if (status.last_poll) {
            const pollAge = (Date.now() - new Date(status.last_poll).getTime()) / 1000;
            online = pollAge < 15;
        }

        res.json({
            online,
            last_poll: status.last_poll || null,
            last_executed: status.last_executed || null,
            active_services: status.active_services || [],
            level: status.level || 1,
            available_sessions: status.available_sessions || null,
            session_info: status.session_info || null
        });
    } catch (error) {
        console.error('Remote status error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * POST /remote/disconnect
 * Controller disconnects from the session.
 * Body: { code: string, controller_id: string }
 */
app.post('/remote/disconnect', async (req, res) => {
    try {
        // Per-IP rate limit: 10 disconnects per minute (fail-closed on Redis error)
        const dcIp = req.ip || 'unknown';
        if (!redis) return res.status(503).json({ error: 'Service temporarily unavailable' });
        try {
            const dcRateKey = `remote_dc_rate:${dcIp}`;
            await redis.set(dcRateKey, 0, { nx: true, ex: 60 });
            const dcCount = await redis.incr(dcRateKey);
            if (dcCount > 10) {
                return res.status(429).json({ error: 'Too many requests. Please slow down.' });
            }
        } catch (e) { return res.status(503).json({ error: 'Service temporarily unavailable' }); }

        const { code, controller_id } = req.body;
        if (!code || !controller_id) {
            return res.status(400).json({ error: 'code and controller_id required' });
        }
        // R12/M2: Validate code format to prevent Redis key injection (matches /remote/connect)
        if (!/^[A-Za-z0-9]{6,8}$/.test(code)) return res.status(400).json({ error: 'Invalid code format' });
        if (!redis) return res.status(503).json({ error: 'Redis not available' });

        const normalizedCode = code.toUpperCase();
        const sessionRaw = await redis.get(`remote:${normalizedCode}`);
        if (!sessionRaw) return res.status(404).json({ error: 'Session not found or expired' });

        const session = typeof sessionRaw === 'string' ? JSON.parse(sessionRaw) : sessionRaw;

        if (session.controller_id !== controller_id) {
            return res.status(403).json({ error: 'Invalid controller_id' });
        }

        session.controller_connected = false;
        session.controller_id = null;
        session.last_controller_ping = null;

        await redis.set(`remote:${normalizedCode}`, JSON.stringify(session), { ex: REMOTE_SESSION_TTL });

        console.log(`[Remote] Controller disconnected from ${normalizedCode.slice(0,3)}***`);

        res.json({ ok: true });
    } catch (error) {
        console.error('Remote disconnect error:', error.message);
        res.status(500).json({ error: 'Internal server error' });
    }
});

/**
 * Health check endpoint
 */
app.get('/health', (req, res) => {
    res.json({
        status: 'ok',
        timestamp: new Date().toISOString()
    });
});

// =============================================================================
// START SERVER (for local development) / EXPORT (for Vercel)
// =============================================================================

// Export for Vercel serverless
module.exports = app;

// Only start server if running directly (not imported by Vercel)
if (require.main === module) {
    app.listen(CONFIG.PORT, () => {
        console.log(`Patreon proxy server running on port ${CONFIG.PORT}`);
        console.log(`Health check: http://localhost:${CONFIG.PORT}/health`);
        console.log(`AI Model: ${CONFIG.AI_MODEL}`);

        // Warn about missing config
        if (!CONFIG.PATREON_CLIENT_ID) {
            console.warn('WARNING: PATREON_CLIENT_ID not set');
        }
        if (!CONFIG.PATREON_CLIENT_SECRET) {
            console.warn('WARNING: PATREON_CLIENT_SECRET not set');
        }
        if (!CONFIG.OPENROUTER_API_KEY) {
            console.warn('WARNING: OPENROUTER_API_KEY not set');
        }
    });
}