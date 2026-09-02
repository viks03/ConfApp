/* ═══════════════════════════════════════════════════════════════════
   scriptPayment.js — Blockchain Education 2026
   Логика за страницата за плащане (Крипто и IBAN)
═══════════════════════════════════════════════════════════════════ */

var _tierPriceEUR = (window.TierData && window.TierData.priceEUR) ? window.TierData.priceEUR : 120.00;
var _tr = window.PayTranslations || {};

// ── Crypto state ──────────────────────────────────────────────────
var cryptoState = {
    orderId:      null,
    pollingTimer: null,
    expiryTimer:  null,   
    isLoading:    false
};

// ══════════════════════════════════════════════════════════════════
// INIT — DOMContentLoaded
// ══════════════════════════════════════════════════════════════════
document.addEventListener('DOMContentLoaded', function () {
    loadActiveCryptoOrder();
});

// ══════════════════════════════════════════════════════════════════
// TAB SWITCHING
// ══════════════════════════════════════════════════════════════════
function switchTab(name, el) {
    document.querySelectorAll('.m-tab').forEach(function (t) {
        t.classList.remove('active');
        t.setAttribute('aria-selected', 'false');
    });
    document.querySelectorAll('.pay-panel').forEach(function (p) {
        p.classList.remove('active');
    });
    el.classList.add('active');
    el.setAttribute('aria-selected', 'true');
    var panel = document.getElementById('panel-' + name);
    if (panel) panel.classList.add('active');
}

// ══════════════════════════════════════════════════════════════════
// IBAN
// ══════════════════════════════════════════════════════════════════
function handleIbanDone() {
    var btn = document.getElementById('btn-iban-done');
    var box = document.getElementById('iban-submitted-box');

    // Скриваме бутона веднага
    if (btn) btn.style.display = 'none';

    // Взимаме antiforgery токена — задължителен за Razor Pages POST
    var token = '';
    var tokenInput = document.querySelector('input[name="__RequestVerificationToken"]');
    if (tokenInput) {
        token = tokenInput.value;
    } else {
        var metaToken = document.querySelector('meta[name="RequestVerificationToken"]');
        if (metaToken) token = metaToken.getAttribute('content');
    }

    if (!token) {
        console.error('[IBAN] No antiforgery token found — POST will be rejected.');
        if (btn) btn.style.display = '';
        return;
    }

    fetch('?handler=SubmitIban', {
        method:  'POST',
        headers: {
            'RequestVerificationToken': token,
            'X-Requested-With':         'XMLHttpRequest'
        }
    })
    .then(function (res) {
        if (res.status === 400) throw new Error('Antiforgery validation failed (400)');
        if (res.status === 401) { window.location.href = '/Login'; return null; }
        if (!res.ok) throw new Error('HTTP ' + res.status);
        return res.json();
    })
    .then(function (data) {
        if (!data) return;
        if (data.success) {
            // Добавяме клас "visible" — CSS-ът го показва чрез .iban-submitted-box.visible { display: block }
            if (box) {
                box.classList.add('visible');
                box.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            }
        } else {
            console.warn('[IBAN] Server error:', data.error);
            if (btn) btn.style.display = '';
        }
    })
    .catch(function (err) {
        console.warn('[IBAN] Submit error:', err);
        if (btn) btn.style.display = '';
    });
}

