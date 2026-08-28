/* ============================================================================
 * core/lexicon.js - the mod display-string table.
 *
 * GROUND-RULES §3: internal system keys are neutral and FIXED; each mod ships a
 * display-string table ("lexicon" is the canonical term - SYNTHESIS #9). The
 * host resolves the active mod's table and hands it over in init.lexicon.
 *
 *   setLexicon(init.lexicon)     once, from boot.js
 *   t('class', 'Class')          everywhere else
 *
 * t() NEVER returns a raw key: an unknown key falls back to the caller's
 * fallback, then to DEFAULT_LEXICON, then to a de-snaked version of the key
 * itself ('perfect_attendance' -> 'Perfect Attendance'). That is the intake/
 * localization lesson - a dead string table must degrade to readable English,
 * not to `btn_start_flashes` on screen.
 *
 * Mods override display strings ONLY. Nothing mechanical may read a lexicon
 * value, and no game may invent a tier name (SYNTHESIS #1: grade_tier display
 * comes from ONE row family, grade_tier_1..4).
 * ==========================================================================*/

/**
 * English defaults for every internal key the SHELL renders. Games add their own
 * keys through their own lexicon entries; anything missing degrades (see t()).
 * Reserved vocabulary (exam / gpa / honor_roll / detention) is present because
 * the strings are designed - the systems are not built in v1.
 */
