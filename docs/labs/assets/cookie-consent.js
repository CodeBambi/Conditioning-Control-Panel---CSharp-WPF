/*
 * Cookie consent for cclabs.app.
 *
 * Behaviour summary (opt-out):
 *   - Third-party content (Google Fonts, the redgifs hero iframe) loads by
 *     default. The HTML ships the embeds in placeholder form (the iframe
 *     carries data-cookie-src; the font <link> carries data-fonts-href);
 *     this script materialises them on first paint unless the visitor has
 *     previously revoked consent.
 *   - On first visit the banner still appears, with three equally-prominent
 *     buttons: Accept all / Reject all / Customize. Accept records consent
 *     and dismisses; Reject removes the injected content and records the
 *     opt-out so it persists.
 *   - The "Cookie preferences" link in the footer reopens the modal so
 *     visitors can change their mind at any time.
 *
 * Storage: localStorage["ccp_consent_v1"] = JSON-stringified
 *          { thirdParty: boolean, ts: ISO string, version: 1 }
 *   - Bump VERSION to force every visitor to re-decide on a policy change.
 *   - ts is compared against now; older than RECONSENT_DAYS forces re-prompt.
 */

(function () {
  'use strict';

  var STORAGE_KEY = 'ccp_consent_v1';
  var VERSION = 1;
  var RECONSENT_DAYS = 365;

  // Age gate: bump AGE_VERSION to force every visitor to re-confirm.
  var AGE_KEY = 'ccp_age_v1';
  var AGE_VERSION = 1;

  var THIRD_PARTY_HOSTS = [
    'fonts.googleapis.com',
    'fonts.gstatic.com',
    'redgifs.com',
  ];

  // ─── Storage helpers ──────────────────────────────────────────────

  function readConsent() {
    try {
      var raw = window.localStorage.getItem(STORAGE_KEY);
      if (!raw) return null;
      var parsed = JSON.parse(raw);
      if (!parsed || parsed.version !== VERSION) return null;
      var ageMs = Date.now() - new Date(parsed.ts).getTime();
      var maxMs = RECONSENT_DAYS * 24 * 60 * 60 * 1000;
      if (!isFinite(ageMs) || ageMs > maxMs) return null;
      return { thirdParty: !!parsed.thirdParty, ts: parsed.ts };
    } catch (_) {
      return null;
    }
  }

  function writeConsent(thirdParty) {
    var record = {
      thirdParty: !!thirdParty,
      ts: new Date().toISOString(),
      version: VERSION,
    };
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(record));
    } catch (_) {
      // localStorage disabled - fall through; banner will reappear next load.
    }
    document.dispatchEvent(
      new CustomEvent('cookie-consent-changed', { detail: record })
    );
    return record;
  }

  // ─── Apply / revoke third-party content ───────────────────────────

  function injectFonts() {
    if (document.head.querySelector('link[data-cc-fonts="active"]')) return;

    var pc1 = document.createElement('link');
    pc1.rel = 'preconnect';
    pc1.href = 'https://fonts.googleapis.com';
    pc1.setAttribute('data-cc-fonts', 'active');
    document.head.appendChild(pc1);

    var pc2 = document.createElement('link');
    pc2.rel = 'preconnect';
    pc2.href = 'https://fonts.gstatic.com';
    pc2.crossOrigin = '';
    pc2.setAttribute('data-cc-fonts', 'active');
    document.head.appendChild(pc2);

    var placeholders = document.head.querySelectorAll(
      'link[data-cookie-blocked="third-party"][data-fonts-href]'
    );
    placeholders.forEach(function (ph) {
      var stylesheet = document.createElement('link');
      stylesheet.rel = 'stylesheet';
      stylesheet.href = ph.getAttribute('data-fonts-href');
      stylesheet.setAttribute('data-cc-fonts', 'active');
      document.head.appendChild(stylesheet);
    });
  }

  function revokeFonts() {
    document.head
      .querySelectorAll('link[data-cc-fonts="active"]')
      .forEach(function (el) {
        el.parentNode.removeChild(el);
      });
  }

  function activateBlockedIframes() {
    document
      .querySelectorAll(
        'iframe[data-cookie-blocked="third-party"][data-cookie-src]'
      )
      .forEach(function (iframe) {
        var holder = iframe.parentNode;
        if (holder && !holder.hasAttribute('data-cc-holder')) {
          holder.setAttribute('data-cc-holder', '');
        }
        if (!iframe.getAttribute('src')) {
          iframe.setAttribute('src', iframe.getAttribute('data-cookie-src'));
        }
        iframe.classList.remove('cc-hidden');
        if (holder) {
          var ph = holder.querySelector('.cc-blocked-placeholder');
          if (ph) ph.classList.add('cc-hidden');
        }
      });
  }

  function deactivateBlockedIframes() {
    document
      .querySelectorAll(
        'iframe[data-cookie-blocked="third-party"][data-cookie-src]'
      )
      .forEach(function (iframe) {
        var holder = iframe.parentNode;
        if (holder && !holder.hasAttribute('data-cc-holder')) {
          holder.setAttribute('data-cc-holder', '');
        }
        iframe.removeAttribute('src');
        iframe.classList.add('cc-hidden');
        if (!holder) return;
        var ph = holder.querySelector('.cc-blocked-placeholder');
        if (!ph) {
          ph = buildPlaceholder(iframe);
          holder.appendChild(ph);
        }
        ph.classList.remove('cc-hidden');
      });
  }

  function buildPlaceholder(iframe) {
    var host = '';
    try {
      host = new URL(iframe.getAttribute('data-cookie-src')).hostname;
    } catch (_) {
      host = 'a third-party service';
    }
    return el('div', { class: 'cc-blocked-placeholder' }, [
      el('div', { class: 'cc-blocked-placeholder-icon', html: '🍪' }),
      el(
        'p',
        { class: 'cc-blocked-placeholder-title' },
        ['Third-party content blocked']
      ),
      el('p', { class: 'cc-blocked-placeholder-text', html:
        'This player loads from <code>' + host + '</code>. ' +
        'You opted out of third-party content; allow it again to view.' }),
      el(
        'button',
        {
          type: 'button',
          class: 'cc-btn cc-btn-primary',
          on: {
            click: function () {
              var rec = writeConsent(true);
              applyConsent(rec);
              hideBanner();
              closeModal();
            },
          },
        },
        ['Allow third-party content']
      ),
    ]);
  }

  function applyConsent(record) {
    // Default-off: nothing third-party loads until there is an explicit yes.
    // "No decision yet" is treated as no, so a first-time visitor who never
    // touches the banner still makes zero requests to Google or redgifs.
    if (record && record.thirdParty === true) {
      injectFonts();
      activateBlockedIframes();
    } else {
      revokeFonts();
      deactivateBlockedIframes();
    }
  }

  // ─── DOM rendering helper ─────────────────────────────────────────

  function el(tag, props, children) {
    var node = document.createElement(tag);
    if (props) {
      for (var k in props) {
        if (k === 'class') node.className = props[k];
        else if (k === 'html') node.innerHTML = props[k];
        else if (k === 'on' && typeof props.on === 'object') {
          for (var ev in props.on) node.addEventListener(ev, props.on[ev]);
        } else if (k in node) node[k] = props[k];
        else node.setAttribute(k, props[k]);
      }
    }
    (children || []).forEach(function (c) {
      if (c == null) return;
      node.appendChild(typeof c === 'string' ? document.createTextNode(c) : c);
    });
    return node;
  }

  // ─── Banner ───────────────────────────────────────────────────────

  var bannerNode = null;
  function showBanner() {
    if (bannerNode) return;
    bannerNode = el('div', { class: 'cc-banner', role: 'region',
      'aria-label': 'Cookie consent' }, [
      el('div', { class: 'cc-banner-inner' }, [
        el('p', { class: 'cc-banner-text', html:
          'The broadcast on the homepage can load a video player from ' +
          '<strong>redgifs.com</strong> (only when you tune in), and some ' +
          'older pages load <strong>Google Fonts</strong>. Both transmit ' +
          'your IP to those services, so neither loads until you allow it. ' +
          'Say no and the broadcast plays our own copy instead. ' +
          '<a href="https://cclabs.app/privacy-policy.html#cookies">Privacy policy</a>.' }),
        el('div', { class: 'cc-banner-actions' }, [
          el('button', { type: 'button', class: 'cc-btn',
            on: { click: function () { onAcceptAll(); } } },
            ['Accept all']),
          el('button', { type: 'button', class: 'cc-btn',
            on: { click: function () { onRejectAll(); } } },
            ['Reject all']),
          el('button', { type: 'button', class: 'cc-btn',
            on: { click: function () { openModal(); } } },
            ['Customize']),
        ]),
      ]),
    ]);
    document.body.appendChild(bannerNode);
  }

  function hideBanner() {
    if (!bannerNode) return;
    bannerNode.parentNode.removeChild(bannerNode);
    bannerNode = null;
  }

  // ─── Modal ────────────────────────────────────────────────────────

  var modalNode = null;
  function openModal() {
    if (modalNode) return;
    var existing = readConsent();
    // Default-off: with no stored decision nothing is loading, so the toggle
    // has to start off or it would misreport the live state.
    var initialThirdParty = existing ? !!existing.thirdParty : false;

    var checkbox = el('input', { type: 'checkbox', id: 'cc-toggle-third-party' });
    checkbox.checked = initialThirdParty;

    modalNode = el('div', { class: 'cc-modal-backdrop', role: 'dialog',
      'aria-modal': 'true', 'aria-labelledby': 'cc-modal-title',
      on: {
        click: function (e) { if (e.target === modalNode) closeModal(); },
        keydown: function (e) { if (e.key === 'Escape') closeModal(); },
      } }, [
      el('div', { class: 'cc-modal' }, [
        el('h2', { id: 'cc-modal-title' }, ['Cookie preferences']),
        el('p', { class: 'cc-modal-lede', html:
          'Choose what loads on cclabs.app. Your choice is stored in ' +
          'localStorage on this device only and you can change it any time ' +
          'via the &ldquo;Cookie preferences&rdquo; link in the footer. See ' +
          'our <a href="https://cclabs.app/privacy-policy.html#cookies">privacy policy</a> for ' +
          'details.' }),

        el('div', { class: 'cc-toggle' }, [
          el('div', { class: 'cc-toggle-info' }, [
            el('h3', null, ['Strictly necessary']),
            el('p', { html:
              'A first-party <code>rc_last_code</code> entry on the ' +
              'remote-control page only - remembers your last code so the ' +
              'field pre-fills. Never leaves your browser. Required for the ' +
              'page to be useful.' }),
          ]),
          el('span', { class: 'cc-toggle-locked' }, ['Always on']),
        ]),

        el('div', { class: 'cc-toggle' }, [
          el('div', { class: 'cc-toggle-info' }, [
            el('h3', null, ['Third-party content']),
            el('p', { html:
              'Google Fonts (Poppins, on some older pages) and the redgifs ' +
              'broadcast on the homepage (which only ever loads when you tune ' +
              'in). Off until you allow it. Left off, those pages use your ' +
              'system font and the broadcast plays a copy served from here.' }),
          ]),
          el('label', { class: 'cc-switch' }, [
            checkbox,
            el('span', { class: 'cc-switch-track' }),
          ]),
        ]),

        el('div', { class: 'cc-modal-actions' }, [
          el('button', { type: 'button', class: 'cc-btn',
            on: { click: function () { closeModal(); } } },
            ['Cancel']),
          el('button', { type: 'button', class: 'cc-btn cc-btn-primary',
            on: { click: function () {
              var rec = writeConsent(checkbox.checked);
              applyConsent(rec);
              hideBanner();
              closeModal();
            } } },
            ['Save preferences']),
        ]),
      ]),
    ]);
    document.body.appendChild(modalNode);
    setTimeout(function () { checkbox.focus(); }, 0);
  }

  function closeModal() {
    if (!modalNode) return;
    modalNode.parentNode.removeChild(modalNode);
    modalNode = null;
  }

  // Quick actions

  function onAcceptAll() {
    var rec = writeConsent(true);
    applyConsent(rec);
    hideBanner();
    closeModal();
  }

  function onRejectAll() {
    var rec = writeConsent(false);
    applyConsent(rec);
    hideBanner();
    closeModal();
  }

  // ─── Footer "Cookie preferences" link ─────────────────────────────

  function attachFooterLink() {
    var existing = document.getElementById('cc-footer-link');
    if (existing) {
      existing.addEventListener('click', function (e) {
        e.preventDefault();
        openModal();
      });
      return;
    }

    var footer = document.querySelector('.footer-links');
    if (!footer) return; // page has no footer (404, /remote/) - that's OK

    var link = el('a', {
      id: 'cc-footer-link',
      href: '#',
      on: {
        click: function (e) {
          e.preventDefault();
          openModal();
        },
      },
    }, ['Cookie preferences']);
    footer.appendChild(link);
  }

  // ─── Age gate (18+) ───────────────────────────────────────────────

  function isAgeConfirmed() {
    try {
      var raw = window.localStorage.getItem(AGE_KEY);
      if (!raw) return false;
      var parsed = JSON.parse(raw);
      return parsed && parsed.confirmed === true && parsed.version === AGE_VERSION;
    } catch (_) {
      return false;
    }
  }

  function recordAgeConfirmation() {
    try {
      window.localStorage.setItem(
        AGE_KEY,
        JSON.stringify({
          confirmed: true,
          ts: new Date().toISOString(),
          version: AGE_VERSION,
        })
      );
    } catch (_) {
      // localStorage disabled - gate will reappear next load. Acceptable.
    }
  }

  var ageNode = null;

  function showAgeGate() {
    if (ageNode) return;
    document.documentElement.classList.add('cc-age-pending');
    ageNode = el(
      'div',
      {
        class: 'cc-age-overlay',
        role: 'dialog',
        'aria-modal': 'true',
        'aria-labelledby': 'cc-age-title',
        on: {
          // Block Tab navigation from leaving the modal.
          keydown: function (e) {
            if (e.key === 'Escape') e.preventDefault();
          },
        },
      },
      [
        el('div', { class: 'cc-age-modal' }, [
          el('h2', { id: 'cc-age-title' }, ['Age verification']),
          el('p', { html:
            'This site contains adult content intended for users 18 or older. ' +
            'By clicking below you confirm you are at least 18 years old in ' +
            'your jurisdiction.' }),
          el('div', { class: 'cc-age-actions' }, [
            el(
              'button',
              {
                type: 'button',
                class: 'cc-age-btn cc-age-btn-confirm',
                on: { click: onConfirmAge },
              },
              ['I am 18+']
            ),
            el(
              'button',
              {
                type: 'button',
                class: 'cc-age-btn cc-age-btn-leave',
                on: { click: onLeaveSite },
              },
              ['Leave']
            ),
          ]),
          el('p', { class: 'cc-age-fineprint', html:
            'Your confirmation is stored locally on this device only. No ' +
            'birthdate is collected. <a href="https://cclabs.app/privacy-policy.html#age-requirement" ' +
            'style="color:#ff8faf;text-decoration:underline">More on age policy</a>.' }),
        ]),
      ]
    );
    document.body.appendChild(ageNode);
    setTimeout(function () {
      var btn = ageNode.querySelector('.cc-age-btn-confirm');
      if (btn) btn.focus();
    }, 0);
  }

  function hideAgeGate() {
    document.documentElement.classList.remove('cc-age-pending');
    if (!ageNode) return;
    ageNode.parentNode.removeChild(ageNode);
    ageNode = null;
  }

  // Broadcast that age is confirmed so other page scripts (e.g. the rabbit-hole
  // 3D dive) can hold their intro until the visitor has passed the 18+ gate.
  function signalAgeConfirmed() {
    try { window.dispatchEvent(new CustomEvent('cc-age-confirmed')); } catch (_) {}
  }

  function onConfirmAge() {
    recordAgeConfirmation();
    hideAgeGate();
    signalAgeConfirmed();
    runCookieFlow();
  }

  function onLeaveSite() {
    // Try window.close() first (works for tabs scripts opened); fall back to
    // a neutral "you can close this tab" screen so we don't leak our domain
    // in a Referer header by redirecting elsewhere.
    window.close();
    setTimeout(function () {
      document.documentElement.innerHTML =
        '<head><meta charset="utf-8"><title>Goodbye</title></head>' +
        '<body style="display:flex;height:100vh;align-items:center;' +
        'justify-content:center;font-family:-apple-system,system-ui,sans-serif;' +
        'color:#9b9ba8;background:#0a0a0f;margin:0;padding:1rem;text-align:center">' +
        'You can safely close this tab.</body>';
    }, 100);
  }

  // ─── Init ─────────────────────────────────────────────────────────

  function runCookieFlow() {
    attachFooterLink();

    var consent = readConsent();
    // Default-on. Apply the (possibly null) consent record; applyConsent
    // treats anything other than an explicit opt-out as "load it".
    applyConsent(consent);
    // Show the banner only on first visit (or after consent expired).
    if (!consent) {
      showBanner();
    }
  }

  function init() {
    if (!isAgeConfirmed()) {
      // Don't load fonts or the redgifs iframe yet - leave the placeholder
      // markup inert until the visitor confirms age.
      showAgeGate();
      return;
    }
    // Returning, already-confirmed visitor: no gate shown, but still announce so
    // age-gated scripts (rabbit-hole dive) start without waiting for a click.
    signalAgeConfirmed();
    runCookieFlow();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }

  window.CCPCookieConsent = {
    open: openModal,
    read: readConsent,
    accept: onAcceptAll,
    reject: onRejectAll,
    THIRD_PARTY_HOSTS: THIRD_PARTY_HOSTS,
    // Age gate hooks (mostly for debugging / manual reset)
    age: {
      isConfirmed: isAgeConfirmed,
      reset: function () {
        try { window.localStorage.removeItem(AGE_KEY); } catch (_) {}
      },
    },
  };
})();