// ══════════════════════════════════════════════════════════════════
// CRYPTO — ЛИМИТ LOCK / UNLOCK
// ══════════════════════════════════════════════════════════════════
function lockCryptoButtons(expiresAt) {
    document.querySelectorAll('.crypto-item').forEach(function (btn) {
        btn.disabled      = true;
        btn.style.opacity = '0.4';
        btn.style.cursor  = 'not-allowed';
        btn.title = _tr.cryptoLimitTitle || 'Maximum active orders reached.';
    });

    var limitInfo = document.getElementById('crypto-limit-info');
    if (limitInfo) limitInfo.remove(); 

    limitInfo = document.createElement('div');
    limitInfo.id = 'crypto-limit-info';
    limitInfo.style.cssText =
        'background:rgba(255,149,0,0.07);border:1px solid rgba(255,149,0,0.3);' +
        'border-radius:4px;padding:12px 16px;margin-top:10px;font-size:0.78rem;' +
        'color:#ff9500;display:flex;align-items:flex-start;gap:10px;';

    var limitMsgText = _tr.cryptoLimitMsg || 'You have reached the maximum number of active orders. Please wait for existing orders to expire.';
    var countdownHtml = expiresAt
        ? '<div id="limit-countdown" style="margin-top:6px;color:#ff9500;font-weight:600;font-size:0.75rem;"></div>'
        : '';

    limitInfo.innerHTML =
        '<span style="font-size:1rem;flex-shrink:0;margin-top:1px;">&#x23F3;</span>' +
        '<div>' +
            '<div style="font-weight:700;letter-spacing:0.06em;text-transform:uppercase;' +
                 'font-family:Oswald,Arial,sans-serif;margin-bottom:3px;font-size:0.75rem;">' +
                 (_tr.cryptoLimitTitle || 'Maximum Orders Reached') +
            '</div>' +
            '<div style="color:#aaa;font-weight:300;line-height:1.55;">' + limitMsgText + '</div>' +
            countdownHtml +
        '</div>';

    var grid = document.querySelector('.crypto-grid');
    if (grid && grid.parentNode) grid.parentNode.insertBefore(limitInfo, grid.nextSibling);

    if (expiresAt) {
        var expiry  = new Date(expiresAt.replace(' ', 'T') + 'Z');
        var cEl     = document.getElementById('limit-countdown');
        var tick    = setInterval(function () {
            var diff = Math.floor((expiry - Date.now()) / 1000);
            if (diff <= 0) {
                clearInterval(tick);
                limitInfo.remove();
                unlockCryptoButtons();
                return;
            }
            var m = Math.floor(diff / 60), s = diff % 60;
            var prefix = _tr.cryptoUnlocksIn || 'Unlocks in';
            if (cEl) cEl.textContent = prefix + ' ' + m + ':' + (s < 10 ? '0' : '') + s;
        }, 1000);
    }
}

function unlockCryptoButtons() {
    document.querySelectorAll('.crypto-item').forEach(function (btn) {
        btn.disabled      = false;
        btn.style.opacity = '';
        btn.style.cursor  = '';
        btn.title         = '';
    });
    var limitInfo = document.getElementById('crypto-limit-info');
    if (limitInfo) limitInfo.remove();
}

// ══════════════════════════════════════════════════════════════════
// CRYPTO — ИЗБОР НА МОНЕТА
// ══════════════════════════════════════════════════════════════════
function selectCrypto(el, currency, network, coinName) {
    if (cryptoState.isLoading) return;
    if (el.disabled) return;

    document.querySelectorAll('.crypto-item').forEach(function (c) { c.classList.remove('selected'); });
    el.classList.add('selected');

    var coinNameEl = document.getElementById('cryptoCoinName');
    var netEl      = document.getElementById('cryptoNetwork');
    if (coinNameEl) coinNameEl.textContent = coinName || currency;
    if (netEl)      netEl.textContent      = network  || '';

    var prompt = document.getElementById('crypto-select-prompt');
    var grid   = document.getElementById('crypto-details-grid');
    if (prompt) prompt.style.display = 'none';
    if (grid)   grid.style.display   = 'grid';

    clearCryptoMessages();
    stopPolling();
    stopExpiryTimer();
    setCryptoLoading(true);

    fetch('/api/crypto/create-order', {
        method:  'POST',
        headers: { 'Content-Type': 'application/json' },
        body:    JSON.stringify({ currency: currency, network: network })
    })
    .then(function (res) {
        if (res.redirected && res.url.includes('/Login')) {
            window.location.href = '/Login';
            return null;
        }
        return res.json();
    })
    .then(function (data) {
        if (!data) return;
        setCryptoLoading(false);

        if (!data.success) {
            if (data.error && (data.error.toLowerCase().includes('maximum') ||
                               data.error.toLowerCase().includes('limit'))) {
                lockCryptoButtons(null);
            }
            showCryptoError(data.error || (_tr.cryptoErr_Default || 'Could not create payment order.'));
            return;
        }

        cryptoState.orderId = data.orderId;

        var addrEl   = document.getElementById('walletAddr');
        var amountEl = document.getElementById('cryptoAmount');
        if (addrEl)   addrEl.textContent   = data.cryptoAddress || '';
        if (amountEl) amountEl.textContent = data.amount        || '';

        var qrImg = document.getElementById('cryptoQR');
        var qrFb  = document.getElementById('qr-fallback');
        if (data.qrCode && qrImg) {
            qrImg.src           = data.qrCode;
            qrImg.style.display = 'block';
            if (qrFb) qrFb.style.display = 'none';
        } else if (qrImg) {
            qrImg.style.display = 'none';
            if (qrFb) qrFb.style.display = 'flex';
        }

        if (data.expiresAt) showExpiryCountdown(data.expiresAt);
        startPolling(data.orderId);
    })
    .catch(function (err) {
        setCryptoLoading(false);
        showCryptoError(_tr.cryptoErr_Network || 'Network error. Please check your connection and try again.');
        console.error('[Crypto] Order error:', err);
    });
}