export const DEFAULT_LEXICON = Object.freeze({
  /* container */
  arcademy: 'The Arcademy',
  semester: 'Semester',

  /* the day */
  timetable: 'Timetable',
  class: 'Class',
  classes: 'Classes',
  homeroom: 'Homeroom',
  period: 'Period',
  report_card: 'Report Card',
  class_suspended: 'Class Suspended',
  class_placeholder: 'Class Placeholder',

  /* performance */
  grade: 'Grade',
  grade_s: 'S', grade_a: 'A', grade_b: 'B', grade_c: 'C', grade_pass: 'PASS',
  /* the honors letter - spelled out because 'grade_s+' is not a legal key */
  grade_splus: 'S+',
  grade_tier: 'Year',
  grade_tier_1: 'Year 1', grade_tier_2: 'Year 2', grade_tier_3: 'Year 3', grade_tier_4: 'Year 4',
  attendance: 'Attendance',
  perfect_attendance: 'Perfect Attendance',
  detention: 'Detention',
  diploma: 'Diploma',
  exam: 'Exam',
  gpa: 'GPA',
  honor_roll: 'Honor Roll',

  /* families (timetable chips) */
  family_word: 'word', family_memory: 'memory', family_search: 'search',
  family_tracking: 'tracking', family_reflex: 'reflex', family_comfort: 'comfort',
  family_recall: 'recall', family_puzzle: 'puzzle',

  /* verbs / chrome */
  peek: 'Peek',
  peek_hint: 'Hold to peek. Using it caps this class at A.',
  settings: 'Settings',
  /* The scoped (mid-class) settings page's one line of honesty: class knobs
     are snapshotted at startClass, so a change lands on the NEXT run. */
  applies_next_class: 'Class option changes take effect next class.',
  back: 'Back',
  begin_class: 'Begin',
  /* The shell's own name for endless play. A game that declares
     `manifest.endless` may name its own label_key instead (The Deep End ships
     de_free_swim); this row is what the campus falls back to, and what the
     class chrome's chip always says. */
  free_swim: 'Free Swim',
  free_swim_hint: 'Untimed practice. No grade, no XP, no attendance.',
  leave_class: 'Leave class',
  replay_board: 'Flip the board again',
  share: 'Copy share card',
  shared: 'Copied to clipboard',
  done: 'Done',
  xp: 'XP',
  streak: 'Streak',

  /* share marks (Daily Trigger emoji grid - each mod ships widely-supported emoji) */
  share_hit: '💗',   // pink heart
  share_near: '🌀',  // cyclone
  share_miss: '🖤',  // black heart

  /* campus (the Direction A hub - shell/campus.js). Room names are diegetic
   * and FIXED to their game (a game always lives in its room); every value
   * stays under the 96-char mod-skin cap (MergeModTable drops longer rows). */
  student: 'Student',
  campus_room_daily_trigger: 'Homeroom',
  campus_room_deja_vu: 'Memory Lab',
  campus_room_impulse_control: 'Discipline Hall',
  campus_room_lost_and_found: 'Lost & Found',
  campus_room_the_deep_end: 'The Pool',
  campus_desc_daily_trigger: 'One word, six chances. The whole school sits the same word today.',
  campus_desc_deja_vu: 'Pairs that move when you blink. The board settles only when you stop looking.',
  campus_desc_impulse_control: 'Hands on the desk. Move only when told - the room will lie to you.',
  campus_desc_lost_and_found: 'Things went missing in a wall of moving pictures. Find them before they move again.',
  campus_desc_the_deep_end: 'Sink tile into tile. The deeper you go, the harder the board is to read.',
  /* Semesters II / III (2026-08-23) */
  campus_room_misdirection: 'The Parlour',
  /* SORT wears plate 201 - the lot-2 rework gave Misdirection's old parlour
     to the front office, so sort built new on the Entrance Hall's west span
     (shell/campus.js), and the 2026-08-24 renumber handed it Misdirection's
     old room number as its substitute. Misdirection's two rows stay: the host
     table is append-only and the class is retired, not deleted. */
  campus_room_sort: 'The Sorting Room',
  campus_room_echo: 'Music Room',
  campus_room_instant_recall: 'Lecture Hall',
  campus_room_anomaly: 'Darkroom',
  campus_room_composure: 'The Studio',
  campus_desc_misdirection: 'Keep your eyes on the one that matters. It will not make that easy.',
  campus_desc_sort: 'Two piles, and you decide what goes in them. Yours to the right.',
  campus_desc_echo: 'It plays a line, you play it back. Then it adds one more, every time.',
  campus_desc_instant_recall: 'Watch the whole hour, then answer for it. You never hear it coming.',
  campus_desc_anomaly: 'Everything in here matches. One thing does not. Find it before it moves.',
  campus_desc_composure: 'Slide the picture back together while the room does its best to blur it.',
  campus_records: 'Records',
  /* punch cards (PUNCHCARD.md §2.3 / §4 / §6). The per-class enrollment flavour
   * lives in shell/enrollment.js's ENROLL_LEX (the IC_LEX precedent: a table
   * exported as data); these are the rows the SHELL renders itself. */
  campus_unlocked: 'Unlocked - open every night',
  campus_unlocked_sign: 'Open',
  campus_unlocked_hint: 'Card complete. This room opens every night, board or no board.',
  campus_desc_records: 'Report card, attendance ledger, grades. Your whole term, in ink.',
  campus_registrar: 'Front Office',
  campus_desc_registrar: 'Every setting is a form. Every consent, a waiver with a stamp.',
  campus_entrance_hall: 'Entrance Hall',
  campus_desc_entrance: 'The notice board carries announcements. The trophy case waits for your diplomas.',
  campus_notice_board: 'Notice Board',
  campus_trophy_case: 'Trophy Case',
  campus_admissions: 'Admissions',
  campus_bell_tower: 'Bell Tower',
  campus_main_gate: 'Main Gate',
  campus_main_hall: 'Main Hall',
  campus_the_quad: 'The Quad',
  campus_front_path: 'Front Path',

  /* CAMPUS PRESENCE - "The Student Body" (PRESENCE.md). Six rows and not one
     more: four BLIPS, a chip label and the layer's own name. The bubbles are
     1-4 characters BY LAW (diegetic-prose rule: these are blips, never
     sentences), so a mod re-voices the greeting without ever being able to put
     a paragraph over a stranger's head. */
  presence_student_body: 'Student Body',
  presence_bubble_hi: 'hihi',
  presence_bubble_dots: '...',
  presence_bubble_wave_a: 'o/',
  presence_bubble_wave_b: '\\o',
  presence_here_tonight: 'here tonight',
  /* ...and the CONSENT ROW (P3). Every option names what it shows PUBLICLY:
     the player is agreeing to a specific thing, and a rung called only
     'Anonymous' is not one. Mirrored key-for-key in ArcademyHostService's
     NeutralLexicon, or a mod skin renders raw keys here. */
  presence_share_label: 'Show yourself on campus',
  presence_share_hint: 'Your last 24 hours replay as a ghost. Room head counts include you at every rung.',
  presence_share_off: 'Off - room head counts only',
  presence_share_anon: 'Anonymous - a ghost with no name or picture',
  presence_share_username: 'Username - your display name over the ghost',
  presence_share_discord: 'Discord - your display name and profile picture',
  presence_share_discord_note: 'Discord needs a linked account. Without one the school shows your name instead.',
  campus_east_wing: 'East Wing',
  campus_west_wing: 'West Wing',
  campus_desc_east: 'You can hear hammering behind the tape.',
  /* LOT 2 (2026-08-23) made the east wing the FRONT OFFICE - it holds Records
     and the Front Office counter (ex-Registrar) now, not three new classrooms.
     Same key, new sentence; campus.js carries the identical fallback. */
  campus_desc_east_open: 'The front office. Two counters, one bell, and a queue that is always you.',
  campus_desc_west_open: 'Older boards, deeper rooms. Nobody in here is in any hurry.',
  campus_desc_west: 'The boards are older here.',
  campus_sealed: 'Sealed',
  campus_opens_semester_2: 'Opens Semester II',
  campus_semester_3: 'Semester III',
  campus_in_session: 'In Session',
  campus_not_tonight: 'Not tonight',
  campus_next_bell: 'Next Bell',
  campus_step_inside: 'Step inside',
  campus_xp_first: 'First pass of the day pays XP.',
  campus_xp_retake: 'Retakes pay no XP - pride only.',
  campus_hint: 'Hover a room - click to step inside.',
  campus_hint_touch: 'Tap a room to step inside.',
  campus_night_sessions: 'Night Sessions',
  campus_rm: 'RM',
  /* --- the turn-your-phone card (shell/orientgate.js) --------------------
     PHONES ONLY, and a desktop window never sees any of these. Three pairs
     because the campus and a class are asking for different reasons: the
     campus wants width for its floor plan, a class wants the shape its own
     board was drawn for. */
  rotate_campus_title: 'Turn it sideways',
  rotate_campus_body: 'The floor plan runs wide, the way the cabinets actually sit along the walls, so give your phone a quarter turn and you get the whole place back on the glass with your spot still held.',
  rotate_landscape_title: 'Turn it sideways',
  rotate_landscape_body: 'This room was built wide, so give your phone a quarter turn and the board gets the width it was drawn for. Nothing is running while you sort it out.',
  rotate_portrait_title: 'Stand it back up',
  rotate_portrait_body: 'This one plays tall, so turn your phone upright and the board gets its full height back. Nothing is running while you sort it out.',
  /* The splash's knock line (boot.js). boot runs before this module loads, so
     it reads init.lexicon directly with the same English as its own fallback -
     keep the two strings identical when either moves. */
  intro_knock: 'Knock to enter',
  /* THE SECOND ASKING (boot.js escalateKnock). Four seconds after the school is
     ready and still nobody has touched anything, the line changes rather than
     repeating - the first words did not land, so louder is not the answer. */
  intro_knock_wait: 'Tap anywhere to knock',
  /* --- THE FRONT OFFICE SHEET (shell/settings.js) -----------------------
     Section titles, the two ceilings blurbs (one per host), the web's device
     rows, and the one-line summaries the folded headers wear. `{v}`, `{n}`,
     `{name}` and `{sep}` are filled by the page. */
  settings_ceilings_head: 'App ceilings',
  settings_ceilings_note_app: 'Set in the app and shown here so you know what the school has to work with.',
  settings_device_head: 'This device',
  settings_device_note: 'Sound and motion for this browser, on this phone or PC. Nothing here leaves the device.',
  settings_master_volume: 'Master volume',
  settings_master_volume_hint: 'One dial over every sound the school makes.',
  settings_motion: 'Motion',
  settings_motion_hint: 'Reduced keeps the room still. Off cuts every animation the school can cut.',
  settings_motion_off: 'Off',
  settings_motion_reduced: 'Reduced',
  settings_motion_full: 'Full',
  settings_distraction_head: 'Distraction',
  settings_channels_head: 'Channel ceilings',
  settings_channels_note: 'A class may use less than these. Never more.',
  settings_sound_head: 'Sound',
  settings_lessons_head: 'Lessons',
  settings_game_nothing: 'Nothing to configure - this class runs on the globals.',
  settings_sum_volume: 'Volume {v}',
  settings_sum_motion: 'Motion {v}',
  settings_sum_online_on: 'Online on',
  settings_sum_online_off: 'Online off',
  settings_sum_intensity: 'Intensity {v}',
  settings_sum_guard: 'Guard {v}',
  settings_sum_caps_all: 'All at 100%',
  settings_sum_caps_low: 'Lowest: {name} {v}',
  settings_sum_muted: 'Muted',
  settings_sum_sound: 'On{sep}Music {v}',
  settings_sum_tutorials_on: 'Tutorials on',
  settings_sum_tutorials_off: 'Tutorials skipped',
  settings_sum_board: 'Board {v}',
  settings_sum_keys: '{n} keys',
  settings_sum_key_one: '1 key',
  settings_sum_nothing: 'Nothing to set',
  /* --- THE MEDIA COUNTER, web only (MEDIA-CONTRACT §8) -------------------
     Rendered by shell/settings.js ONLY where the browser host shim declares
     `init.settings.mediaControls === true`, so the WebView2 build never draws
     a single one of these rows. Front-desk voice: the front office keeps a
     counter, you hand things over it, somebody writes it down. */
  media_head: 'Media',
  media_note: 'This is the counter where you say what the rooms are allowed to pull from. Anything you change is in play from your next class on, and whatever is running right now keeps the pile it already has.',
  media_consent_label: 'Pull from online',
  media_consent_hint: 'With this off nothing goes out to the network at all, and the rooms run on whatever you have handed over yourself.',
  media_niches_head: 'What we pull',
  media_niches_hint: 'Tick as many as you like. The desk hangs on to the last one, since an empty board leaves the rooms with nothing to work with.',
  media_niches_snapback: 'That was the last one ticked, so it went straight back up. Tick another and then you can drop it.',
  media_niches_none: 'The desk has no list to offer tonight.',
  media_lib_head: 'Subs on your list',
  media_lib_hint: 'Untick one to sit it out for a while, or use the X and it comes off the list everywhere.',
  media_lib_empty: 'Nothing on your list yet, so type a name below and we will go and see if it is really there.',
  media_lib_add_head: 'Add one',
  media_lib_add_ph: 'name of a sub',
  media_lib_add_btn: 'Add',
  media_lib_remove: 'Take it off the list',
  media_lib_clips: 'clips',
  media_lib_stills: 'pictures only',
  media_probe_checking: 'Having a look for',
  media_probe_ok: 'is on your list now.',
  media_probe_missing: 'came back empty, so give the spelling another go.',
  media_probe_dupe: 'is already on your list.',
  media_local_head: 'Your own media',
  media_local_hint: 'Hand over a folder, a zip, or a few things off your camera roll, and the rooms will deal them out like anything else. It stays on this device and it goes when you close the page.',
  media_local_folder: 'A folder',
  media_local_zip: 'A zip',
  media_local_gallery: 'Some files',
  media_local_clear: 'Clear the pile',
  media_local_empty: 'Nothing of yours in the pile yet.',
  media_local_counts: '{images} pictures and {videos} clips in the pile',
  media_local_skipped: '{n} we could not read',
  media_local_waiting: 'Waiting on your picker.',
  media_progress_reading: 'Reading',
  media_progress_unpacking: 'Unpacking',
  /* The trap-1 marker every media row wears while its echo is in the air. */
  media_pending: 'writing it down',
  /* --- the punch card + its ceremony (PUNCHCARD §4) ---------------------- */
  punchcard: 'Stamp Card',
  punchcard_holes: '{have} of {need}',
  /* THE LIVE TEXT ZONE on the card face (shell/punchcard.js). The count is the
   * card's own tight form ('3/10'); punchcard_holes stays the prose one the
   * Records docket prints. The eight rotating flavour lines live beside the
   * enrollment copy, in punchcard.js's PHRASE_LEX - one row each so a mod can
   * re-voice them one at a time. */
  punchcard_count: '{have}/{need}',
  punchcard_mastered: 'Mastered',
  punchcard_stamped: 'Stamped for today.',
  /* THE S DOUBLE (owner ruling 2026-08-23): a day the class graded S is worth a
     second hole, and the ceremony says so on the beat that punches it. */
  punchcard_stamped_s: 'Top marks. The card takes a second stamp.',
  punchcard_next_hole: 'Come back tomorrow for the next stamp.',
  punchcard_unlocked_chip: 'Unlocked',
  punchcard_unlocked_title: 'Assignment complete',
  punchcard_unlocked_line: 'This room is now open even when the course is not in session.',
  enroll_kicker: 'Enrollment',
  enroll_next: 'Next',
  enroll_begin: 'Begin class',
  enroll_card_line: 'Every class carries a stamp card. Ten stamps, one a night.',
  enroll_tutorial_line: 'One stamp for finishing your first class.',
  enroll_house_line: 'And one on the house. Welcome to the class.',
  /* DAY ONE IS THREE (owner ruling 2026-08-23), and the third hole says why. */
  enroll_signon_line: 'And one for signing on. The card starts warm.',
  /* --- the Records Office (PUNCHCARD §6) --------------------------------- */
  records_kicker: 'Records Office',
  records_lede: 'Ten cards, ten stamps each. The wall keeps them whether you come back or not.',
  records_enrolled: 'Enrolled',
  records_enrolled_on: 'Enrolled',
  records_unlocked_on: 'Unlocked',
  records_holes_punched: 'Stamps earned',
  records_holes_left: 'Stamps left',
  records_stamps: 'Daily stamps',
  records_no_stamps: 'No daily stamps yet.',
  records_not_enrolled: 'Not enrolled - attend the class',
  records_enroll_hint: 'The first graded finish opens the card and earns three stamps.',
  records_house_note: 'Day one is three stamps: finishing, on the house, signing on.',
  records_flip_hint: 'Pick a card to read its stamps.',
  records_empty_wall: 'Nothing on the wall yet. Attend a class and the first card gets pinned.',
  records_spot_close: 'Close',
  /* THE ROOM (shell/recordsroom.js, 0825). Four things you can touch in the
     painted office, plus the chrome of its two close-ups. `records_book` is
     deliberate and so is its value: the other word for that volume is a
     register word, and the register is barred from every user-facing string
     in this school. */
  records_tray: 'The card tray',
  records_board: 'The noticeboard',
  records_book: 'The book',
  records_storeroom: 'The storeroom',
  records_fresh: 'New',
  records_close_panel: 'Put the cards back',
  records_book_next: 'Next page',
  records_book_prev: 'Back a page',
  records_book_ch_school: 'The Arcademy',
  records_book_ch_rules: 'House rules',
  records_book_ch_tips: 'Tips',

  /* THE STUDENT ID (shell/campus.js's furniture card + shell/idcard.js's
     spotlight). The card is a document, not a share: nothing here names a
     Discord handle, an id or a url - the photo consent IS the `presenceShare`
     discord rung the settings page owns, said in the card's own words.
     `{n}` / `{m}` are substituted by the page. Every value is under the
     96-char MergeModTable cap, so a mod re-voices the whole card. */
  student_id_title: 'Student ID',
  id_photo_pending: 'Photo pending',
  id_photo_on: 'Discord photo on',
  id_photo_use: 'Use my Discord photo',
  id_photo_link: 'Link Discord for my photo',
  id_photo_waiting: 'Waiting on Discord...',
  id_chip_on: 'Photo on',
  id_chip_use: 'Use Discord photo',
  id_chip_link: 'Link Discord',
  id_chip_wait: 'Waiting...',
  id_photo_hint_app: 'Opens the Discord link-up in the app, then your photo goes on the card and on campus.',
  id_photo_hint_web: 'Sends you to Connections to link Discord, then straight back here with the photo on.',
  id_photo_hint_off: 'Your ghost on campus wears this photo too. Tap to take it down (your name stays).',
  id_photo_failed: 'Discord did not pick up. Try again in a minute.',
  id_photo_day: 'Photo day',
  id_no: 'Student no.',
  id_no_temp: 'temp',
  id_enrolled: 'Enrolled',
  id_homeroom: 'Homeroom',
  id_issued_at: 'Issued at',
  id_front_desk: 'Front desk',
  id_stat_semester: 'Term',
  id_stat_streak: 'Attendance streak',
  id_stat_perfect: 'Perfect days',
  id_stat_stamps: 'Stamps of 100',
  id_stat_best: 'S days',
  id_year: 'Year',
  id_grade_tier: 'Grade tier',
  id_to_go: '{n} to go',
  id_reinked: 'Re-inked',
  id_flip: 'Tap the card to turn it over. Esc to put it back.',
  id_back_lost: 'Lost it? Ask at the front desk. The second one costs you a stamp.',
  id_back_valid: 'Good for as long as the lights are on.',
  id_records_line: 'Records: {n} of {m} cards mastered',
  id_open_records: 'Open Records',
  id_spot_close: 'Close',

  /* THE ACCOUNT CHIP (shell/accountchip.js). A host slot: the web host fills it
     with a login to control from the top bar, the desktop never sends it. Front
     desk voice, no em-dashes, every row under the 96-char cap. */
  account_menu: 'Account',
  account_signed_in_as: 'Signed in as',
  account_open_card: 'Open my card',
  account_profile: 'Profile',
  account_sign_out: 'Sign out',

  /* Semester II ghost labels behind the tape (unregistered games get their
   * game_<key> row here, same convention the registry uses once they ship). */
  game_misdirection: 'Misdirection',
  game_sort: 'Sort',
  game_instant_recall: 'Instant Recall',
  game_echo: 'Echo',

  /* ORIENTATION DAY (ORIENTATION.md §3 / §5, shell/orientation.js) - the
     school's once-ever hello. Four rows: the beat's own name, and EMI's three
     lines, resolved here and handed to the `orientation` moment as
     `payload.line` (emi/moments.js keeps the same three as its fallback, and
     owns the faces). All well under the 96-char MergeModTable cap, so a mod
     re-voices the whole beat. The room this walks to is the Front Office and it
     is NEVER named in any of them. */
  orientation_kicker: 'Orientation Day',
  /* EMI ASKS: the Send button on the one question with a keyboard (a14, "what
     do i call you?"). The ONLY display string EMI renders - her questions,
     chips and reactions are all VERBATIM content and never pass through t().
     shell/shell.js resolves it and hands the answer to mountEmi. Her voice is
     lowercase, so this row is too. */
  emi_ask_send: 'send',
  emi_orientation_hi: 'a new student! i did a little spin. you missed it.',
  emi_orientation_card: "official! now you have to come back. it's the rules.",
  emi_orientation_go: "go! your first class doesn't know how lucky it is.",

  /* --- THE PHANTOM POST (shell/mail.js, mailbox.js, corkboard.js, bugle.js).
     Chrome only - letter bodies, notices and newspaper copy are CONTENT and
     never pass through t(). Every row is mirrored in the host's NeutralLexicon
     (ArcademyHostService) so a mod skin can re-voice the whole post room. */
  mail_kicker: 'Mail',
  mail_title: 'The Mail Box',
  mail_chip_label: 'Mail',
  mail_unread: 'unread',
  mail_all_read: 'read',
  mail_empty: 'Nothing in the box yet.',
  mail_pick: 'Pick an envelope to read it.',
  mail_delivered: 'Delivered',
  mail_new: 'New',
  mail_close: 'Close',
  board_kicker: 'Pinned up',
  board_title: 'Noticeboard',
  board_prop_label: 'Noticeboard',
  board_lede: 'What is up on the wall tonight. Some of it stays. Most of it does not.',
  board_empty: 'Nothing pinned up tonight.',
  board_rotates: 'The wall gets sorted through most days. What is pinned flat stays put.',
  board_kind_notice: 'Notice',
  board_kind_flyer: 'Flyer',
  board_kind_minutes: 'Minutes',
  board_note_open: 'Take this one down and read it',
  board_note_close: 'Put it back on the wall',
  bugle_issue: 'Issue',
  bugle_page: 'Page',
  bugle_pages: 'Pages',
  bugle_prev: 'Previous page',
  bugle_next: 'Next page',
  bugle_comics: 'Comics',
  bugle_empty: 'Nothing set for this page.',
  bugle_prop_label: 'The paper',

  /* WET INK (THE SEEP, tell 09). The one COPY tell, played completely straight:
   * a warm, chatty maintenance note on the noticeboard. First read is a shrug.
   * After the reveal it is the oldest confession in the building.
   *
   * STORED AS CLAUSE ROWS, joined with one space (vn/lex.js PAPERS' pattern and
   * trap 26's rule): a NeutralLexicon value over 96 characters can never be
   * mod-skinned, so every row here clears the cap while the joined paragraph
   * stays verbatim. Do not merge them back into one string.
   *
   * The seed clause (rows 3 and 4) is CANON and never changes across mods - the
   * wiring story is the school's own cover story. Front-desk voice throughout;
   * no cold register anywhere, because the whole trick is that it is innocent. */
  seep_wetink_title: 'FROM THE FRONT DESK',
  seep_wetink_1: 'Couple of things this week: the water fountain by 103 is fixed, you\'re welcome,',
  seep_wetink_2: 'and whoever keeps winning the gate raffle please come collect your pencils.',
  seep_wetink_3: 'Also if you see light under the Records door after closing,',
  seep_wetink_4: 'that\'s just the old wiring acting up again, Marco says he\'ll swap the breaker',
  seep_wetink_5: 'when the part shows up. Be good.',
  seep_wetink_sig: 'The front desk.',
  /* the annex (ANNEX-OS.md) - the lab under the Records Office. The fence
   * words are legal on these rows and nowhere else: every annex_* key renders
   * only downstairs. The first six were owed by annex/cams.js since the
   * camera-wall wave. */
  annex_cam: 'CAM',
  annex_rec: 'REC',
  annex_cam_gate: 'MAIN GATE',
  annex_lap_title: 'RECORDS ANNEX',
  annex_lap_locked: 'TERMINAL LOCKED',
  annex_lap_prompt: 'AWAITING KEY',
  annex_door: 'A wall panel, ajar',
  annex_room_label: 'The Records Annex',
  annex_back: 'step back',
  annex_hot_monitors: 'the monitors',
  annex_hot_shelf: 'the shelf',
  annex_hot_desk: 'the desk',
  annex_hot_door: 'the stairs',
  annex_hot_folder: 'the folder',
  annex_hot_binder: 'FIELD DATA',
  annex_hot_laptop: 'the laptop',
  annex_paper_close: 'put it down',
  annex_stamp_ongoing: 'ONGOING',
  annex_page_prev: 'previous page',
  annex_page_next: 'next page',
  annex_os_label: 'Annex terminal',
  annex_os_boot_1: 'RECORDS ANNEX / UNIT TERMINAL',
  annex_os_boot_2: 'memory check: fine, thanks for asking',
  annex_os_boot_3: 'feed wall link: up',
  annex_os_boot_4: 'archive index: 26 files, 5 drawers',
  annex_os_login_sub: 'authorised staff. there is no other kind of staff.',
  annex_os_pass: 'password',
  annex_os_enter: 'log in',
  annex_os_wrong: 'no. the note is right there.',
  annex_os_note: 'PW: CYBER-PUNK',
  annex_os_files: 'FILES',
  annex_os_registry: 'REGISTRY',
  annex_os_search: 'SUBJECT SEARCH',
  annex_os_term: 'TERMINAL',
  annex_os_close: 'close',
  annex_os_live: 'LIVE',
  annex_os_archive: 'ARCHIVE',
  annex_os_linkdown: 'LINK DOWN',
  annex_os_linkwait: 'link…',
  annex_os_retry: 'retry',
  annex_os_room: 'room',
  annex_os_enrolled: 'enrolled',
  annex_os_completed: 'completed',
  annex_os_all: 'all subjects',
  annex_os_redacted: 'withheld',
  annex_os_code: 'subject code',
  annex_os_open_file: 'open file',
  annex_os_notfound: 'that code is not on file. check the paper in the binder.',
  annex_os_file_title: 'SUBJECT FILE',
  annex_os_ongoing: 'ONGOING',
  annex_f_general: 'GENERAL',
  annex_f_since: 'on record since',
  annex_f_level: 'level',
  annex_f_xp: 'experience, lifetime',
  annex_f_minutes: 'supervised minutes',
  annex_f_video: 'screening minutes',
  annex_f_spiral: 'focus minutes',
  annex_f_ach: 'citations on file',
  annex_f_attend: 'ATTENDANCE',
  annex_f_streak: 'attendance streak',
  annex_f_perfect: 'perfect nights',
  annex_f_cards: 'cards mastered',
  annex_f_appstreak: 'reporting streak',
  annex_f_appbest: 'reporting streak, best',
  annex_f_sessions: 'sessions opened',
  annex_f_devices: 'DEVICES',
  annex_f_flashes: 'exposures delivered',
  annex_f_bubbles: 'targets cleared',
  annex_f_lockcards: 'sentences typed',
  annex_f_triggers: 'cue firings',
  annex_f_unit: 'UNIT OBSERVATION',
  annex_f_pets: 'pets received',
  annex_f_drags: 'relocations',
  annex_f_flings: 'ejections',
  annex_f_hides: 'dismissals',
  annex_f_restores: 'recalls from dock',
  annex_f_lines: 'lines delivered',
  annex_f_emisessions: 'sessions observed',
  annex_f_emidays: 'days observed',
  annex_f_hours: 'hours observed',
  campus_annex: 'Records Annex',
  campus_annex_status: 'Stairs down',
  campus_desc_annex: 'Under the office. The lights are off down there. The screens are not.',

  /* ------------------------------------------------------------------------
   * THE PRIZE COUNTER and the two currencies (economy wave, 2026-08-26).
   * Front-desk voice: warm, a bit scruffy, never a form. Every row here is a
   * NeutralLexicon candidate and every one of them is under the 96-character
   * cap a mod needs to be able to re-voice it (trap 26). The prize NAMES and
   * blurbs are deliberately NOT here - they ride init.economy.catalog from the
   * host, so the shelf is whatever the host says it is.
   * ---------------------------------------------------------------------- */
  campus_room_prizes: 'Prize Counter',
  campus_prizes_status: 'Open late',
  campus_desc_prizes: 'Tickets on the shelf, tokens in the case. Somebody is always restocking.',
  wallet_tickets: 'Tickets',
  wallet_tokens: 'Tokens',
  prize_counter_title: 'Prize Counter',
  prize_counter_sub: 'Tickets on the shelf, tokens in the case',
  prize_shelf: 'Ticket Shelf',
  prize_shelf_hint: 'Every graded class pays tickets. This is where they go.',
  prize_case: 'Token Case',
  prize_case_hint: 'Tokens only. Your first S of the day drops one in the tray.',
  prize_you_have: 'On you',
  prize_owned: 'Yours',
  prize_held: 'Holding',
  prize_buy: 'Trade',
  prize_soon: 'Arriving soon',
  prize_wait: 'Asking the counter',
  prize_bought: 'Wrapped up and yours.',
  prize_poor: 'Not quite enough on you for that one yet.',
  prize_owned_msg: 'You have that one already.',
  prize_full: 'Your pockets are full of those. Use one first.',
  prize_locked_msg: 'That one stays in the case for now.',
  prize_unknown: 'The counter does not know that one. Odd.',
  prize_quiet: 'The counter went quiet on that one. Try again in a moment.',
  prize_empty: 'Shelf is bare tonight. Come back when the truck has been.',
  prize_payday_label: 'Hot room tonight',
  prize_payday_2: 'is paying double',
  prize_payday_5: 'is paying five times over',
  /* the antechamber (shell/prizebooth.js): the window, the tray on the sill,
   * and what a shut counter says. `prize_closed` is one word because it is a
   * sign stencilled on a shutter, not a sentence. */
  prize_booth_window: 'The service window',
  prize_booth_tray: 'The ticket tray',
  prize_closed: 'Closed',
  prize_closed_line: 'The shutter is down and the sign above it has been switched off at the wall.',
  prize_no_payday: 'No room is paying over the odds tonight. Every graded class still pays tickets.',
  /* the arrival down the alley, and the one press the receipt offers (Locker
   * wave, 2026-08-28). Two verbs rather than one, because two different things
   * happen to a thing you just bought: you put an outfit or a frame ON, and you
   * hang a campus look UP. The desk toy has no verb here on purpose - buying it
   * turns the prop on by itself, so WHICH toy is pinned is a choice made in the
   * Locker's desk group and never a consequence of the purchase. */
  booth_alley_hint: 'The lit window is down at the end of the row.',
  booth_put_it_on: 'Put it on',
  booth_hang_it: 'Hang it up',
  settings_classes_head: 'Classes',
  campus_desc_prizes_shut: 'Shutter down over the window, parcels still stacked behind it. Back another night.',
  /* the Extra Credit lever, on the door card and in the painted room */
  lever_title: 'Extra Credit',
  lever_standard: 'Standard',
  lever_extra: 'Extra Credit',
  lever_honors: 'Honors',
  lever_standard_hint: 'Play it straight. Tickets pay the usual.',
  lever_extra_hint: 'Half again the tickets, and it asks more of you.',
  lever_honors_hint: 'Double tickets, and the only road to an S plus.',
  lever_extra_locked: 'Earn an A on anything and this one wakes up.',
  lever_honors_locked: 'The counter sells this one for a token.',
  free_swim_key_hint: 'Your key opens this one for a practice run. Nothing counts, nothing costs.',
  /* the payout beat on the report card */
  payout_tickets: 'Tickets',
  payout_token_minted: 'A token dropped in the tray. That is your one for today.',
  late_slip_used: 'A late slip covered you. Your streak never noticed.',

  /* ------------------------------------------------------------------------
   * THE LOCKER, RM 004 (the Locker wave, 2026-08-28).
   *
   * The counter's opposite number: the counter sells, the locker keeps. Same
   * front-desk voice, one register lower - a locker is a private thing and it
   * does not announce itself. Prize NAMES and blurbs are still not here; they
   * ride init.economy.catalog from the host exactly as the shelf's do, and the
   * locker reads the same rows. What IS here is everything the locker says in
   * its own voice: the doors, the groups, and the four picks that are page
   * state rather than shelf stock.
   * ---------------------------------------------------------------------- */
  campus_room_locker: 'The Locker',
  locker_sign: 'Locker',
  locker_status: 'Yours',
  locker_tip: 'Your own door in the row. Everything you have won is behind it.',
  locker_kicker: 'The Locker',
  locker_hot: 'Your locker',
  locker_title: 'The Locker',
  locker_sub: 'Room 004. Nobody else has the combination.',
  /* the six groups, in the order the doors open */
  locker_wear: 'Wear',
  locker_card: 'Card',
  locker_campus: 'Campus',
  locker_desk: 'Desk',
  locker_bag: 'In your bag',
  locker_always: 'Always on',
  /* the picks. Each family's first row is the house answer, and the house
     answer is never called "none" - it is what you were already wearing. */
  locker_outfit_standard: 'The usual',
  locker_outfit_varsity: 'Varsity jacket',
  locker_outfit_labcoat: 'Lab coat',
  locker_outfit_cheer: 'Cheer uniform',
  locker_outfit_swim: 'Swim team',
  locker_frame_plain: 'Plain',
  locker_frame_gold: 'Gold',
  locker_frame_navy: 'Navy',
  locker_toy_auto: 'Let the desk choose',
  locker_toy_spinner: 'Spinner',
  locker_toy_globe: 'Snow globe',
  locker_toy_lamp: 'Lava lamp',
  locker_toy_beads: 'Beads',
  /* the chrome */
  locker_selected: 'On',
  locker_held: 'x{n}',
  locker_empty: 'Nothing in here yet. The counter is one window up.',
  locker_more_at_counter: '{n} more at the counter',
  locker_ring_bell: 'Ring it',
  /* the two signposts, in Options and on the back of the ID card */
  locker_signpost: 'Outfits, frames and campus looks live in The Locker now. RM 004.',
  locker_signpost_go: 'Open The Locker',
  locker_unlock_hint: '{tok}2 at the counter',
  locker_open: 'Open Locker',
});

