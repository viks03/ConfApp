document.addEventListener("DOMContentLoaded", function () {
    var textarea            = document.getElementById('emailsTextarea');
    var sendBtn              = document.getElementById('sendBtn');
    var stopBtn              = document.getElementById('stopBtn');
    var stopBtnText          = document.getElementById('stopBtnText');
    var emailStats           = document.getElementById('emailStats');
    var statValid            = document.getElementById('statValid');
    var statInvalid          = document.getElementById('statInvalid');
    var statDup              = document.getElementById('statDup');
    var subjectInput         = document.getElementById('subjectInput');
    var subjectCounter       = document.getElementById('subjectCounter');
    var htmlInput            = document.getElementById('htmlTemplateInput');
    var templateSizeHint     = document.getElementById('templateSizeHint');
    var previewFrame         = document.getElementById('previewFrame');
    var fileInput            = document.getElementById('templateFileInput');
    var dropZone             = document.getElementById('fileDropZone');
    var sourceBadge          = document.getElementById('templateSourceBadge');
    var logBox               = document.getElementById('logBox');
    var progressWrap         = document.getElementById('progressWrap');
    var progressBar          = document.getElementById('progressBar');
    var progressLabel        = document.getElementById('progressLabel');
    var summaryBox           = document.getElementById('summaryBox');
    var sendBtnText          = document.getElementById('sendBtnText');
    var sendBtnIcon          = document.getElementById('sendBtnIcon');
    var historyEmptyCard     = document.getElementById('historyEmptyCard');
    var historyEmpty         = document.getElementById('historyEmpty');
    var successGroupCard     = document.getElementById('successGroupCard');
    var successGroupCount    = document.getElementById('successGroupCount');
    var successGroupEmpty    = document.getElementById('successGroupEmpty');
    var successGroupWrap     = document.getElementById('successGroupWrap');
    var successGroupBody     = document.getElementById('successGroupBody');
    var failedGroupCard      = document.getElementById('failedGroupCard');
    var failedGroupCount     = document.getElementById('failedGroupCount');
    var failedGroupEmpty     = document.getElementById('failedGroupEmpty');
    var failedGroupWrap      = document.getElementById('failedGroupWrap');
    var failedGroupBody      = document.getElementById('failedGroupBody');
    var historyCount         = document.getElementById('historyCount');
    var historyFilterBtns    = document.querySelectorAll('.history-filter-btn');
    var refreshHistoryBtn    = document.getElementById('refreshHistoryBtn');
    var historyTabBadge      = document.getElementById('historyTabBadge');
    var historyTotalNum      = document.getElementById('historyTotalNum');
    var historyOkNum         = document.getElementById('historyOkNum');
    var historyErrNum        = document.getElementById('historyErrNum');

    var MAX_SUBJECT_LENGTH   = 200;
    var MAX_TEMPLATE_BYTES   = 750 * 1024;
    var ACTIVE_TAB_KEY       = 'sendInvActiveTab';

    // ── Inline SVG icons (swapped into spans so we never wipe sibling text) ──
    var ICON_SEND =
        '<svg class="inv-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
        '<line x1="22" y1="2" x2="11" y2="13"></line><polygon points="22 2 15 22 11 13 2 9 22 2"></polygon></svg>';
    var ICON_SPINNER =
        '<svg class="inv-icon spin" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round">' +
        '<path d="M21 12a9 9 0 1 1-6.219-8.56"></path></svg>';

    // ── Tabs: Compose / History ─────────────────────────────────────────────
    // Remembers the active tab across full page reloads (localStorage — same
    // approach already used elsewhere in the admin panel for tab memory).
    var tabButtons = document.querySelectorAll('.inv-tab');
    var tabPanes   = document.querySelectorAll('.inv-tab-pane');

    function activateTab(target) {
        tabButtons.forEach(function (b) { b.classList.toggle('active', b.getAttribute('data-tab') === target); });
        tabPanes.forEach(function (p) { p.classList.toggle('active', p.id === 'tab-' + target); });
    }

    tabButtons.forEach(function (btn) {
        btn.addEventListener('click', function () {
            var target = btn.getAttribute('data-tab');
            activateTab(target);
            try { localStorage.setItem(ACTIVE_TAB_KEY, target); } catch (e) { /* ignore (private mode etc.) */ }
            if (target === 'history') fetchHistory();
        });
    });

    (function restoreActiveTab() {
        var saved = null;
        try { saved = localStorage.getItem(ACTIVE_TAB_KEY); } catch (e) { /* ignore */ }
        if (saved === 'history' || saved === 'compose') activateTab(saved);
    })();

    // ── UTF-8-safe base64 encode ────────────────────────────────────────────
    // btoa() alone breaks on non-Latin1 characters, so we route through
    // encodeURIComponent/unescape first — the standard trick for this.
    // Base64 is also why the request now sails through cleanly behind
    // Cloudflare (see the comment further down, near the fetch call).
    function utf8ToBase64(str) {
        return btoa(unescape(encodeURIComponent(str)));
    }

    // RFC4122-ish v4 UUID, with crypto.randomUUID() used when available
    // (all current browsers support it, this is just a safety net).
    function generateBatchId() {
        if (window.crypto && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    // ── Subject counter (mirrors the 200-char server-side limit) ────────────
    function updateSubjectCounter() {
        if (!subjectInput || !subjectCounter) return;
        var len = subjectInput.value.length;
        subjectCounter.textContent = len + ' / ' + MAX_SUBJECT_LENGTH + ' characters';
        subjectCounter.style.color = len > MAX_SUBJECT_LENGTH ? '#dc2626' : '';
    }
    if (subjectInput) {
        subjectInput.addEventListener('input', updateSubjectCounter);
        updateSubjectCounter();
    }

    // ── Template size hint (mirrors the 750KB server-side limit) ────────────
    function updateTemplateSizeHint() {
        if (!templateSizeHint || !htmlInput) return;
        var bytes = new Blob([htmlInput.value]).size;
        var kb = Math.round(bytes / 1024);
        var maxKb = Math.round(MAX_TEMPLATE_BYTES / 1024);
        templateSizeHint.textContent = kb + ' KB (max ' + maxKb + ' KB)';
        templateSizeHint.style.color = bytes > MAX_TEMPLATE_BYTES ? '#dc2626' : '';
    }

    // ── Delay slider ─────────────────────────────────────────────────────────
    var delaySlider = document.getElementById('delaySlider');
    var delayLabel  = document.getElementById('delayLabel');

    function updateDelayFill() {
        if (!delaySlider) return;
        var min = parseFloat(delaySlider.min), max = parseFloat(delaySlider.max), val = parseFloat(delaySlider.value);
        var pct = max > min ? ((val - min) / (max - min)) * 100 : 0;
        delaySlider.style.setProperty('--fill', pct + '%');
    }

    if (delaySlider && delayLabel) {
        delaySlider.addEventListener('input', function () {
            delayLabel.textContent = (delaySlider.value / 1000).toFixed(1) + 's';
            updateDelayFill();
        });
        updateDelayFill(); // paint the correct fill for the initial value on load
    }

    // ── Template source badge + live preview ─────────────────────────────────
    function setTemplateSource(label, isFile) {
        if (!sourceBadge) return;
        sourceBadge.textContent = label;
        sourceBadge.classList.toggle('is-file', !!isFile);
    }

    // The preview <iframe> renders via srcdoc, which is a fully separate HTML
    // document — it does NOT inherit the page's CSS/font. Reading the real
    // computed font-family off the page (rather than hardcoding a guess like
    // "sans-serif") keeps this placeholder visually identical to the rest of
    // the UI even if the site's font stack ever changes.
    var pageFontFamily = getComputedStyle(document.body).fontFamily || 'sans-serif';

    function noTemplateHtml() {
        // NOTE: font-family values from getComputedStyle often contain embedded
        // double quotes (e.g. -apple-system, BlinkMacSystemFont, "Segoe UI", ...).
        // Putting that straight into a style="..." HTML attribute breaks the
        // attribute at the first embedded quote, silently dropping the rule.
        // A <style> block has no such conflict — quotes there are just text.
        return '<style>body{margin:0;} .no-tpl{font-family:' + pageFontFamily +
            ';padding:24px;color:#94a3b8;font-size:13px;}</style>' +
            '<div class="no-tpl">No template loaded yet — paste HTML or upload an .html file on the left.</div>';
    }

    // No server-side default template anymore — the field starts empty and
    // the preview stays blank until the admin pastes or uploads an .html file.
    if (htmlInput && previewFrame) {
        previewFrame.srcdoc = noTemplateHtml();

        htmlInput.addEventListener('input', function () {
            if (htmlInput.value.trim() === '') {
                previewFrame.srcdoc = noTemplateHtml();
                setTemplateSource('No template loaded', false);
            } else {
                previewFrame.srcdoc = htmlInput.value;
                setTemplateSource('Manually edited', false);
            }
            updateTemplateSizeHint();
        });
        updateTemplateSizeHint();
    }

    // ── Upload an .html file (click or drag & drop) ─────────────────────────
    // Everything here is purely client-side (FileReader) — the file itself
    // is never uploaded on its own, it just fills the textarea. The actual
    // transfer later goes through the same base64-encoded /handler=SendOne.
    function loadTemplateFile(file) {
        if (!file) return;
        var name = file.name || '';
        var looksHtml = /\.html?$/i.test(name) || file.type === 'text/html';
        if (!looksHtml) {
            alert('Please upload an .html file.');
            return;
        }
        if (file.size > MAX_TEMPLATE_BYTES) {
            alert('That file is ' + Math.round(file.size / 1024) + 'KB — the maximum allowed template size is ' + Math.round(MAX_TEMPLATE_BYTES / 1024) + 'KB.');
            return;
        }
        var reader = new FileReader();
        reader.onload = function (e) {
            htmlInput.value = e.target.result;
            previewFrame.srcdoc = htmlInput.value;
            setTemplateSource(name, true);
            updateTemplateSizeHint();
        };
        reader.onerror = function () {
            alert('Error reading the file.');
        };
        reader.readAsText(file, 'UTF-8');
    }

    if (fileInput) {
        fileInput.addEventListener('change', function () {
            loadTemplateFile(fileInput.files && fileInput.files[0]);
            fileInput.value = ''; // so 'change' fires again if the same file is picked twice
        });
    }

    if (dropZone) {
        ['dragover', 'dragenter'].forEach(function (evt) {
            dropZone.addEventListener(evt, function (e) {
                e.preventDefault();
                dropZone.classList.add('is-dragover');
            });
        });
        ['dragleave', 'drop'].forEach(function (evt) {
            dropZone.addEventListener(evt, function (e) {
                e.preventDefault();
                dropZone.classList.remove('is-dragover');
            });
        });
        dropZone.addEventListener('drop', function (e) {
            e.preventDefault();
            var f = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
            if (f) loadTemplateFile(f);
        });
    }

    // ── Parse / validate recipients ──────────────────────────────────────────
    function checkEmail(emailStr) {
        emailStr = emailStr.trim();
        var atSymbol = String.fromCharCode(64);
        var atIndex = emailStr.indexOf(atSymbol);
        if (atIndex < 1) return false;
        var localPart = emailStr.slice(0, atIndex);
        var domainPart = emailStr.slice(atIndex + 1);
        return localPart.length > 0 && domainPart.length >= 3 && domainPart.indexOf('.') !== -1;
    }

    var validEmailsArray = [];

    function handleInput() {
        var rawText = textarea.value.trim();

        if (rawText.length === 0) {
            if (emailStats) emailStats.style.display = 'none';
            sendBtn.setAttribute('disabled', 'disabled');
            sendBtn.disabled = true;
            return;
        }

        var lines = rawText.split('\n');
        validEmailsArray = [];
        var invalidCount = 0;
        var dupCount = 0;
        var seen = {};

        for (var i = 0; i < lines.length; i++) {
            var line = lines[i].trim();
            if (line === '' || line.indexOf('#') === 0) continue;

            var emailOnly = line;
            var nameOnly = "";
            if (line.indexOf('|') !== -1) {
                var parts = line.split('|');
                emailOnly = parts[0].trim();
                nameOnly = parts[1] ? parts[1].trim() : "";
            }

            if (!checkEmail(emailOnly)) {
                invalidCount++;
                continue;
            }

            var lowerEmail = emailOnly.toLowerCase();
            if (seen[lowerEmail]) {
                dupCount++;
                continue;
            }

            seen[lowerEmail] = true;
            validEmailsArray.push({ email: emailOnly, name: nameOnly });
        }

        if (statValid) statValid.textContent = validEmailsArray.length + ' valid';
        if (statInvalid) statInvalid.textContent = invalidCount + ' invalid';
        if (statDup) statDup.textContent = dupCount + ' duplicates';
        if (emailStats) emailStats.style.display = 'flex';

        if (validEmailsArray.length > 0) {
            sendBtn.removeAttribute('disabled');
            sendBtn.disabled = false;
        } else {
            sendBtn.setAttribute('disabled', 'disabled');
            sendBtn.disabled = true;
        }
    }

    textarea.addEventListener('input', handleInput);
    textarea.addEventListener('change', handleInput);
    textarea.addEventListener('keyup', handleInput);
    textarea.addEventListener('paste', function () {
        setTimeout(handleInput, 50);
    });

    handleInput();

    // ── Sending history — real data, persisted server-side in InvitationSendLog ─
    var historyRows = []; // { email, name, subject, date, time, status: 'ok'|'err', category, message }
    var HISTORY_FILTER_KEY = 'invHistoryFilter';

    // Default is "Successful" — but if the admin already picked a filter on a
    // previous visit, honor that instead. localStorage can be unavailable
    // (private browsing, locked-down browser settings) — never let that crash the page.
    var activeFilter = 'ok';
    try {
        var savedFilter = localStorage.getItem(HISTORY_FILTER_KEY);
        if (savedFilter === 'ok' || savedFilter === 'err' || savedFilter === 'all') {
            activeFilter = savedFilter;
        }
    } catch (e) { /* localStorage unavailable — keep the 'ok' default */ }

    historyFilterBtns.forEach(function (b) {
        b.classList.toggle('active', b.getAttribute('data-filter') === activeFilter);
    });

    function escapeHtml(str) {
        var div = document.createElement('div');
        div.textContent = str == null ? '' : String(str);
        return div.innerHTML;
    }

    function formatTrackingDate(isoUtc) {
        if (!isoUtc) return '';
        var d = new Date(isoUtc + (isoUtc.endsWith('Z') ? '' : 'Z'));
        return d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short' }) + ' ' +
               d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
    }

    function renderSuccessRow(row) {
        if (!successGroupBody) return;
        var tr = document.createElement('tr');

        var engagementCell;
        if (row.clickedAt) {
            engagementCell = '<div class="engagement-cell">' +
                '<span class="inv-status-pill inv-status-clicked">Clicked</span>' +
                '<span class="engagement-meta">' + row.clickedDisplay + (row.clickCount > 1 ? ' · ×' + row.clickCount : '') + '</span>' +
                '</div>';
        } else if (row.openedAt) {
            engagementCell = '<div class="engagement-cell">' +
                '<span class="inv-status-pill inv-status-opened">Opened</span>' +
                '<span class="engagement-meta">' + row.openedDisplay + (row.openCount > 1 ? ' · ×' + row.openCount : '') + '</span>' +
                '</div>';
        } else {
            engagementCell = '<span class="inv-badge-muted">Not opened yet</span>';
        }

        var detailsCell = (row.id != null && row.hasSentBody)
            ? '<a class="history-download-link" href="?handler=DownloadSentEmail&id=' + row.id + '" target="_blank">' +
              '<svg class="inv-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="7 10 12 15 17 10"></polyline><line x1="12" y1="15" x2="12" y2="3"></line></svg>' +
              'Download</a>'
            : '<span class="inv-badge-muted">No copy saved</span>';

        tr.innerHTML =
            '<td>' + escapeHtml(row.email) + '</td>' +
            '<td>' + (row.name ? escapeHtml(row.name) : '<span style="color:#94a3b8;">—</span>') + '</td>' +
            '<td style="color:#64748b;font-size:12px;">' + escapeHtml(row.subject) + '</td>' +
            '<td style="color:#64748b;font-size:11.5px;white-space:nowrap;">' + row.date + '</td>' +
            '<td style="color:#94a3b8;font-size:11.5px;white-space:nowrap;">' + row.time + '</td>' +
            '<td style="font-size:11.5px;">' + engagementCell + '</td>' +
            '<td style="font-size:11.5px;">' + detailsCell + '</td>';

        successGroupBody.appendChild(tr);
    }

    function renderFailedRow(row) {
        if (!failedGroupBody) return;
        var tr = document.createElement('tr');

        var detailsCell = '<div class="details-line">' +
            '<span class="inv-badge-tag">' + (row.category ? escapeHtml(row.category) : 'Error') + '</span>' +
            '<span style="color:#991b1b;">' + (row.message ? escapeHtml(row.message) : 'No details available.') + '</span>' +
            '</div>';

        tr.innerHTML =
            '<td>' + escapeHtml(row.email) + '</td>' +
            '<td>' + (row.name ? escapeHtml(row.name) : '<span style="color:#94a3b8;">—</span>') + '</td>' +
            '<td style="color:#64748b;font-size:12px;">' + escapeHtml(row.subject) + '</td>' +
            '<td style="color:#64748b;font-size:11.5px;white-space:nowrap;">' + row.date + '</td>' +
            '<td style="color:#94a3b8;font-size:11.5px;white-space:nowrap;">' + row.time + '</td>' +
            '<td style="font-size:11.5px;">' + detailsCell + '</td>';

        failedGroupBody.appendChild(tr);
    }

    // Which card(s) show is purely a function of the active filter — "All"
    // shows BOTH cards stacked (Successful first, per the button order), each
    // with only the columns that make sense for it, instead of forcing two
    // very differently-shaped rows into one shared table.
    function updateHistoryChrome() {
        var successRows = historyRows.filter(function (r) { return r.status === 'ok'; });
        var failedRows  = historyRows.filter(function (r) { return r.status === 'err'; });

        if (historyCount) {
            var stamp = new Date().toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
            historyCount.textContent = historyRows.length + (historyRows.length === 1 ? ' entry total' : ' entries total') +
                ' · updated ' + stamp;
        }
        if (historyTotalNum) historyTotalNum.textContent = historyRows.length;
        if (historyOkNum) historyOkNum.textContent = successRows.length;
        if (historyErrNum) historyErrNum.textContent = failedRows.length;
        if (historyTabBadge) {
            if (historyRows.length > 0) {
                historyTabBadge.textContent = historyRows.length > 99 ? '99+' : historyRows.length;
                historyTabBadge.style.display = 'inline-flex';
            } else {
                historyTabBadge.style.display = 'none';
            }
        }

        if (successGroupCount) successGroupCount.textContent = successRows.length + (successRows.length === 1 ? ' entry' : ' entries');
        if (failedGroupCount) failedGroupCount.textContent = failedRows.length + (failedRows.length === 1 ? ' entry' : ' entries');

        if (successGroupEmpty) successGroupEmpty.style.display = successRows.length === 0 ? 'block' : 'none';
        if (successGroupWrap) successGroupWrap.style.display = successRows.length === 0 ? 'none' : 'block';
        if (failedGroupEmpty) failedGroupEmpty.style.display = failedRows.length === 0 ? 'block' : 'none';
        if (failedGroupWrap) failedGroupWrap.style.display = failedRows.length === 0 ? 'none' : 'block';

        var hasAnyHistory = historyRows.length > 0;
        if (historyEmptyCard) historyEmptyCard.style.display = hasAnyHistory ? 'none' : 'block';
        if (successGroupCard) successGroupCard.style.display = (hasAnyHistory && (activeFilter === 'ok' || activeFilter === 'all')) ? '' : 'none';
        if (failedGroupCard) failedGroupCard.style.display = (hasAnyHistory && (activeFilter === 'err' || activeFilter === 'all')) ? '' : 'none';

        // Brief visible pulse on whichever table(s) are currently on screen —
        // a spinning refresh icon alone is easy to miss; this makes "the
        // content actually refreshed" obvious even when nothing changed.
        [successGroupWrap, failedGroupWrap].forEach(function (wrap) {
            if (!wrap) return;
            wrap.classList.remove('just-updated');
            void wrap.offsetWidth; // force reflow so the animation can restart
            wrap.classList.add('just-updated');
        });
    }

    function renderAllHistory() {
        if (successGroupBody) successGroupBody.innerHTML = '';
        if (failedGroupBody) failedGroupBody.innerHTML = '';
        historyRows.forEach(function (row) {
            if (row.status === 'ok') renderSuccessRow(row);
            else renderFailedRow(row);
        });
        updateHistoryChrome();
    }

    // Pulls the real log from the database (Areas/Admin OnGetHistoryAsync).
    // Called on page load, whenever the History tab is opened, after a send
    // batch finishes, and on manual Refresh.
    async function fetchHistory() {
        try {
            var res = await fetch('?handler=History');
            if (!res.ok) return;
            var data = await res.json();
            historyRows = data.map(function (e) {
                var d = new Date(e.sentAt + (e.sentAt.endsWith('Z') ? '' : 'Z')); // sentAt is UTC
                return {
                    id: e.id,
                    email: e.email,
                    name: e.recipientName,
                    subject: e.subject,
                    date: d.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' }),
                    time: d.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit', second: '2-digit' }),
                    status: e.success ? 'ok' : 'err',
                    category: e.errorCategory,
                    message: e.errorMessage,
                    hasSentBody: e.hasSentBody,
                    openedAt: e.openedAt,
                    openCount: e.openCount,
                    openedDisplay: formatTrackingDate(e.openedAt),
                    clickedAt: e.clickedAt,
                    clickCount: e.clickCount,
                    clickedDisplay: formatTrackingDate(e.clickedAt)
                };
            });
            renderAllHistory();
        } catch (err) {
            console.log('Could not load sending history:', err);
        }
    }

    historyFilterBtns.forEach(function (btn) {
        btn.addEventListener('click', function () {
            activeFilter = btn.getAttribute('data-filter');
            historyFilterBtns.forEach(function (b) { b.classList.toggle('active', b === btn); });
            try { localStorage.setItem(HISTORY_FILTER_KEY, activeFilter); } catch (e) { /* ignore — not fatal */ }
            updateHistoryChrome();
        });
    });

    if (refreshHistoryBtn) {
        refreshHistoryBtn.addEventListener('click', function () {
            if (refreshHistoryBtn.disabled) return; // already refreshing — ignore double-clicks
            refreshHistoryBtn.disabled = true;
            var icon = refreshHistoryBtn.querySelector('svg');
            if (icon) icon.classList.add('spin');
            fetchHistory().finally(function () {
                refreshHistoryBtn.disabled = false;
                if (icon) icon.classList.remove('spin');
            });
        });
    }

    var clearAllHistoryBtn = document.getElementById('clearAllHistoryBtn');
    if (clearAllHistoryBtn) {
        clearAllHistoryBtn.addEventListener('click', async function () {
            var count = historyRows.length;
            var warning = count > 0
                ? 'This will PERMANENTLY delete all ' + count + ' invitation history record(s) from the database, ' +
                  'including any saved copies of sent invitations. This cannot be undone.\n\nContinue?'
                : 'This will permanently delete all invitation history from the database. This cannot be undone.\n\nContinue?';
            if (!confirm(warning)) return;

            clearAllHistoryBtn.disabled = true;
            try {
                var tokenElement = document.querySelector('input[name="__RequestVerificationToken"]');
                var formData = new FormData();
                if (tokenElement) formData.append('__RequestVerificationToken', tokenElement.value);

                var res = await fetch('?handler=ClearHistory', { method: 'POST', body: formData });
                var data = await res.json();
                if (data.success) {
                    fetchHistory();
                } else {
                    alert('Could not clear history: ' + (data.message || 'unknown error'));
                }
            } catch (err) {
                alert('Network error while clearing history.');
            } finally {
                clearAllHistoryBtn.disabled = false;
            }
        });
    }

    fetchHistory(); // initial load, so the tab badge count is correct even before opening it

    // ── Sending ─────────────────────────────────────────────────────────────
    function logLine(text, type) {
        var p = document.createElement('p');
        p.className = 'log-line log-' + (type || 'info');
        p.textContent = text;
        logBox.appendChild(p);
        logBox.scrollTop = logBox.scrollHeight;
    }

    function setProgress(done, total) {
        var pct = total > 0 ? Math.round((done / total) * 100) : 0;
        progressBar.style.width = pct + '%';
        progressLabel.textContent = done + ' / ' + total + ' (' + pct + '%)';
    }

    var isSending = false;
    var stopRequested = false;
    var lastFailedRecipients = [];

    async function sendBatch(recipients) {
        if (isSending || recipients.length === 0) return;

        var subject = subjectInput.value.trim();
        if (!subject) {
            alert('Please fill in the email subject!');
            return;
        }
        if (subject.length > MAX_SUBJECT_LENGTH) {
            alert('Subject is too long (' + subject.length + ' characters, max ' + MAX_SUBJECT_LENGTH + ').');
            return;
        }

        var htmlTemplateValue = htmlInput.value.trim();
        if (!htmlTemplateValue) {
            alert('Please enter the HTML template code!');
            return;
        }
        if (new Blob([htmlTemplateValue]).size > MAX_TEMPLATE_BYTES) {
            alert('The template is too large (max ' + Math.round(MAX_TEMPLATE_BYTES / 1024) + 'KB). Trim it down or remove embedded images.');
            return;
        }

        var delay = delaySlider ? parseInt(delaySlider.value) : 1200;
        var tokenElement = document.querySelector('input[name="__RequestVerificationToken"]');
        var tokenValue = tokenElement ? tokenElement.value : '';
        var batchId = generateBatchId();

        isSending = true;
        stopRequested = false;
        lastFailedRecipients = [];

        sendBtn.setAttribute('disabled', 'disabled');
        sendBtn.disabled = true;
        sendBtnText.textContent = 'Sending...';
        sendBtnIcon.innerHTML = ICON_SPINNER;
        if (stopBtn) stopBtn.style.display = 'inline-flex';

        logBox.innerHTML = '';
        logBox.classList.add('show');
        progressWrap.classList.add('show');
        summaryBox.className = 'inv-summary';
        summaryBox.innerHTML = '';

        setProgress(0, recipients.length);
        logLine('Starting to send ' + recipients.length + ' invitations...', 'info');
        logLine('Subject: "' + subject + '"', 'info');
        logLine('Batch ID: ' + batchId, 'info');
        logLine('-------------------------------------------------------', 'info');

        var sent = 0, failed = 0;

        for (var i = 0; i < recipients.length; i++) {
            if (stopRequested) {
                logLine('Stopped by user at ' + i + ' / ' + recipients.length + '.', 'warn');
                break;
            }

            var recipient = recipients[i];
            var num = '[' + (i + 1) + '/' + recipients.length + ']';

            try {
                var formData = new FormData();
                if (tokenValue) formData.append('__RequestVerificationToken', tokenValue);
                formData.append('email', recipient.email);
                formData.append('name', recipient.name);
                formData.append('subject', subject);
                formData.append('batchId', batchId);
                // Base64-encode the raw HTML template before sending — on production
                // (behind Cloudflare/a reverse proxy), raw <html>/<style> in a POST
                // body is very likely to get flagged by a WAF/security filter as an
                // XSS attempt, and the request never reaches the backend at all.
                // A base64 blob doesn't look like anything recognizable, sidestepping
                // that entirely. Decoded back server-side in OnPostSendOneAsync.
                formData.append('htmlTemplateBase64', utf8ToBase64(htmlTemplateValue));

                var res = await fetch('?handler=SendOne', { method: 'POST', body: formData });
                var contentType = res.headers.get('content-type') || '';
                if (contentType.indexOf('application/json') === -1) {
                    // We got A response, but not JSON — the request almost certainly
                    // never reached our backend code at all. Most likely cause: a
                    // security filter (Cloudflare/WAF) or reverse proxy intercepted
                    // it and returned its own HTML block/challenge page instead.
                    // NOTE: because the backend never ran, this failure can't be
                    // written to the database log — it only shows up here, live.
                    throw new Error('NON_JSON_RESPONSE:' + res.status);
                }
                var data = await res.json();

                if (data.success) {
                    sent++;
                    logLine(num + ' OK ' + recipient.email, 'ok');
                } else {
                    failed++;
                    var catLabel = data.category ? '[' + data.category + '] ' : '';
                    logLine(num + ' FAIL ' + recipient.email + ' - ' + catLabel + data.message, 'err');
                    lastFailedRecipients.push(recipient);
                }
            } catch (err) {
                failed++;
                var msg;
                if (err.message && err.message.indexOf('NON_JSON_RESPONSE:') === 0) {
                    var status = err.message.split(':')[1];
                    msg = '[Blocked] Request never reached the server (HTTP ' + status + ') — likely intercepted by a security filter (Cloudflare/WAF) or reverse proxy.';
                } else {
                    msg = '[Network] Could not reach the server at all — check your internet connection.';
                }
                logLine(num + ' FAIL ' + recipient.email + ' - ' + msg, 'err');
                lastFailedRecipients.push(recipient);
            }

            setProgress(i + 1, recipients.length);

            if (i < recipients.length - 1 && delay > 0 && !stopRequested) {
                await new Promise(function (r) { setTimeout(r, delay); });
            }
        }

        logLine('-------------------------------------------------------', 'info');
        logLine('Done: ' + sent + ' successful, ' + failed + ' failed.', sent > 0 ? 'ok' : 'err');

        summaryBox.className = 'inv-summary show';
        var summaryHtml = '';
        if (failed === 0 && sent > 0) {
            summaryBox.classList.add('success');
            summaryHtml = 'Done! <strong>' + sent + '</strong> emails sent successfully.';
        } else if (sent > 0) {
            summaryBox.classList.add('partial');
            summaryHtml = 'Sent: <strong>' + sent + '</strong> | Failed: <strong>' + failed + '</strong>.';
        } else {
            summaryBox.classList.add('error');
            summaryHtml = 'All sends failed. Check the log above for the specific reason for each.';
        }
        if (lastFailedRecipients.length > 0) {
            summaryHtml += ' <button type="button" class="inv-retry-btn" id="retryFailedBtn">' +
                '<svg class="inv-icon sm" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="1 4 1 10 7 10"></polyline><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"></path></svg>' +
                'Retry ' + lastFailedRecipients.length + ' failed</button>';
        }
        summaryBox.innerHTML = summaryHtml;

        var retryBtn = document.getElementById('retryFailedBtn');
        if (retryBtn) {
            retryBtn.addEventListener('click', function () {
                var toRetry = lastFailedRecipients.slice();
                sendBatch(toRetry);
            });
        }

        isSending = false;
        sendBtn.removeAttribute('disabled');
        sendBtn.disabled = false;
        sendBtnText.textContent = 'Send Again';
        sendBtnIcon.innerHTML = ICON_SEND;
        if (stopBtn) stopBtn.style.display = 'none';

        // Refresh History from the database now that this batch is done —
        // the live log above already showed progress in real time.
        fetchHistory();
    }

    sendBtn.addEventListener('click', function () {
        if (validEmailsArray.length === 0) return;
        sendBatch(validEmailsArray.slice());
    });

    if (stopBtn) {
        stopBtn.addEventListener('click', function () {
            stopRequested = true;
            stopBtn.disabled = true;
            if (stopBtnText) stopBtnText.textContent = 'Stopping...';
            setTimeout(function () {
                stopBtn.disabled = false;
                if (stopBtnText) stopBtnText.textContent = 'Stop Sending';
            }, 1500);
        });
    }
});