// ══════════════════════════════════════════════════════════════════
// CRYPTO — POLLING
// ══════════════════════════════════════════════════════════════════
function startPolling(orderId) {
    stopPolling();
    cryptoState.pollingTimer = setInterval(function () {
        fetch('/api/crypto/check-status/' + orderId)
            .then(function (res) {
                if (res.status === 401 || res.status === 403) {
                    stopPolling();
                    return null;
                }
                return res.json();
            })
            .then(function (data) {
                if (!data || !data.success) return;

                if (data.isPaid) {
                    stopPolling();
                    stopExpiryTimer();
                    showPaymentConfirmed();
                    setTimeout(function () { window.location.href = '/Profile'; }, 2500);
                    return;
                }

                if (data.isExpired) {
                    stopPolling();
                    stopExpiryTimer();
                    unlockCryptoButtons();
                    showCryptoError(_tr.cryptoErr_Expired || 'This payment order has expired. Please select a currency again.');
                }
            })
            .catch(function (err) { console.error('[Polling] Error:', err); });
    }, 15000);
}

function stopPolling() {
    if (cryptoState.pollingTimer) {
        clearInterval(cryptoState.pollingTimer);
        cryptoState.pollingTimer = null;
    }
}

function stopExpiryTimer() {
    if (cryptoState.expiryTimer) {
        clearInterval(cryptoState.expiryTimer);
        cryptoState.expiryTimer = null;
    }
}

// ══════════════════════════════════════════════════════════════════
// CRYPTO — UI HELPERS
// ══════════════════════════════════════════════════════════════════
function setCryptoLoading(isLoading) {
    cryptoState.isLoading = isLoading;
    var walletBox = document.querySelector('.wallet-box');
    var qrBox     = document.querySelector('.qr-box');
    var addrEl    = document.getElementById('walletAddr');
    var amountEl  = document.getElementById('cryptoAmount');

    if (isLoading) {
        if (walletBox) walletBox.style.opacity = '0.4';
        if (qrBox)     qrBox.style.opacity     = '0.4';
        if (addrEl)    addrEl.textContent       = 'Loading...';
        if (amountEl)  amountEl.textContent     = 'Calculating...';
        var qrImg = document.getElementById('cryptoQR');
        if (qrImg) { qrImg.src = ''; qrImg.style.display = 'none'; }
        var qrFb = document.getElementById('qr-fallback');
        if (qrFb) qrFb.style.display = 'none';
    } else {
        if (walletBox) walletBox.style.opacity = '1';
        if (qrBox)     qrBox.style.opacity     = '1';
    }
}

function clearCryptoMessages() {
    ['crypto-error-msg', 'crypto-expiry', 'crypto-confirmed'].forEach(function (id) {
        var el = document.getElementById(id);
        if (el) el.remove();
    });
}