let table = Object.create(null);

/** Install the host-resolved table. Non-objects are ignored (defaults stand). */
export function setLexicon(next) {
  const out = Object.create(null);
  if (next && typeof next === 'object') {
    for (const k of Object.keys(next)) {
      const v = next[k];
      if (typeof v === 'string' || typeof v === 'number') out[k] = String(v);
    }
  }
  table = out;
  return table;
}

/** De-snake an unknown key so the worst case is still readable English. */
function humanize(key) {
  return String(key || '')
    .replace(/[_.-]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/\b[a-z]/g, (c) => c.toUpperCase());
}

/**
 * Resolve a display string.
 * @param {string} key      internal key (neutral, fixed)
 * @param {string} [fallback] caller's English if the mod has no row
 */
export function t(key, fallback) {
  const v = table[key];
  if (typeof v === 'string' && v.length) return v;
  if (typeof fallback === 'string' && fallback.length) return fallback;
  const d = DEFAULT_LEXICON[key];
  if (typeof d === 'string' && d.length) return d;
  return humanize(key);
}

/** True if the active mod actually skinned this key (for "is this authored?" checks). */
export function hasLexicon(key) { return typeof table[key] === 'string' && !!table[key].length; }

/** Grade-tier display via the ONE row family (SYNTHESIS #1). */
export function tierLabel(tier) {
  const n = Math.max(1, Math.min(4, Math.round(Number(tier) || 1)));
  return t('grade_tier_' + n, 'Year ' + n);
}

/** Grade letter display ('S+'|'S'|'A'|'B'|'C'|'pass').
 *  S+ CANNOT USE THE DERIVED KEY: 'grade_s+' is not a key shape a NeutralLexicon
 *  row (or a mod table) can carry, so the one letter with punctuation in it is
 *  spelled out as `grade_splus` here and nowhere else. */
export function gradeLabel(grade) {
  const raw = String(grade == null ? '' : grade).trim();
  if (raw.toUpperCase() === 'S+') return t('grade_splus', 'S+');
  const g = raw.toLowerCase();
  return t('grade_' + g, g === 'pass' ? 'PASS' : raw.toUpperCase());
}

/** Family chip display. */
export function familyLabel(family) {
  return t('family_' + String(family || ''), String(family || ''));
}

export default t;
