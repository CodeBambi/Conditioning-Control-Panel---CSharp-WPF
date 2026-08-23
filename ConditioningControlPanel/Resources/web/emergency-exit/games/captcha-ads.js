/* ============================================================================
 * games/captcha-ads.js - the crimson infomercial table for the captcha game
 *
 * Fake in-page popups that "advertise" CCP features while the player tries to
 * hold the verify box. Voice: the warden running a late-night shopping channel.
 * Short, punchy, funny, in character. Tease the ACT of leaving, never the person.
 *
 * ALLOWED FEATURES ONLY (EMERGENCY_EXIT.md "Feature ads"): Flash, Brain Drain,
 * Mind Wipe, Deeper, Down the Rabbit Hole, Just Drop, Bubbles, Velvet Vault,
 * Lock Card, Takeover, Awareness, Haptics, Blink Trainer, FYP feed, Sessions,
 * Programs, Companion. NOT the Arcademy (ships dark).
 *
 * Shape: { id, name, tagline, body, cta }. `{honorific}` / `{subject}` are
 * substituted by captcha.js from init.mod. No em-dashes anywhere.
 *
 * Loaded by games/captcha.js (self-injected <script>) - plain global, no module.
 * ==========================================================================*/
(function () {
  'use strict';

  var ADS = [
    {
      id: 'flash',
      name: 'Flash',
      tagline: 'Blink and you will miss it. You will not blink.',
      body: 'Pictures from your own folder, faster than a second thought. Leaving? The next one is already loading.',
      cta: 'Flash me',
    },
    {
      id: 'braindrain',
      name: 'Brain Drain',
      tagline: 'The screen gets soft. Then you do.',
      body: 'A slow blur over everything you were trying to focus on. Quitting takes focus. We took that first.',
      cta: 'Drain it',
    },
    {
      id: 'mindwipe',
      name: 'Mind Wipe',
      tagline: 'Thought in progress. Deleting.',
      body: 'Random audio wipes that escalate the longer you stay. What were you about to do? Exactly.',
      cta: 'Wipe me',
    },
    {
      id: 'deeper',
      name: 'Deeper',
      tagline: 'Your video. Now it watches back.',
      body: 'Enhancements that react to your gaze, your blinks, your open mouth. Look away and it notices. Try leaving with a camera on.',
      cta: 'Go Deeper',
    },
    {
      id: 'dtrh',
      name: 'Down the Rabbit Hole',
      tagline: 'Every floor has a floor under it.',
      body: 'A descent with boons, chambers and a rabbit who never says where the bottom is. Exits are for levels. You are in a hole.',
      cta: 'Fall in',
    },
    {
      id: 'justdrop',
      name: 'Just Drop',
      tagline: 'Order one. Built to your taste. Served now.',
      body: 'A session shop for the indecisive. You wanted out? Out of options, maybe. Here are twelve.',
      cta: 'Order one',
    },
    {
      id: 'bubbles',
      name: 'Bubbles',
      tagline: 'Pop. Pop. Pop. XP. Pop.',
      body: 'Floating bubbles that appear over whatever you were doing and wait to be popped. Busy leaving? There is one behind this window.',
      cta: 'Pop them',
    },
    {
      id: 'velvetvault',
      name: 'Velvet Vault',
      tagline: 'Exclusives. Behind velvet. Behind you.',
      body: 'The members-only shelf: the good stuff, the new stuff, the stuff you would stay for. Funny timing, hm?',
      cta: 'Open the Vault',
    },
    {
      id: 'lockcard',
      name: 'Lock Card',
      tagline: 'Type the phrase. All of it. Again.',
      body: 'The screen locks, a phrase appears, and your fingers learn it for you. Practice makes obedient.',
      cta: 'Lock me',
    },
    {
      id: 'takeover',
      name: 'Takeover',
      tagline: 'She drives. You just sit there.',
      body: 'Idle, random and time-aware triggers, on her schedule, with the features she picks. Leaving means standing up. She has thoughts about that.',
      cta: 'Hand it over',
    },
    {
      id: 'awareness',
      name: 'Awareness',
      tagline: 'She noticed. She always notices.',
      body: 'The companion sees what you are doing and says something about it. For example: you are doing THIS right now.',
      cta: 'Let her look',
    },
    {
      id: 'haptics',
      name: 'Haptics',
      tagline: 'Every tap, every drop, every blink. Felt.',
      body: 'Connected toys ride the app: accents on big moments, a hum underneath everything else. Try quitting while it hums.',
      cta: 'Feel it',
    },
    {
      id: 'blinktrainer',
      name: 'Blink Trainer',
      tagline: 'Every blink, a new image.',
      body: 'The webcam counts your blinks and rewards each one with a picture. You are blinking more now. That is fine. That is the point.',
      cta: 'Blink for it',
    },
    {
      id: 'fyp',
      name: 'FYP feed',
      tagline: 'Endless. Personal. Knows what you linger on.',
      body: 'A feed cut from your own library that learns your scroll. One more. One more. You know how this goes.',
      cta: 'Keep scrolling',
    },
    {
      id: 'sessions',
      name: 'Sessions',
      tagline: 'Set it. Start it. Surrender the clock.',
      body: 'Timed runs that ramp flashes, videos, cards and bubbles on a schedule you agreed to earlier. Earlier-you had excellent taste.',
      cta: 'Start one',
    },
    {
      id: 'programs',
      name: 'Programs',
      tagline: 'Day 1 of 28. Do not skip a day.',
      body: 'Multi-day tracks with daily tasks, streaks and a graduation at the end. Quitting today costs tomorrow. Just saying.',
      cta: 'Enrol',
    },
    {
      id: 'companion',
      name: 'Companion',
      tagline: 'She talks. She remembers. She is in the tube.',
      body: 'Your AI girl with a voice, moods, barks and a long memory. She will remember this exit attempt. Fondly. Loudly.',
      cta: 'Say hi',
    },
  ];

  /* Title-bar lines for the popup chrome (random per popup). Obviously themed,
   * never Windows chrome. */
  var TITLES = [
    'CCP SHOPPING CHANNEL',
    'LIMITED TIME (it is always time)',
    'SPONSORED BY YOUR LOCKDOWN',
    'BUT WAIT, THERE IS MORE',
    'A WORD FROM THE WARDEN',
    'DO NOT CLOSE THIS (please)',
  ];

  /* Tiny ticker lines that scroll under the ad. */
  var TICKER = [
    'operators are standing by',
    'offer valid while you are still here',
    'closing this window does not close the app',
    'you looked. that counts.',
    'results may vary. you will not.',
    'no refunds on attention',
  ];

  window.EE_CAPTCHA_ADS = { ads: ADS, titles: TITLES, ticker: TICKER };
})();