function showCryptoError(msg) {
    clearCryptoMessages();

    var grid   = document.getElementById('crypto-details-grid');
    var prompt = document.getElementById('crypto-select-prompt');
    if (grid)   grid.style.display   = 'none';
    if (prompt) prompt.style.display = 'flex';

    document.querySelectorAll('.crypto-item').forEach(function (c) { c.classList.remove('selected'); });

    var icon    = '\u2715';
    var title   = _tr.cryptoTitle_PayErr    || 'Payment Error';
    var color   = '#dc3545';
    var bgColor = 'rgba(220,53,69,0.07)';
    var bdColor = 'rgba(220,53,69,0.25)';
    var hint    = _tr.cryptoHint_SelectAgain || 'Please select a cryptocurrency and try again.';
    var msgLow  = (msg || '').toLowerCase();

    if (msgLow.includes('supported') || msgLow.includes('not currently')) {
        icon = '\u26A0'; title = _tr.cryptoTitle_NotAvail || 'Currency Not Available';
        color = '#ffc107'; bgColor = 'rgba(255,193,7,0.07)'; bdColor = 'rgba(255,193,7,0.25)';
        hint  = _tr.cryptoHint_TryOther || 'This currency is temporarily unavailable. Please select a different one.';
    } else if (msgLow.includes('maximum') || msgLow.includes('limit') || msgLow.includes('wait')) {
        icon = '\u23F3'; title = _tr.cryptoTitle_LimitReached || 'Maximum Orders Reached';
        color = '#ff9500'; bgColor = 'rgba(255,149,0,0.07)'; bdColor = 'rgba(255,149,0,0.25)';
        hint  = _tr.cryptoHint_WaitExpiry || 'Please wait for existing orders to expire or select a different currency.';
    } else if (msgLow.includes('minimum')) {
        icon = '\u26A0'; title = _tr.cryptoTitle_MinAmount || 'Amount Below Minimum';
        color = '#ffc107'; bgColor = 'rgba(255,193,7,0.07)'; bdColor = 'rgba(255,193,7,0.25)';
        hint  = '';
    } else if (msgLow.includes('network') || msgLow.includes('connection')) {
        icon = '\u21BB'; title = _tr.cryptoTitle_ConnErr || 'Connection Error';
        color = '#6c757d'; bgColor = 'rgba(108,117,125,0.07)'; bdColor = 'rgba(108,117,125,0.25)';
        hint  = _tr.cryptoHint_TryAgain || 'Please check your internet connection and try again.';
    } else if (msgLow.includes('expired')) {
        icon = '\u23F1'; title = _tr.cryptoTitle_Expired || 'Order Expired';
        color = '#ff9500'; bgColor = 'rgba(255,149,0,0.07)'; bdColor = 'rgba(255,149,0,0.25)';
        hint  = _tr.cryptoHint_NewAddress || 'Select a cryptocurrency above to generate a new payment address.';
    } else if (msgLow.includes('confirmed')) {
        icon = '\u2713'; title = _tr.cryptoTitle_Confirmed || 'Already Confirmed';
        color = '#28a745'; bgColor = 'rgba(40,167,69,0.07)'; bdColor = 'rgba(40,167,69,0.25)';
        hint  = '';
    }

    var errEl = document.createElement('div');
    errEl.id  = 'crypto-error-msg';
    errEl.style.cssText =
        'background:' + bgColor + ';border:1px solid ' + bdColor + ';border-radius:4px;' +
        'padding:14px 16px;margin-top:14px;display:flex;align-items:flex-start;gap:12px;' +
        'animation:fadeIn .3s ease;';
    errEl.innerHTML =
        '<span style="font-size:1.05rem;color:' + color + ';flex-shrink:0;line-height:1.4;margin-top:1px;">' + icon + '</span>' +
        '<div>' +
            '<div style="font-size:0.8rem;font-weight:600;color:' + color + ';letter-spacing:0.06em;' +
                 'text-transform:uppercase;font-family:Oswald,Arial,sans-serif;margin-bottom:4px;">' + title + '</div>' +
            '<div style="font-size:0.79rem;color:#aaa;font-weight:300;line-height:1.55;">' +
                (msg || (_tr.cryptoErr_Default || 'An unexpected error occurred.')) +
            '</div>' +
            (hint
                ? '<div style="font-size:0.71rem;color:#555;margin-top:5px;letter-spacing:0.03em;">' + hint + '</div>'
                : '') +
        '</div>';

    var panel = document.getElementById('panel-crypto');
    if (panel) panel.querySelector('.panel-inner').appendChild(errEl);
}

function showPaymentConfirmed() {
    if (document.getElementById('crypto-confirmed')) return;

    var panel = document.getElementById('panel-crypto');
    if (!panel) return;

    var refEl  = document.querySelector('.sum-ref-val');
    var refNum = refEl ? refEl.textContent.trim() : '';

    var box = document.createElement('div');
    box.id  = 'crypto-confirmed';
    box.className = 'payment-confirmed-box';
    box.innerHTML =
        '<div class="payment-confirmed-icon">' +
            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" aria-hidden="true">' +
                '<polyline points="20 6 9 17 4 12"/>' +
            '</svg>' +
        '</div>' +
        '<div class="payment-confirmed-title">' + (_tr.confirmedTitle || 'Payment Confirmed!') + '</div>' +
        '<div class="payment-confirmed-sub">' +
            (_tr.confirmedSub || 'Your cryptocurrency payment has been successfully verified. Your registration is now confirmed.') +
        '</div>' +
        (refNum ? '<div class="payment-confirmed-ref">' + refNum + '</div>' : '') +
        '<div class="payment-confirmed-redirect">' +
            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">' +
                '<path d="M12 2a10 10 0 1 1 0 20 10 10 0 0 1 0-20z" stroke-dasharray="31.4" stroke-dashoffset="0"/>' +
            '</svg>' +
            (_tr.confirmedRedirect || 'Redirecting to your profile...') +
        '</div>';

    panel.querySelector('.panel-inner').appendChild(box);

    var walletBox = document.querySelector('.wallet-box');
    var qrBox     = document.querySelector('.qr-box');
    if (walletBox) walletBox.style.opacity = '0.3';
    if (qrBox)     qrBox.style.opacity     = '0.3';
}

function showExpiryCountdown(expiresAt) {
    stopExpiryTimer(); 

    var existing = document.getElementById('crypto-expiry');
    if (existing) existing.remove();

    var timerEl = document.createElement('p');
    timerEl.id  = 'crypto-expiry';
    timerEl.style.cssText = 'font-size:0.78rem;color:#888;text-align:center;margin-top:8px;font-family:"Oswald",Arial,sans-serif;';

    var panel = document.getElementById('panel-crypto');
    if (panel) panel.querySelector('.panel-inner').appendChild(timerEl);

    var expiry = new Date(expiresAt.replace(' ', 'T').replace(/Z?$/, 'Z'));

    cryptoState.expiryTimer = setInterval(function () {
        var diff = Math.floor((expiry - Date.now()) / 1000);
        if (diff <= 0) {
            stopExpiryTimer();
            timerEl.textContent = 'Order expired.';
            timerEl.style.color = '#dc3545';
            return;
        }
        var m = Math.floor(diff / 60), s = diff % 60;
        timerEl.textContent = 'Expires in ' + m + ':' + (s < 10 ? '0' : '') + s;
        timerEl.style.color = diff < 60 ? '#dc3545' : (diff < 120 ? '#ff9500' : '#888');
    }, 1000);
}

// ══════════════════════════════════════════════════════════════════
// COPY HELPERS
// ══════════════════════════════════════════════════════════════════
function copyWallet() {
    var addrEl = document.getElementById('walletAddr');
    var btnTxt = document.getElementById('copyWalletTxt');
    var btn    = document.getElementById('copyWalletBtn');
    var addr   = addrEl ? addrEl.textContent.trim() : '';
    if (!addr || addr === 'Loading...') return;

    var onCopied = function () {
        if (btnTxt) btnTxt.textContent = _tr.cryptoCopied || 'Copied!';
        if (btn) btn.style.color = '#28a745';
        setTimeout(function () {
            if (btnTxt) btnTxt.textContent = _tr.cryptoCopyAddress || 'Copy address';
            if (btn) btn.style.color = '';
        }, 2000);
    };

    if (navigator.clipboard) {
        navigator.clipboard.writeText(addr).then(onCopied).catch(function () { fbCopy(addr, onCopied); });
    } else {
        fbCopy(addr, onCopied);
    }
}

function copyText(text, btn) {
    var onCopied = function () {
        if (!btn) return;
        var orig = btn.innerHTML;
        btn.innerHTML  =
            '<svg viewBox="0 0 24 24" fill="none" stroke="#28a745" stroke-width="2.5">' +
            '<polyline points="20 6 9 17 4 12"/></svg>';
        btn.style.color = '#28a745';
        setTimeout(function () { btn.innerHTML = orig; btn.style.color = ''; }, 2000);
    };
    if (navigator.clipboard) {
        navigator.clipboard.writeText(text).then(onCopied).catch(function () { fbCopy(text, onCopied); });
    } else {
        fbCopy(text, onCopied);
    }
}

function fbCopy(text, cb) {
    var ta = document.createElement('textarea');
    ta.value = text;
    ta.style.cssText = 'position:fixed;top:-9999px;left:-9999px;';
    document.body.appendChild(ta);
    ta.select();
    try { document.execCommand('copy'); if (cb) cb(); } catch (e) {}
    document.body.removeChild(ta);
}

// ══════════════════════════════════════════════════════════════════
// ACTIVE ORDER ПРИ PAGE LOAD
// ══════════════════════════════════════════════════════════════════
function loadActiveCryptoOrder() {
    fetch('/api/crypto/active-order')
        .then(function (res) {
            if (res.status === 401 || res.status === 403) return null;
            return res.json();
        })
        .then(function (data) {
            if (!data) return;

            if (!data.success) {
                if (data.error === 'limit_reached') lockCryptoButtons(null);
                return;
            }

            cryptoState.orderId = data.orderId;

            document.querySelectorAll('.crypto-item').forEach(function (btn) {
                var oc = btn.getAttribute('onclick') || '';
                if (oc.indexOf("'" + data.currency + "'") !== -1) btn.classList.add('selected');
            });

            var prompt = document.getElementById('crypto-select-prompt');
            var grid   = document.getElementById('crypto-details-grid');
            if (prompt) prompt.style.display = 'none';
            if (grid)   grid.style.display   = 'grid';

            var coinNames = { BTC: 'Bitcoin', ETH: 'Ethereum', USDC: 'USD Coin', EURC: 'Euro Coin' };
            var coinEl = document.getElementById('cryptoCoinName');
            var netEl  = document.getElementById('cryptoNetwork');
            if (coinEl) coinEl.textContent = coinNames[data.currency] || data.currency;
            if (netEl)  netEl.textContent  = data.network || '';

            var addrEl   = document.getElementById('walletAddr');
            var amountEl = document.getElementById('cryptoAmount');
            if (addrEl)   addrEl.textContent   = data.cryptoAddress || '';
            if (amountEl) amountEl.textContent = data.amount        || '';

            var qrImg = document.getElementById('cryptoQR');
            var qrFb  = document.getElementById('qr-fallback');
            if (data.qrCode && qrImg) {
                qrImg.src = data.qrCode; qrImg.style.display = 'block';
                if (qrFb) qrFb.style.display = 'none';
            } else if (qrImg) {
                qrImg.style.display = 'none';
                if (qrFb) qrFb.style.display = 'flex';
            }

            if (data.expiresAt) showExpiryCountdown(data.expiresAt);
            startPolling(data.orderId);
        })
        .catch(function () {});
}

window.addEventListener('beforeunload', function () {
    stopPolling();
    stopExpiryTimer();
});