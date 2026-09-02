/**
 * adminPanel.js
 * Blockchain Education 2026 — Admin Panel
 *
 * Sections:
 *   1.  Toast Notifications
 *   2.  Tab Navigation
 *   3.  Modal System
 *   4.  View Registration Modal Binding
 *   5.  Edit Registration Modal Binding
 *   6.  Content Modal Bindings (Ticket, Lecturer, Event, Member, Partner, Schedule, Hotel, FAQ)
 *   7.  Delete Flow
 *   8.  Payments — Confirm / Cancel
 *   9.  Verifications — Approve / Reject
 *   10. Table Filters
 *   11. Image Zoom Preview
 *   12. Toggles & Drag-and-Drop (Promo Slides, FAQ)
 *   13. Form Validation
 *   14. Centralised Submit Helper
 *   15. Save Handlers
 *   16. Utility Helpers
 *   17. Bootstrap / Init
 */

'use strict';

// ─────────────────────────────────────────────────────────────────────────────
// 1. TOAST NOTIFICATIONS
// ─────────────────────────────────────────────────────────────────────────────

const TOAST_ICONS = {
    success: '<svg class="icon-inline icon-lg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"></circle><polyline points="8 12.5 11 15.5 16 9"></polyline></svg>',
    error: '<svg class="icon-inline icon-lg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"></circle><line x1="9" y1="9" x2="15" y2="15"></line><line x1="15" y1="9" x2="9" y2="15"></line></svg>',
    warning: '<svg class="icon-inline icon-lg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"></path><line x1="12" y1="9" x2="12" y2="13"></line><line x1="12" y1="17" x2="12.01" y2="17"></line></svg>',
    info: '<svg class="icon-inline icon-lg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="9"></circle><line x1="12" y1="11" x2="12" y2="16"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>',
};

const TOAST_KICKERS = { success: 'Done', error: 'Error', warning: 'Warning', info: 'Note' };

// Продължителността зависи от вида. Грешката е 0 = не изчезва сама:
// потребителят трябва да успее да я прочете и да я затвори сам, защото
// текстът в нея често идва от сървъра и е дълъг.
const TOAST_DURATIONS = { success: 3500, info: 4000, warning: 6000, error: 0 };

// Повече от три наведнъж не се четат — най-старият излиза.
const TOAST_LIMIT = 3;

const TOAST_CLOSE_ICON = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>';

function dismissToast(toast) {
    if (!toast || toast.dataset.leaving === '1') return;
    toast.dataset.leaving = '1';
    toast.classList.remove('show');
    toast.classList.add('is-leaving');
    // transitionend може да не дойде (скрит таб, reduced motion) — пазим се.
    const kill = () => toast.remove();
    toast.addEventListener('transitionend', kill, { once: true });
    setTimeout(kill, 400);
}

window.showToast = function showToast(message, type = 'success', duration) {
    const container = document.getElementById('toast-container');
    if (!container) return;
    if (!TOAST_ICONS[type]) type = 'info';

    const ms = duration ?? TOAST_DURATIONS[type] ?? 3500;

    const toast = document.createElement('div');
    toast.className = `toast toast-${type}`;
    toast.setAttribute('role', type === 'error' ? 'alert' : 'status');
    toast.innerHTML =
        `<span class="toast-icon" aria-hidden="true">${TOAST_ICONS[type]}</span>` +
        `<span class="toast-body"><span class="toast-kicker">${TOAST_KICKERS[type]}</span>` +
        `<span class="toast-msg"></span></span>` +
        `<button type="button" class="toast-close" aria-label="Dismiss">${TOAST_CLOSE_ICON}</button>`;
    // textContent, не innerHTML — съобщенията за грешка идват от сървъра.
    toast.querySelector('.toast-msg').textContent = message;
    toast.querySelector('.toast-close').addEventListener('click', () => dismissToast(toast));
    container.appendChild(toast);

    // Контейнерът е column-reverse, така че първото дете е най-старото.
    while (container.children.length > TOAST_LIMIT) {
        dismissToast(container.firstElementChild);
    }

    requestAnimationFrame(() => requestAnimationFrame(() => toast.classList.add('show')));

    if (ms > 0) {
        const timer = setTimeout(() => dismissToast(toast), ms);
        // Ако мишката стои върху toast-а, той чака.
        toast.addEventListener('mouseenter', () => clearTimeout(timer));
        toast.addEventListener('mouseleave', () => setTimeout(() => dismissToast(toast), 1200));
    }

    return toast;
};

// ─────────────────────────────────────────────────────────────────────────────
// 2. TAB NAVIGATION
// ─────────────────────────────────────────────────────────────────────────────

// ВНИМАНИЕ: нов таб в Index.cshtml НЕ работи, докато не се добави и тук.
// activateTab() отхвърля всичко извън този списък и пада обратно на
// 'tab-registrations' — бутонът се вижда, но при клик не се случва нищо
// и в конзолата няма грешка. Лесно се пропуска.
const VALID_TABS = new Set([
    'tab-registrations', 'tab-payments', 'tab-verifications',
    'tab-crypto', 'tab-audit', 'tab-conference', 'tab-icbi',
    'tab-lecturers', 'tab-schedule', 'tab-attend', 'tab-travel',
    'tab-faq', 'tab-privacy', 'tab-terms', 'tab-cookies', 'tab-settings',
    'tab-emails', 'tab-health', 'tab-paycontrol',
]);

function initTabs() {
    const tabs     = document.querySelectorAll('.admin-tab');
    const sections = document.querySelectorAll('.admin-section');

    function activateTab(tabId) {
        if (!VALID_TABS.has(tabId)) tabId = 'tab-registrations';
        tabs.forEach(t => t.classList.remove('active'));
        sections.forEach(s => s.classList.remove('active'));
        const btn     = document.querySelector(`.admin-tab[data-target="${tabId}"]`);
        const section = document.getElementById(tabId);
        if (btn && section) {
            btn.classList.add('active');
            section.classList.add('active');
            localStorage.setItem('activeAdminTab', tabId);
        }
    }

    activateTab(localStorage.getItem('activeAdminTab') || 'tab-registrations');
    tabs.forEach(tab => tab.addEventListener('click', () => activateTab(tab.dataset.target)));
}

// ─────────────────────────────────────────────────────────────────────────────
// 3. MODAL SYSTEM
// ─────────────────────────────────────────────────────────────────────────────

function openModal(id) {
    document.getElementById(id)?.classList.add('active');
}

function closeModal() {
    document.querySelectorAll('.modal-overlay').forEach(m => m.classList.remove('active'));
    // Clear all validation state
    document.querySelectorAll('.input-error').forEach(el => el.classList.remove('input-error'));
    document.querySelectorAll('.field-error-msg').forEach(el => el.remove());
    document.querySelectorAll('.modal-validation-banner').forEach(el => el.classList.remove('show'));
    document.querySelectorAll('.image-upload-preview').forEach(el => el.classList.remove('show'));
}

function initModals() {
    document.querySelectorAll('.modal-close-btn').forEach(btn =>
        btn.addEventListener('click', closeModal));

    document.querySelectorAll('.modal-overlay').forEach(overlay =>
        overlay.addEventListener('mousedown', e => { if (e.target === overlay) closeModal(); }));

    document.addEventListener('keydown', e => { if (e.key === 'Escape') closeModal(); });

    document.querySelectorAll('[data-modal-target]').forEach(trigger =>
        trigger.addEventListener('click', () => handleModalTrigger(trigger)));

    // "Edit" shortcut inside view modal
    const viewEditBtn = document.getElementById('viewModalEditBtn');
    if (viewEditBtn) {
        viewEditBtn.addEventListener('click', () => {
            const uid = viewEditBtn.dataset.targetUserId;
            closeModal();
            document.querySelector(`.edit-reg-btn[data-id="${uid}"]`)?.click();
        });
    }
}

async function handleModalTrigger(trigger) {
    const targetId  = trigger.dataset.modalTarget;
    const titleText = trigger.dataset.modalTitle;
    const modal     = document.getElementById(targetId);
    if (!modal) return;

    if (titleText) {
        const h3 = modal.querySelector('.modal-header h3');
        if (h3) h3.textContent = titleText;
    }

    const cl = trigger.classList;
    if (targetId === 'delete-modal')               bindDeleteModal(trigger);
    else if (cl.contains('view-reg-btn'))          await bindViewModal(trigger);
    else if (cl.contains('edit-reg-btn'))          bindEditRegModal(trigger);
    else if (cl.contains('edit-ticket-btn'))       bindTicketModal(trigger);
    else if (cl.contains('edit-lecturer-btn'))     bindLecturerModal(trigger);
    else if (cl.contains('edit-event-btn'))        bindEventModal(trigger);
    else if (cl.contains('edit-member-btn'))       bindMemberModal(trigger);
    else if (cl.contains('edit-partner-btn'))      bindPartnerModal(trigger);
    else if (cl.contains('edit-schedule-btn'))     bindScheduleModal(trigger);
    else if (cl.contains('edit-hotel-btn'))        bindHotelModal(trigger);
    else if (cl.contains('edit-promo-btn'))        bindPromoModal(trigger);
    else if (cl.contains('edit-faq-btn'))          bindFaqModal(trigger);
    else if (cl.contains('edit-footerlink-btn'))   bindFooterLinkModal(trigger);

    openModal(targetId);
}

// ─────────────────────────────────────────────────────────────────────────────
// 4. VIEW REGISTRATION MODAL
// ─────────────────────────────────────────────────────────────────────────────

async function bindViewModal(trigger) {
    const d  = trigger.dataset;
    const id = d.id;

    setText('viewFullName',       `${d.fname} ${d.lname}`);
    setText('viewAge',             d.age);
    setText('viewEmail',           d.email);
    setText('viewPhone',           d.phone     || '—');
    setText('viewTitle',           d.title     || '—');
    setText('viewOrg',             d.org       || '—');
    setText('viewPart',            d.part      || '—');
    setText('viewForeign',         d.foreign);
    setText('viewCreated',         d.created);
    setText('viewRefNum',          d.refnum    || '—');
    setText('viewPayMethod',       d.paymethod || '—');
    setText('viewPaidAt',          d.paidat    || '—');
    setText('viewGdpr',            d.gdpr);
    setText('viewMarketing',       d.marketing);
    setText('viewPublishConsent',  d.publishconsent);

    setColoredStatus('viewAccStatus', d.confirmed,
        { 'Verified': '#2e7d32' }, '#f44336');

    setColoredStatus('viewPayment', d.payment,
        { 'Confirmed': '#2e7d32', 'Cancelled': '#d32f2f' }, '#f57f17');

    // Paper file
    const docEl = document.getElementById('viewDoc');
    if (docEl) {
        docEl.innerHTML = d.filename
            ? `<a href="?handler=DownloadPaper&userId=${id}" style="color:#2196F3;font-weight:bold;text-decoration:none;">${escHtml(d.filename)}</a>`
            : 'None';
    }

    // Verification block (Student=2 / Journalist=4 only)
    const verifSection = document.getElementById('viewVerifSection');
    if (verifSection) {
        const needsVerif = d.partform === '2' || d.partform === '4';
        verifSection.style.display = needsVerif ? '' : 'none';
        if (needsVerif) {
            setText('viewVerifStatus',      d.verifstatus      || 'None');
            setText('viewVerifInstitution', d.verifinstitution || '—');
            setText('viewVerifSpecialty',   d.verifspecialty   || '—');
            setText('viewVerifYear',        d.verifyear        || '—');
            const sidItem = document.getElementById('viewVerifStudentIdItem');
            if (sidItem) {
                sidItem.style.display = d.partform === '2' ? '' : 'none';
                setText('viewVerifStudentId', d.verifstudentid || '—');
            }
            const docLink = document.getElementById('viewVerifDocLink');
            if (docLink) {
                const has = d.hasverifDoc === 'true';
                docLink.style.display = has ? '' : 'none';
                if (has) {
                    const a = document.getElementById('viewVerifDocAnchor');
                    if (a) a.href = `?handler=DownloadVerifDoc&userId=${id}`;
                }
            }
        }
    }

    const viewEditBtn = document.getElementById('viewModalEditBtn');
    if (viewEditBtn) viewEditBtn.dataset.targetUserId = id;

    // Rejection reason (async — only for Rejected verifications)
    const rejectionBox  = document.getElementById('viewRejectionReasonBox');
    const rejectionSpan = document.getElementById('viewRejectionReason');
    if (rejectionBox && rejectionSpan) {
        const isRejected = d.verifstatus === 'Rejected' && (d.partform === '2' || d.partform === '4');
        rejectionBox.style.display = isRejected ? '' : 'none';
        if (isRejected) {
            rejectionSpan.textContent = 'Loading...';
            try {
                const r = await fetch(`?handler=FetchRejectionReason&userId=${encodeURIComponent(id)}`);
                const j = await r.json();
                rejectionSpan.textContent = j.reason || 'No reason recorded.';
            } catch {
                rejectionSpan.textContent = 'Could not load reason.';
            }
        }
    }

    // Audit logs (async)
    const auditBody = document.getElementById('viewAuditLogsBody');
    if (!auditBody) return;
    auditBody.innerHTML = '<tr><td colspan="4" style="text-align:center;color:#999;padding:20px;">Loading...</td></tr>';

    try {
        const res  = await fetch(`?handler=FetchUserAudits&email=${encodeURIComponent(d.email)}`);
        const logs = await res.json();
        if (logs?.length) {
            auditBody.innerHTML = logs.map(log => `
                <tr>
                    <td><strong>${escHtml(log.action)}</strong></td>
                    <td style="color:#999;font-size:12px;">${escHtml(log.ip)}</td>
                    <td style="white-space:nowrap;font-size:12px;">${escHtml(log.date)}</td>
                    <td style="color:#666;font-size:12px;">${escHtml(log.details)}</td>
                </tr>`).join('');
        } else {
            auditBody.innerHTML = '<tr><td colspan="4" style="text-align:center;color:#999;padding:16px;">No activity recorded yet.</td></tr>';
        }
    } catch {
        auditBody.innerHTML = '<tr><td colspan="4" style="text-align:center;color:#f44336;padding:16px;">Failed to load logs.</td></tr>';
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 5. EDIT REGISTRATION MODAL
// ─────────────────────────────────────────────────────────────────────────────

function bindEditRegModal(trigger) {
    const d = trigger.dataset;
    setVal('regUserId',        d.id);
    setVal('regFirstName',     d.fname);
    setVal('regLastName',      d.lname);
    setVal('regAge',           d.age);
    setVal('regPhone',         d.phone    || '');
    setVal('regTitle',         d.title    || '');
    setVal('regOrg',           d.org      || '');
    setVal('regParticipation', d.part     || '1');
    setVal('regPaymentStatus', d.payment  || 'Pending');

    setChecked('regForeignCheck',   d.foreign === 'true');
    setChecked('regEmailConfirmed', d.confirmed === 'Verified');

    // Verification status — show only for Student (2) and Journalist (4)
    const verifGroup = document.getElementById('regVerifStatusGroup');
    const verifSelect = document.getElementById('regVerifStatus');
    const part = d.part || '';
    if (verifGroup && verifSelect) {
        const needsVerif = part === '2' || part === '4';
        verifGroup.style.display = needsVerif ? '' : 'none';
        if (needsVerif) setVal('regVerifStatus', d.verifstatus || 'None');
    }

    // Also show/hide verif group when participation changes
    const partSelect = document.getElementById('regParticipation');
    if (partSelect) {
        partSelect.onchange = function () {
            if (!verifGroup) return;
            const p = this.value;
            verifGroup.style.display = (p === '2' || p === '4') ? '' : 'none';
        };
    }

    const link  = document.getElementById('regDownloadLink');
    const noFile = document.getElementById('regNoFile');
    if (link && noFile) {
        if (d.filename) {
            link.style.display  = 'inline-flex';
            noFile.style.display = 'none';
            setText('regFileName', d.filename);
            link.href = `?handler=DownloadPaper&userId=${d.id}`;
        } else {
            link.style.display  = 'none';
            noFile.style.display = 'inline';
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 6. CONTENT MODAL BINDINGS
// ─────────────────────────────────────────────────────────────────────────────

function bindTicketModal(trigger) {
    const d = trigger.dataset;
    setVal('editTicketId',        d.id);
    setVal('ticketName_en',       d['nameEn']);
    setVal('ticketName_bg',       d['nameBg']);
    setVal('ticketDesc_en',       d['descEn']);
    setVal('ticketDesc_bg',       d['descBg']);
    setVal('ticketRegPrice_en',   d['regEn']);
    setVal('ticketRegPrice_bg',   d['regBg']);
    setVal('ticketPromoPrice_en', d['promoEn'] || '');
    setVal('ticketPromoPrice_bg', d['promoBg'] || '');
    setVal('ticketPerks_en',      d['perksEn']);
    setVal('ticketPerks_bg',      d['perksBg']);
}

function initTicketPriceSync() {
    const pairs = [
        ['ticketRegPrice_en', 'ticketRegPrice_bg'],
        ['ticketPromoPrice_en', 'ticketPromoPrice_bg'],
    ];
    pairs.forEach(([visibleId, hiddenId]) => {
        const visible = document.getElementById(visibleId);
        const hidden  = document.getElementById(hiddenId);
        if (!visible || !hidden) return;
        visible.addEventListener('input', () => { hidden.value = visible.value; });
    });
}

function bindLecturerModal(trigger) {
    const d = trigger.dataset;
    setVal('lecturerId',       d.id);
    setVal('lecturerName_en',  d['nameEn']);
    setVal('lecturerName_bg',  d['nameBg']);
    setVal('lecturerCategory', d.category);
    setVal('lecturerRole_en',  d['roleEn']);
    setVal('lecturerRole_bg',  d['roleBg']);
    setVal('lecturerOrg_en',   d['orgEn']);
    setVal('lecturerOrg_bg',   d['orgBg']);
    setVal('lecturerBio_en',   d['bioEn']  || '');
    setVal('lecturerBio_bg',   d['bioBg']  || '');
    setVal('lecturerUrl',      d.url       || '');
    toggleRequiredStar('lecturerAvatarRequired', d.id);
}

function bindEventModal(trigger) {
    const d = trigger.dataset;
    setVal('eventId',          d.id);
    setVal('eventTitle_en',    d['titleEn']);
    setVal('eventTitle_bg',    d['titleBg']);
    setVal('eventLocation_en', d['locEn']);
    setVal('eventLocation_bg', d['locBg']);
    setVal('eventUrl',         d.url || '');
    toggleRequiredStar('eventImageRequired', d.id);
}

function bindMemberModal(trigger) {
    const d = trigger.dataset;
    setVal('memberId',        d.id);
    setVal('memberName_en',   d['nameEn']);
    setVal('memberName_bg',   d['nameBg']);
    setVal('memberRole_en',   d['roleEn']);
    setVal('memberRole_bg',   d['roleBg']);
    setVal('memberOrg_en',    d['orgEn']);
    setVal('memberOrg_bg',    d['orgBg']);
    setVal('memberCommittee', d.committee);
    toggleRequiredStar('memberAvatarRequired', d.id);
}

function bindPartnerModal(trigger) {
    const d = trigger.dataset;
    setVal('partnerId',       d.id);
    setVal('partnerName_en',  d['nameEn']);
    setVal('partnerName_bg',  d['nameBg']);
    setVal('partnerCategory', d.category);
    setVal('partnerUrl',      d.url || '');
    toggleRequiredStar('partnerLogoRequired', d.id);
}

function bindScheduleModal(trigger) {
    const d = trigger.dataset;
    setVal('sessionId',          d.id);
    setVal('sessionDay',         d.day);
    setVal('sessionStartTime',   d.start);
    setVal('sessionEndTime',     d.end);
    setVal('sessionTitle_en',    d['titleEn']);
    setVal('sessionTitle_bg',    d['titleBg']);
    setVal('sessionType',        d.type);
    setVal('sessionSpeaker_en',  d['speakerEn'] || '');
    setVal('sessionSpeaker_bg',  d['speakerBg'] || '');
    setVal('sessionLocation_en', d['locEn']     || '');
    setVal('sessionLocation_bg', d['locBg']     || '');
    setVal('sessionDesc_en',     d['descEn']    || '');
    setVal('sessionDesc_bg',     d['descBg']    || '');
    setVal('sessionLiveStreamUrl', d.streamUrl || '');
}

function bindHotelModal(trigger) {
    const d = trigger.dataset;
    setVal('hotelId',      d.id);
    setVal('hotelName_en', d['nameEn']);
    setVal('hotelName_bg', d['nameBg']);
    setVal('hotelDesc_en', d['descEn'] || '');
    setVal('hotelDesc_bg', d['descBg'] || '');
    setVal('hotelUrl',     d.url       || '');
}

function bindPromoModal(trigger) {
    const d = trigger.dataset;
    setVal('promoId',        d.id);
    setVal('promoTitle_en',  d['titleEn']);
    setVal('promoTitle_bg',  d['titleBg']);
    setVal('promoDesc_en',   d['descEn']);
    setVal('promoDesc_bg',   d['descBg']);
    toggleRequiredStar('promoImageRequired', d.id);
    ['promoTitle_en', 'promoTitle_bg', 'promoDesc_en', 'promoDesc_bg'].forEach(updateCharCounter);
}

function bindFaqModal(trigger) {
    const d = trigger.dataset;
    setVal('faqId',          d.id);
    setVal('faqQuestion_en', d['questionEn']);
    setVal('faqQuestion_bg', d['questionBg']);
    setVal('faqAnswer_en',   d['answerEn']);
    setVal('faqAnswer_bg',   d['answerBg']);
}

function bindFooterLinkModal(trigger) {
    const d = trigger.dataset;
    setVal('footerLinkId',       d.id);
    setVal('footerLinkLabel_en', d['labelEn']);
    setVal('footerLinkLabel_bg', d['labelBg']);
    setVal('footerLinkUrl',      d.url);
    setVal('footerLinkIconSvg',  d.icon);
    updateFooterLinkIconPreview();
    // setVal() присвоява .value директно, без да гърми 'input' event —
    // затова броячите (виж initCharCounters) не се опресняват сами при
    // Edit; правим го изрично тук, иначе показват "0 / 60" и т.н. дори
    // при вече попълнено поле.
    ['footerLinkLabel_en', 'footerLinkLabel_bg', 'footerLinkUrl', 'footerLinkIconSvg'].forEach(updateCharCounter);
}

// ─────────────────────────────────────────────────────────────────────────────
// 7. DELETE FLOW
// ─────────────────────────────────────────────────────────────────────────────

const DELETE_HANDLER_MAP = {
    'delete-lecturer-btn' : { handler: 'DeleteLecturer', tab: 'tab-lecturers'   },
    'delete-event-btn'    : { handler: 'DeleteEvent',    tab: 'tab-icbi'        },
    'delete-member-btn'   : { handler: 'DeleteMember',   tab: 'tab-conference'  },
    'delete-partner-btn'  : { handler: 'DeletePartner',  tab: 'tab-conference'  },
    'delete-session-btn'  : { handler: 'DeleteSession',  tab: 'tab-schedule'    },
    'delete-hotel-btn'    : { handler: 'DeleteHotel',    tab: 'tab-travel'      },
    'delete-promo-btn'    : { handler: 'DeletePromo',    tab: 'tab-settings'    },
    'delete-faq-btn'      : { handler: 'DeleteFaq',      tab: 'tab-faq'         },
    'delete-footerlink-btn': { handler: 'DeleteFooterLink', tab: 'tab-settings' },
    'delete-reg-btn'      : { handler: 'DeleteUser',     tab: 'tab-registrations'},
};

function bindDeleteModal(trigger) {
    const nameSpan = document.getElementById('delete-item-name');
    if (nameSpan) nameSpan.textContent = trigger.dataset.deleteName || 'this record';

    const confirmBtn = document.querySelector('#delete-modal .btn-confirm-delete');
    if (!confirmBtn) return;

    let cfg = { handler: '', tab: 'tab-registrations' };
    for (const [cls, data] of Object.entries(DELETE_HANDLER_MAP)) {
        if (trigger.classList.contains(cls)) { cfg = data; break; }
    }

    confirmBtn.dataset.deleteId      = trigger.dataset.id;
    confirmBtn.dataset.deleteHandler = cfg.handler;
    confirmBtn.dataset.deleteTab     = cfg.tab;
}

function initDeleteConfirm() {
    const btn = document.querySelector('#delete-modal .btn-confirm-delete');
    if (!btn) return;

    btn.addEventListener('click', async function () {
        const { deleteId: id, deleteHandler: handler, deleteTab: tab } = this.dataset;
        if (!id || !handler) return;

        setButtonLoading(this, 'Deleting...');
        try {
            const result = await postJson(`?handler=${handler}`, buildFormData({ id }));
            if (result.success) {
                showToast('Record deleted successfully.', 'success');
                closeModal();
                persistTabAndReload(tab);
            } else {
                showToast(`Error: ${result.message}`, 'error');
                resetButton(this, 'Yes, Delete It');
            }
        } catch {
            showToast('A server error occurred.', 'error');
            resetButton(this, 'Yes, Delete It');
        }
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// 8. PAYMENTS — CONFIRM / CANCEL
// ─────────────────────────────────────────────────────────────────────────────

function initPaymentActions() {
    document.addEventListener('click', async function (e) {
        const btn = e.target.closest('.confirm-payment-btn');
        if (btn) {
            e.preventDefault();
            const { userid, name, ref, method } = btn.dataset;
            if (!userid || btn.disabled) return;
            const displayMethod = method || 'Manual';
            if (!confirm(`Confirm payment for ${name}?\nReference: ${ref || '—'}\nMethod: ${displayMethod}`)) return;
            setButtonLoading(btn, 'Confirming...');
            try {
                const result = await postJson('?handler=ConfirmPayment',
                    buildFormData({ userId: userid, method: displayMethod }));
                if (result.success) {
                    showToast(`Payment confirmed for ${name}.`, 'success');
                    persistTabAndReload('tab-payments');
                } else {
                    showToast(`Error: ${result.message}`, 'error');
                    resetButton(btn, '<svg class="icon-inline" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg> Confirm');
                }
            } catch {
                showToast('A server error occurred.', 'error');
                resetButton(btn, '<svg class="icon-inline" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg> Confirm');
            }
        }

        const cancelBtn = e.target.closest('.cancel-payment-btn');
        if (cancelBtn) {
            e.preventDefault();
            const { userid, name } = cancelBtn.dataset;
            if (!userid || cancelBtn.disabled) return;
            if (!confirm(`Cancel payment for ${name}?\nThis will mark their status as Cancelled.`)) return;
            setButtonLoading(cancelBtn, 'Cancelling...');
            try {
                const result = await postJson('?handler=CancelPayment',
                    buildFormData({ userId: userid }));
                if (result.success) {
                    showToast(`Payment cancelled for ${name}.`, 'warning');
                    persistTabAndReload('tab-payments');
                } else {
                    showToast(`Error: ${result.message}`, 'error');
                    resetButton(cancelBtn, '<svg class="icon-inline" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg> Cancel');
                }
            } catch {
                showToast('A server error occurred.', 'error');
                resetButton(cancelBtn, '<svg class="icon-inline" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg> Cancel');
            }
        }
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// 9. VERIFICATIONS — APPROVE / REJECT
// ─────────────────────────────────────────────────────────────────────────────

function initVerificationActions() {
    document.querySelectorAll('.approve-verif-btn').forEach(btn => {
        btn.addEventListener('click', async function () {
            const { userid, name, type, partform } = this.dataset;
            const isSubsidised = partform === '2' || partform === '4';
            const subsidisedNote = partform === '2'
                ? '\n\n📌 This will auto-confirm their payment as Subsidised (student discount).'
                : partform === '4'
                ? '\n\n📌 This will auto-confirm their registration (journalist/media accreditation).'
                : '';
            const msg = `Approve verification for ${name} (${type})?${subsidisedNote}`;
            if (!confirm(msg)) return;

            setButtonLoading(this, 'Approving...');
            try {
                const result = await postJson('?handler=ApproveVerification',
                    buildFormData({ userId: userid }));

                if (result.success) {
                    const extra = result.paymentAutoConfirmed
                        ? ' Registration auto-confirmed (subsidised).' : '';
                    showToast(`Verification approved for ${name}.${extra}`, 'success');
                    persistTabAndReload('tab-verifications');
                } else {
                    showToast(`Error: ${result.message}`, 'error');
                    resetButton(this, '<svg class="icon-inline" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg> Approve');
                }
            } catch {
                showToast('A server error occurred.', 'error');
                resetButton(this, '<svg class="icon-inline" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg> Approve');
            }
        });
    });

    document.querySelectorAll('.reject-verif-btn').forEach(btn => {
        btn.addEventListener('click', function () {
            const { userid, name } = this.dataset;
            setText('rejectVerifName', name);
            const confirmBtn = document.getElementById('confirmRejectBtn');
            if (confirmBtn) {
                confirmBtn.dataset.userId = userid;
                confirmBtn.dataset.name   = name;
            }
            const textarea = document.getElementById('rejectReasonText');
            if (textarea) textarea.value = '';

            openModal('reject-verif-modal');
        });
    });

    const confirmRejectBtn = document.getElementById('confirmRejectBtn');
    if (confirmRejectBtn) {
        confirmRejectBtn.addEventListener('click', async function () {
            const { userId, name } = this.dataset;
            const reason = (document.getElementById('rejectReasonText')?.value ?? '').trim();

            if (reason.length < 5) {
                showToast('Please provide a rejection reason (minimum 5 characters).', 'error');
                return;
            }

            setButtonLoading(this, 'Rejecting...');
            try {
                const result = await postJson('?handler=RejectVerification',
                    buildFormData({ userId, reason }));

                if (result.success) {
                    showToast(`Verification rejected for ${name}.`, 'warning');
                    persistTabAndReload('tab-verifications');
                } else {
                    showToast(`Error: ${result.message}`, 'error');
                    resetButton(this, 'Confirm Rejection');
                }
            } catch {
                showToast('A server error occurred.', 'error');
                resetButton(this, 'Confirm Rejection');
            }
        });
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 10. TABLE FILTERS
// ─────────────────────────────────────────────────────────────────────────────

function setupTableFilter(rowSelector, filters) {
    const rows = document.querySelectorAll(rowSelector);
    if (!rows.length) return;

    const configs = filters.map(f => ({
        el:   document.getElementById(f.inputId),
        attr: f.dataAttr,
        type: f.type || 'select',
    }));

    function apply() {
        rows.forEach(row => {
            const show = configs.every(({ el, attr, type }) => {
                if (!el) return true;
                const val     = el.value.trim();
                const rowData = (row.dataset[attr] ?? '').toLowerCase();
                if (type === 'search') return !val || rowData.includes(val.toLowerCase());
                return val === 'all' || rowData === val.toLowerCase();
            });
            row.style.display = show ? '' : 'none';
        });
    }

    configs.forEach(({ el, type }) => {
        if (el) el.addEventListener(type === 'search' ? 'input' : 'change', apply);
    });
}

function initFilters() {
    setupTableFilter('.reg-data-row', [
        { inputId: 'regSearchInput',    dataAttr: 'search',  type: 'search' },
        { inputId: 'regPaymentFilter',  dataAttr: 'payment', type: 'select' },
        { inputId: 'regTypeFilter',     dataAttr: 'type',    type: 'select' },
        { inputId: 'regAccountFilter',  dataAttr: 'account', type: 'select' },
    ]);
    setupTableFilter('.pay-data-row', [
        { inputId: 'paySearchInput',    dataAttr: 'search', type: 'search' },
        { inputId: 'payStatusFilter',   dataAttr: 'status', type: 'select' },
        { inputId: 'payMethodFilter',   dataAttr: 'method', type: 'select' },
    ]);
    setupTableFilter('.verif-data-row', [
        { inputId: 'verifSearchInput',  dataAttr: 'search', type: 'search' },
        { inputId: 'verifStatusFilter', dataAttr: 'status', type: 'select' },
        { inputId: 'verifTypeFilter',   dataAttr: 'type',   type: 'select' },
    ]);
    setupTableFilter('.crypto-data-row', [
        { inputId: 'cryptoSearchInput',    dataAttr: 'search',   type: 'search' },
        { inputId: 'cryptoStatusFilter',   dataAttr: 'status',   type: 'select' },
        { inputId: 'cryptoCurrencyFilter', dataAttr: 'currency', type: 'select' },
    ]);
    setupTableFilter('.audit-data-row', [
        { inputId: 'auditSearchInput',  dataAttr: 'search', type: 'search' },
        { inputId: 'auditActionFilter', dataAttr: 'action', type: 'select' },
    ]);
}

// ─────────────────────────────────────────────────────────────────────────────
// 11. IMAGE ZOOM PREVIEW
// ─────────────────────────────────────────────────────────────────────────────

function initImageZoom() {
    document.querySelectorAll('.zoomable-image').forEach(img => {
        img.addEventListener('click', () => {
            const src = img.dataset.imageSrc || img.src;
            const preview = document.getElementById('preview-image-src');
            if (preview) { preview.src = src; openModal('image-preview-modal'); }
        });
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// 12. TOGGLES & DRAG-AND-DROP (Promo Slides, FAQ)
// ─────────────────────────────────────────────────────────────────────────────

function setupImagePreview(fileInputId, previewId, previewImgId, previewNameId) {
    const input = document.getElementById(fileInputId);
    if (!input) return;

    input.addEventListener('change', function () {
        const preview    = document.getElementById(previewId);
        const previewImg = document.getElementById(previewImgId);
        const previewName = document.getElementById(previewNameId);
        const file = this.files?.[0];

        if (file) {
            const reader = new FileReader();
            reader.onload = e => {
                if (previewImg)  previewImg.src     = e.target.result;
                if (previewName) previewName.textContent = file.name;
                if (preview)     preview.classList.add('show');
            };
            reader.readAsDataURL(file);
        } else {
            preview?.classList.remove('show');
        }

        this.classList.remove('input-error');
        this.parentNode.querySelector('.field-error-msg')?.remove();
    });
}

function initPromoToggleActive() {
    document.querySelectorAll('.toggle-promo-active-btn').forEach(btn => {
        btn.addEventListener('click', async function () {
            const id = this.dataset.id;
            this.disabled = true;
            try {
                const result = await postJson('?handler=TogglePromoActive', buildFormData({ id }));
                if (result.success) {
                    persistTabAndReload('tab-settings', 300);
                } else {
                    showToast(`Error: ${result.message}`, 'error');
                    this.disabled = false;
                }
            } catch {
                showToast('A server error occurred. Please try again.', 'error');
                this.disabled = false;
            }
        });
    });
}

function initFaqToggleActive() {
    document.querySelectorAll('.toggle-faq-active-btn').forEach(btn => {
        btn.addEventListener('click', async function () {
            const id = this.dataset.id;
            this.disabled = true;
            try {
                const result = await postJson('?handler=ToggleFaqActive', buildFormData({ id }));
                if (result.success) {
                    persistTabAndReload('tab-faq', 300);
                } else {
                    showToast(`Error: ${result.message}`, 'error');
                    this.disabled = false;
                }
            } catch {
                showToast('A server error occurred. Please try again.', 'error');
                this.disabled = false;
            }
        });
    });
}

// Same show/hide pattern as Promo/FAQ, but the row list here has no drag
// handle — display order on the public footer is fully random (see
// _Layout.cshtml), not admin-curated, so there's nothing to persist an
// ── Имейл известия: включване и изключване по вид ────────────────────────
// Без презареждане на страницата — превключвателят е моментален. При грешка
// от сървъра се връща на предишното си положение, за да не показва състояние,
// което не е записано.
// ── Крипто: изчистване на неактивните поръчки ────────────────────────────
// Трие само изтеклите. Потвърдените са следа от реално плащане и остават.
function initClearInactiveCrypto() {
    const btn = document.getElementById('clearInactiveCryptoBtn');
    if (!btn) return;

    btn.addEventListener('click', async function () {
        if (!confirm(
            'Да изчистя ли изтеклите крипто поръчки?\n\n' +
            'Потвърдените плащания и активните поръчки НЕ се пипат.\n' +
            'Действието е необратимо.'
        )) return;

        const original = btn.innerHTML;
        btn.disabled = true;
        btn.textContent = 'Изчиства…';

        try {
            const result = await postJson('?handler=ClearInactiveCryptoOrders', buildFormData({}));
            if (result.success) {
                showToast(result.message || 'Готово.', 'success');
                if (result.removed > 0) setTimeout(() => location.reload(), 900);
                else { btn.disabled = false; btn.innerHTML = original; }
            } else {
                showToast('Грешка: ' + result.message, 'error');
                btn.disabled = false; btn.innerHTML = original;
            }
        } catch {
            showToast('Сървърна грешка. Опитайте отново.', 'error');
            btn.disabled = false; btn.innerHTML = original;
        }
    });
}

function initEmailNotificationToggles() {
    document.querySelectorAll('.email-toggle-input').forEach(input => {
        input.addEventListener('change', async function () {
            const key      = this.dataset.key;
            const enabled  = this.checked;
            const row      = this.closest('.email-toggle-row');

            this.disabled = true;
            if (row) row.classList.add('is-saving');

            try {
                const result = await postJson(
                    '?handler=ToggleEmailNotification',
                    buildFormData({ templateKey: key, enabled: enabled })
                );

                if (result.success) {
                    showToast(
                        enabled ? 'Notification enabled.' : 'Notification disabled.',
                        'success'
                    );
                    const label = this.closest('.email-switch');
                    if (label) label.title = enabled ? 'Enabled' : 'Disabled';
                } else {
                    // Връщаме превключвателя — състоянието НЕ е записано.
                    this.checked = !enabled;
                    showToast(`Error: ${result.message}`, 'error');
                }
            } catch {
                this.checked = !enabled;
                showToast('A server error occurred. Please try again.', 'error');
            } finally {
                this.disabled = false;
                if (row) row.classList.remove('is-saving');
            }
        });
    });
}

// order for.
// ЗАДАЧА 3 — управление на плащанията.
// Осем превключвателя на три нива. Скриптът само записва състоянието и
// поддържа йерархията видима; решението кое се показва на /Payment се
// взима на сървъра.
//
//   ниво        data-gate-key        data-gate-level
//   всички      all                  all
//   метод       method.card          method
//               method.crypto        method
//               method.iban          method
//   валута      currency.BTC         currency
//               currency.ETH         currency
//               currency.EURC        currency
//               currency.USDC        currency
//
// Заявка: POST ?handler=TogglePaymentGate  { key, enabled }
// Очакван отговор: { success: bool, message: string }
function initPaymentGates() {
    const root = document.getElementById('payment-gates');
    if (!root) return;

    const master     = root.querySelector('[data-gate-key="all"]');
    const cryptoGate = root.querySelector('[data-gate-key="method.crypto"]');
    const methods    = root.querySelectorAll('[data-gate-level="method"]');
    const currencies = root.querySelectorAll('[data-gate-level="currency"]');
    const methodGroup   = root.querySelector('[data-gate-group="method"]');
    const currencyGroup = root.querySelector('[data-gate-group="currency"]');
    const stateEl    = document.getElementById('pay-gate-state');

    function lock(input, locked) {
        input.disabled = locked;
        const row = input.closest('.pay-gate-row');
        if (row) row.setAttribute('aria-disabled', locked ? 'true' : 'false');
    }

    function syncHierarchy() {
        const allOn = master ? master.checked : true;
        root.classList.toggle('is-stopped', !allOn);
        if (stateEl) stateEl.textContent = allOn ? 'Payments are open' : 'All payments stopped';

        methods.forEach(i => lock(i, !allOn));
        if (methodGroup) methodGroup.classList.toggle('is-locked', !allOn);

        const cryptoOn = allOn && (!cryptoGate || cryptoGate.checked);
        currencies.forEach(i => lock(i, !cryptoOn));
        if (currencyGroup) currencyGroup.classList.toggle('is-locked', !cryptoOn);
    }

    root.querySelectorAll('.pay-gate-input').forEach(input => {
        input.addEventListener('change', async function () {
            const key     = this.dataset.gateKey;
            const enabled = this.checked;
            const row     = this.closest('.pay-gate-row') || this.closest('.pay-gate-master');

            this.disabled = true;
            if (row) row.classList.add('is-saving');
            syncHierarchy();

            try {
                const result = await postJson('?handler=TogglePaymentGate',
                    buildFormData({ key: key, enabled: enabled }));

                if (result.success) {
                    showToast(enabled ? 'Payments enabled.' : 'Payments stopped.',
                              enabled ? 'success' : 'warning');
                } else {
                    // Състоянието НЕ е записано — връщаме превключвателя.
                    this.checked = !enabled;
                    showToast(`Error: ${result.message}`, 'error');
                }
            } catch {
                this.checked = !enabled;
                showToast('A server error occurred. Please try again.', 'error');
            } finally {
                this.disabled = false;
                if (row) row.classList.remove('is-saving');
                syncHierarchy();
            }
        });
    });

    syncHierarchy();
}

function initFooterLinkToggleActive() {
    document.querySelectorAll('.toggle-footerlink-active-btn').forEach(btn => {
        btn.addEventListener('click', async function () {
            const id = this.dataset.id;
            this.disabled = true;
            try {
                const result = await postJson('?handler=ToggleFooterLinkActive', buildFormData({ id }));
                if (result.success) {
                    persistTabAndReload('tab-settings', 300);
                } else {
                    showToast(`Error: ${result.message}`, 'error');
                    this.disabled = false;
                }
            } catch {
                showToast('A server error occurred. Please try again.', 'error');
                this.disabled = false;
            }
        });
    });
}

// Live preview while the admin edits the SVG code field — wraps whatever
// is currently typed in the site's standard 24×24 stroke icon frame, same
// as .footer-quicklink-icon on the public footer. Swallows malformed
// markup quietly (innerHTML on an SVG-namespaced node just renders
// nothing useful rather than throwing), so a half-typed tag never breaks
// the modal.
function updateFooterLinkIconPreview() {
    const svgCode = document.getElementById('footerLinkIconSvg')?.value || '';
    const preview = document.getElementById('footerLinkIconPreview');
    if (!preview) return;
    preview.innerHTML = `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">${svgCode}</svg>`;
}

function initFooterLinkIconPreview() {
    const textarea = document.getElementById('footerLinkIconSvg');
    if (!textarea) return;
    textarea.addEventListener('input', updateFooterLinkIconPreview);
}

// ── Generic Drag and Drop ────────────────────────────────────────────────────
function initDragDrop(listId, saveCallback) {
    const list = document.getElementById(listId);
    if (!list) return;

    let draggedRow = null;

    list.querySelectorAll('.promo-slide-row').forEach(row => {
        row.addEventListener('dragstart', () => {
            draggedRow = row;
            row.classList.add('is-dragging');
        });

        row.addEventListener('dragend', () => {
            row.classList.remove('is-dragging');
            list.querySelectorAll('.promo-slide-row').forEach(r => r.classList.remove('is-drag-over'));
            draggedRow = null;
        });

        row.addEventListener('dragover', e => {
            e.preventDefault();
            if (row === draggedRow) return;
            row.classList.add('is-drag-over');
        });

        row.addEventListener('dragleave', () => row.classList.remove('is-drag-over'));

        row.addEventListener('drop', async e => {
            e.preventDefault();
            row.classList.remove('is-drag-over');
            if (!draggedRow || row === draggedRow) return;

            const rect = row.getBoundingClientRect();
            const insertAfter = (e.clientY - rect.top) > rect.height / 2;
            if (insertAfter) row.after(draggedRow); else row.before(draggedRow);

            await saveCallback();
        });
    });

    initTouchDragDrop(list, saveCallback);
}

function initTouchDragDrop(list, saveCallback) {
    let touchDraggedRow = null;
    let lastTargetRow = null;
    let moved = false;
    const TOUCH_MOVE_THRESHOLD = 6;
    let startX = 0, startY = 0;

    list.querySelectorAll('.promo-drag-handle').forEach(handle => {
        handle.addEventListener('touchstart', e => {
            const row = handle.closest('.promo-slide-row');
            if (!row) return;
            touchDraggedRow = row;
            lastTargetRow = null;
            moved = false;
            const t = e.touches[0];
            startX = t.clientX;
            startY = t.clientY;
        }, { passive: true });

        handle.addEventListener('touchmove', e => {
            if (!touchDraggedRow) return;
            const t = e.touches[0];
            const dx = Math.abs(t.clientX - startX);
            const dy = Math.abs(t.clientY - startY);

            if (!moved) {
                if (dx < TOUCH_MOVE_THRESHOLD && dy < TOUCH_MOVE_THRESHOLD) return;
                moved = true;
                touchDraggedRow.classList.add('is-dragging');
            }

            e.preventDefault();

            const elAtPoint = document.elementFromPoint(t.clientX, t.clientY);
            const targetRow = elAtPoint ? elAtPoint.closest('.promo-slide-row') : null;

            if (lastTargetRow && lastTargetRow !== targetRow) {
                lastTargetRow.classList.remove('is-drag-over');
            }
            if (targetRow && targetRow !== touchDraggedRow) {
                targetRow.classList.add('is-drag-over');
                lastTargetRow = targetRow;
            } else {
                lastTargetRow = null;
            }
        }, { passive: false });

        handle.addEventListener('touchend', async e => {
            if (!touchDraggedRow) return;
            const draggedRow = touchDraggedRow;
            const targetRow = lastTargetRow;

            draggedRow.classList.remove('is-dragging');
            if (targetRow) targetRow.classList.remove('is-drag-over');

            touchDraggedRow = null;
            lastTargetRow = null;

            if (!moved || !targetRow || targetRow === draggedRow) {
                moved = false;
                return;
            }
            moved = false;

            const finalTouch = e.changedTouches[0];
            const rect = targetRow.getBoundingClientRect();
            const insertAfter = (finalTouch.clientY - rect.top) > rect.height / 2;
            if (insertAfter) targetRow.after(draggedRow); else targetRow.before(draggedRow);

            await saveCallback();
        });

        handle.addEventListener('touchcancel', () => {
            if (touchDraggedRow) touchDraggedRow.classList.remove('is-dragging');
            if (lastTargetRow) lastTargetRow.classList.remove('is-drag-over');
            touchDraggedRow = null;
            lastTargetRow = null;
            moved = false;
        });
    });
}

async function savePromoOrder() {
    const list = document.getElementById('promoSlideList');
    if (!list) return;

    const fd = new FormData();
    fd.append('__RequestVerificationToken', getCsrfToken());
    list.querySelectorAll('.promo-slide-row').forEach(row => fd.append('orderedIds', row.dataset.id));

    try {
        const result = await postJson('?handler=ReorderPromos', fd);
        showToast(result.success ? 'Order saved.' : `Error: ${result.message}`, result.success ? 'success' : 'error');
        if (result.success) localStorage.setItem('activeAdminTab', 'tab-settings');
    } catch {
        showToast('A server error occurred while saving the new order.', 'error');
    }
}

async function saveFaqOrder() {
    const list = document.getElementById('faqList');
    if (!list) return;

    const fd = new FormData();
    fd.append('__RequestVerificationToken', getCsrfToken());
    list.querySelectorAll('.promo-slide-row').forEach(row => fd.append('orderedIds', row.dataset.id));

    try {
        const result = await postJson('?handler=ReorderFaqs', fd);
        showToast(result.success ? 'Order saved.' : `Error: ${result.message}`, result.success ? 'success' : 'error');
        if (result.success) localStorage.setItem('activeAdminTab', 'tab-faq');
    } catch {
        showToast('A server error occurred while saving the new order.', 'error');
    }
}

function initImagePreviews() {
    setupImagePreview('lecturerAvatar', 'lecturerAvatarPreview', 'lecturerAvatarPreviewImg', 'lecturerAvatarPreviewName');
    setupImagePreview('memberAvatar',   'memberAvatarPreview',   'memberAvatarPreviewImg',   'memberAvatarPreviewName');
    setupImagePreview('partnerLogo',    'partnerLogoPreview',    'partnerLogoPreviewImg',    'partnerLogoPreviewName');
    setupImagePreview('eventImage',     'eventImagePreview',     'eventImagePreviewImg',     'eventImagePreviewName');
    setupImagePreview('promoImage',     'promoImagePreview',     'promoImagePreviewImg',     'promoImagePreviewName');
}

// ─────────────────────────────────────────────────────────────────────────────
// 13. FORM VALIDATION
// ─────────────────────────────────────────────────────────────────────────────

/**
 * @param {string|null} bannerId - ID of the banner element (null = no banner)
 * @param {Array}       rules    - [{ id, label, required, min, max, pattern, patternMsg }]
 * @param {string}      [fileId] - Optional file input required on new records
 * @param {boolean}     [isNew]  - True if creating a new record
 */
function validateForm(bannerId, rules, fileId = null, isNew = false) {
    let valid = true;

    rules.forEach(({ id }) => {
        const el = document.getElementById(id);
        if (!el) return;
        el.classList.remove('input-error');
        el.parentNode.querySelector('.field-error-msg')?.remove();
    });

    const banner = bannerId ? document.getElementById(bannerId) : null;
    banner?.classList.remove('show');

    rules.forEach(rule => {
        const el = document.getElementById(rule.id);
        if (!el) return;
        const val = el.value?.trim() ?? '';
        let msg = '';

        // Забележка: rule.required вече се проверява ПЪРВО, но само за
        // самото "празно". Ако полето Е опционално (required не е
        // зададено) И има стойност, maxLength/min/max/pattern все пак
        // се прилагат — иначе опционални полета (напр. Footer Content →
        // Brand Tagline) не биха се валидирали изобщо, щом не са празни.
        if (rule.required && !val) {
            msg = `${rule.label} is required.`;
        } else if (val && rule.maxLength !== undefined && val.length > rule.maxLength) {
            msg = `${rule.label} is too long (max ${rule.maxLength} characters).`;
        } else if (val && rule.min !== undefined && Number(val) < rule.min) {
            msg = `${rule.label} must be at least ${rule.min}.`;
        } else if (val && rule.max !== undefined && Number(val) > rule.max) {
            msg = `${rule.label} must be at most ${rule.max}.`;
        } else if (val && rule.pattern && !rule.pattern.test(val)) {
            msg = rule.patternMsg || `${rule.label} is invalid.`;
        }

        if (msg) {
            valid = false;
            el.classList.add('input-error');
            const div = document.createElement('div');
            div.className   = 'field-error-msg';
            div.textContent = msg;
            el.parentNode.appendChild(div);
        }
    });

    if (fileId && isNew) {
        const fileEl = document.getElementById(fileId);
        if (fileEl && !fileEl.files.length) {
            valid = false;
            fileEl.classList.add('input-error');
            const div = document.createElement('div');
            div.className   = 'field-error-msg';
            div.textContent = 'An image file is required when adding a new record.';
            fileEl.parentNode.appendChild(div);
        }
    }

    if (!valid && banner) {
        banner.classList.add('show');
        banner.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }

    return valid;
}

function initRealtimeValidation() {
    function clearError(e) {
        const el = e.target;
        if (!el.classList.contains('input-error')) return;
        el.classList.remove('input-error');
        el.parentNode.querySelector('.field-error-msg')?.remove();
    }
    document.addEventListener('input',  clearError);
    document.addEventListener('change', clearError);
}

// ─────────────────────────────────────────────────────────────────────────────
// 14. CENTRALISED SUBMIT HELPER
// ─────────────────────────────────────────────────────────────────────────────

async function submitForm({ btn, originalText, loadingText = 'Saving...', url, formData, tab, successMsg }) {
    setButtonLoading(btn, loadingText);
    try {
        const result = await postJson(url, formData);
        if (result.success) {
            showToast(successMsg, 'success');
            closeModal();
            persistTabAndReload(tab);
        } else {
            showToast(`Error: ${result.message}`, 'error');
            resetButton(btn, originalText);
        }
    } catch {
        showToast('A server error occurred. Please try again.', 'error');
        resetButton(btn, originalText);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 15. SAVE HANDLERS
// ─────────────────────────────────────────────────────────────────────────────

function initSaveHandlers() {

    // ── Registration ─────────────────────────────────────────────────────────
    on('saveRegistrationBtn', 'click', async function () {
        const isValid = validateForm(null, [
            { id: 'regFirstName', label: 'First name',    required: true },
            { id: 'regLastName',  label: 'Last name',     required: true },
            { id: 'regAge',       label: 'Age',           required: true, min: 16, max: 100 },
            { id: 'regPhone',     label: 'Phone number',  required: true },
            { id: 'regOrg',       label: 'Organization',  required: true },
        ]);
        if (!isValid) return;

        const userId = document.getElementById('regUserId')?.value;
        const partVal = document.getElementById('regParticipation')?.value;
        const verifStatusVal = (partVal === '2' || partVal === '4')
            ? (document.getElementById('regVerifStatus')?.value || 'None')
            : null;
        await submitForm({
            btn:         this,
            originalText:'Save Changes',
            url:         '?handler=SaveRegistration',
            formData:    buildFormData({
                id:                 userId,
                firstName:          document.getElementById('regFirstName')?.value,
                lastName:           document.getElementById('regLastName')?.value,
                age:                document.getElementById('regAge')?.value,
                phone:              document.getElementById('regPhone')?.value,
                academicTitle:      document.getElementById('regTitle')?.value,
                organization:       document.getElementById('regOrg')?.value,
                participation:      partVal,
                isForeigner:        document.getElementById('regForeignCheck')?.checked,
                paymentStatus:      document.getElementById('regPaymentStatus')?.value,
                emailConfirmed:     document.getElementById('regEmailConfirmed')?.checked,
                verificationStatus: verifStatusVal,
            }),
            tab:         'tab-registrations',
            successMsg:  'Registration updated successfully.',
        });
    });

    // ── Lecturer ──────────────────────────────────────────────────────────────
    on('saveLecturerBtn', 'click', async function () {
        const isNew = document.getElementById('lecturerId')?.value === '0';
        const ok = validateForm('lecturer-validation-banner', [
            { id: 'lecturerName_en',  label: 'Full Name (EN)',    required: true },
            { id: 'lecturerName_bg',  label: 'Full Name (BG)',    required: true },
            { id: 'lecturerRole_en',  label: 'Role (EN)',         required: true },
            { id: 'lecturerRole_bg',  label: 'Role (BG)',         required: true },
            { id: 'lecturerOrg_en',   label: 'Organization (EN)', required: true },
            { id: 'lecturerOrg_bg',   label: 'Organization (BG)', required: true },
        ], 'lecturerAvatar', isNew);
        if (!ok) return;

        const fd = buildFormData({
            Id:             document.getElementById('lecturerId')?.value || '0',
            FullNameEn:     document.getElementById('lecturerName_en')?.value,
            FullNameBg:     document.getElementById('lecturerName_bg')?.value,
            Category:       document.getElementById('lecturerCategory')?.value,
            RoleEn:         document.getElementById('lecturerRole_en')?.value,
            RoleBg:         document.getElementById('lecturerRole_bg')?.value,
            OrganizationEn: document.getElementById('lecturerOrg_en')?.value,
            OrganizationBg: document.getElementById('lecturerOrg_bg')?.value,
            BiographyEn:    document.getElementById('lecturerBio_en')?.value,
            BiographyBg:    document.getElementById('lecturerBio_bg')?.value,
            ProfileUrl:     document.getElementById('lecturerUrl')?.value,
        });
        appendFile(fd, 'lecturerAvatar', 'avatarFile');
        await submitForm({ btn: this, originalText: 'Save Lecturer', url: '?handler=SaveLecturer', formData: fd, tab: 'tab-lecturers', successMsg: 'Lecturer saved successfully.' });
    });

    // ── Event ─────────────────────────────────────────────────────────────────
    on('saveEventBtn', 'click', async function () {
        const isNew = document.getElementById('eventId')?.value === '0';
        const ok = validateForm('event-validation-banner', [
            { id: 'eventTitle_en',    label: 'Event Title (EN)', required: true },
            { id: 'eventTitle_bg',    label: 'Event Title (BG)', required: true },
            { id: 'eventLocation_en', label: 'Location (EN)',    required: true },
            { id: 'eventLocation_bg', label: 'Location (BG)',    required: true },
        ], 'eventImage', isNew);
        if (!ok) return;

        const fd = buildFormData({
            Id:         document.getElementById('eventId')?.value || '0',
            TitleEn:    document.getElementById('eventTitle_en')?.value,
            TitleBg:    document.getElementById('eventTitle_bg')?.value,
            LocationEn: document.getElementById('eventLocation_en')?.value,
            LocationBg: document.getElementById('eventLocation_bg')?.value,
            EventUrl:   document.getElementById('eventUrl')?.value,
        });
        appendFile(fd, 'eventImage', 'eventImage');
        await submitForm({ btn: this, originalText: 'Save Event', url: '?handler=SaveEvent', formData: fd, tab: 'tab-icbi', successMsg: 'Event saved successfully.' });
    });

    // ── Member ────────────────────────────────────────────────────────────────
    on('saveMemberBtn', 'click', async function () {
        const isNew = document.getElementById('memberId')?.value === '0';
        const ok = validateForm('member-validation-banner', [
            { id: 'memberName_en', label: 'Full Name (EN)',    required: true },
            { id: 'memberName_bg', label: 'Full Name (BG)',    required: true },
            { id: 'memberRole_en', label: 'Role (EN)',         required: true },
            { id: 'memberRole_bg', label: 'Role (BG)',         required: true },
            { id: 'memberOrg_en',  label: 'Organization (EN)', required: true },
            { id: 'memberOrg_bg',  label: 'Organization (BG)', required: true },
        ], 'memberAvatar', isNew);
        if (!ok) return;

        const fd = buildFormData({
            Id:             document.getElementById('memberId')?.value || '0',
            FullNameEn:     document.getElementById('memberName_en')?.value,
            FullNameBg:     document.getElementById('memberName_bg')?.value,
            RoleEn:         document.getElementById('memberRole_en')?.value,
            RoleBg:         document.getElementById('memberRole_bg')?.value,
            OrganizationEn: document.getElementById('memberOrg_en')?.value,
            OrganizationBg: document.getElementById('memberOrg_bg')?.value,
            CommitteeType:  document.getElementById('memberCommittee')?.value,
        });
        appendFile(fd, 'memberAvatar', 'avatarFile');
        await submitForm({ btn: this, originalText: 'Save Changes', url: '?handler=SaveMember', formData: fd, tab: 'tab-conference', successMsg: 'Committee member saved successfully.' });
    });

    // ── Partner ───────────────────────────────────────────────────────────────
    on('savePartnerBtn', 'click', async function () {
        const isNew = document.getElementById('partnerId')?.value === '0';
        const ok = validateForm('partner-validation-banner', [
            { id: 'partnerName_en', label: 'Partner Name (EN)', required: true },
            { id: 'partnerName_bg', label: 'Partner Name (BG)', required: true },
        ], 'partnerLogo', isNew);
        if (!ok) return;

        const fd = buildFormData({
            Id:         document.getElementById('partnerId')?.value || '0',
            NameEn:     document.getElementById('partnerName_en')?.value,
            NameBg:     document.getElementById('partnerName_bg')?.value,
            Category:   document.getElementById('partnerCategory')?.value,
            WebsiteUrl: document.getElementById('partnerUrl')?.value || '',
        });
        appendFile(fd, 'partnerLogo', 'logoFile');
        await submitForm({ btn: this, originalText: 'Save Partner', url: '?handler=SavePartner', formData: fd, tab: 'tab-conference', successMsg: 'Partner saved successfully.' });
    });

    // ── Session ───────────────────────────────────────────────────────────────
    on('saveSessionBtn', 'click', async function () {
        const ok = validateForm('session-validation-banner', [
            { id: 'sessionStartTime', label: 'Start time',         required: true },
            { id: 'sessionEndTime',   label: 'End time',           required: true },
            { id: 'sessionTitle_en',  label: 'Session Title (EN)', required: true },
            { id: 'sessionTitle_bg',  label: 'Session Title (BG)', required: true },
        ]);
        if (!ok) return;

        const start = document.getElementById('sessionStartTime')?.value;
        const end   = document.getElementById('sessionEndTime')?.value;
        if (start && end && start >= end) {
            showToast('End time must be later than start time.', 'error');
            return;
        }

        await submitForm({
            btn:         this,
            originalText:'Save Session',
            url:         '?handler=SaveSession',
            formData:    buildFormData({
                Id:           document.getElementById('sessionId')?.value || '0',
                Day:          document.getElementById('sessionDay')?.value,
                StartTime:    start,
                EndTime:      end,
                TitleEn:      document.getElementById('sessionTitle_en')?.value,
                TitleBg:      document.getElementById('sessionTitle_bg')?.value,
                SessionType:  document.getElementById('sessionType')?.value,
                SpeakerEn:    document.getElementById('sessionSpeaker_en')?.value,
                SpeakerBg:    document.getElementById('sessionSpeaker_bg')?.value,
                LocationEn:   document.getElementById('sessionLocation_en')?.value,
                LocationBg:   document.getElementById('sessionLocation_bg')?.value,
                DescriptionEn: document.getElementById('sessionDesc_en')?.value,
                DescriptionBg: document.getElementById('sessionDesc_bg')?.value,
                LiveStreamUrl: document.getElementById('sessionLiveStreamUrl')?.value || '',
            }),
            tab:         'tab-schedule',
            successMsg:  'Session saved successfully.',
        });
    });

    // ── Live Link (Само за конкретната сесия) ────────────────────────────────
    on('saveLiveLinkBtn', 'click', async function () {
        const ok = validateForm('livelink-validation-banner', [
            { id: 'liveLinkSessionId', label: 'Session',         required: true },
            { id: 'liveLinkUrl',       label: 'Live Stream URL', required: true,
              pattern: /^https?:\/\/.+/i, 
              patternMsg: 'Please enter a valid URL (starting with http:// or https://).'
            },
        ]);
        if (!ok) return;

        await submitForm({
            btn:         this,
            originalText:'Save Link',
            url:         '?handler=SaveSessionLiveLink',
            formData:    buildFormData({
                liveLinkSessionId: document.getElementById('liveLinkSessionId')?.value,
                liveLinkUrl:       document.getElementById('liveLinkUrl')?.value,
            }),
            tab:         'tab-schedule',
            successMsg:  'Live stream link saved successfully.',
        });
    });

    // ── Remove Live Link ─────────────────────────────────────────────────────
    document.querySelectorAll('.remove-stream-btn').forEach(btn => {
        btn.addEventListener('click', async function () {
            const id = this.dataset.id;
            const title = this.dataset.title;

            if (!confirm(`Are you sure you want to remove the live stream from "${title}"?`)) return;

            setButtonLoading(this, 'Removing...');
            try {
                const result = await postJson('?handler=SaveSessionLiveLink', buildFormData({
                    liveLinkSessionId: id,
                    liveLinkUrl: '' // празно изтрива линка
                }));

                if (result.success) {
                    showToast('Live stream link removed.', 'success');
                    persistTabAndReload('tab-schedule');
                } else {
                    showToast(`Error: ${result.message}`, 'error');
                    resetButton(this, 'Unlink');
                }
            } catch {
                showToast('A server error occurred.', 'error');
                resetButton(this, 'Unlink');
            }
        });
    });

    // ── Hotel ─────────────────────────────────────────────────────────────────
    on('saveHotelBtn', 'click', async function () {
        const ok = validateForm('hotel-validation-banner', [
            { id: 'hotelName_en', label: 'Hotel Name (EN)', required: true },
            { id: 'hotelName_bg', label: 'Hotel Name (BG)', required: true },
        ]);
        if (!ok) return;

        await submitForm({
            btn:         this,
            originalText:'Save Hotel',
            url:         '?handler=SaveHotel',
            formData:    buildFormData({
                Id:            document.getElementById('hotelId')?.value || '0',
                NameEn:        document.getElementById('hotelName_en')?.value,
                NameBg:        document.getElementById('hotelName_bg')?.value,
                DescriptionEn: document.getElementById('hotelDesc_en')?.value,
                DescriptionBg: document.getElementById('hotelDesc_bg')?.value,
                Url:           document.getElementById('hotelUrl')?.value,
            }),
            tab:         'tab-travel',
            successMsg:  'Hotel saved successfully.',
        });
    });

    // ── Social Links ─────────────────────────────────────────────────────────
    on('saveSocialLinksBtn', 'click', async function () {
        await submitForm({
            btn:         this,
            originalText:'Save Social Links',
            url:         '?handler=SaveSocialLinks',
            formData:    buildFormData({
                LinkedInUrl:  document.getElementById('socialLinkedIn')?.value  || '',
                XUrl:         document.getElementById('socialX')?.value         || '',
                InstagramUrl: document.getElementById('socialInstagram')?.value || '',
                FacebookUrl:  document.getElementById('socialFacebook')?.value  || '',
                TikTokUrl:    document.getElementById('socialTikTok')?.value    || '',
                YouTubeUrl:   document.getElementById('socialYouTube')?.value   || '',
            }),
            tab:         'tab-settings',
            successMsg:  'Social links saved successfully.',
        });
    });

    // ── Promo Slide ──────────────────────────────────────────────────────────
    on('savePromoBtn', 'click', async function () {
        const isNew = document.getElementById('promoId')?.value === '0';
        const ok = validateForm('promo-validation-banner', [
            { id: 'promoTitle_en', label: 'Title (EN)',       required: true },
            { id: 'promoTitle_bg', label: 'Title (BG)',       required: true },
            { id: 'promoDesc_en',  label: 'Description (EN)', required: true },
            { id: 'promoDesc_bg',  label: 'Description (BG)', required: true },
        ], 'promoImage', isNew);
        if (!ok) return;

        const fd = buildFormData({
            Id:            document.getElementById('promoId')?.value || '0',
            TitleEn:       document.getElementById('promoTitle_en')?.value,
            TitleBg:       document.getElementById('promoTitle_bg')?.value,
            DescriptionEn: document.getElementById('promoDesc_en')?.value,
            DescriptionBg: document.getElementById('promoDesc_bg')?.value,
        });
        appendFile(fd, 'promoImage', 'imageFile');
        await submitForm({ btn: this, originalText: 'Save Promo Slide', url: '?handler=SavePromo', formData: fd, tab: 'tab-settings', successMsg: 'Promo slide saved successfully.' });
    });

    // ── FAQ ───────────────────────────────────────────────────────────
    on('saveFaqBtn', 'click', async function () {
        const ok = validateForm('faq-validation-banner', [
            { id: 'faqQuestion_en', label: 'Question (EN)', required: true },
            { id: 'faqQuestion_bg', label: 'Question (BG)', required: true },
            { id: 'faqAnswer_en',   label: 'Answer (EN)',   required: true },
            { id: 'faqAnswer_bg',   label: 'Answer (BG)',   required: true },
        ]);
        if (!ok) return;

        await submitForm({
            btn:         this,
            originalText:'Save FAQ',
            url:         '?handler=SaveFaq',
            formData:    buildFormData({
                Id:         document.getElementById('faqId')?.value || '0',
                QuestionEn: document.getElementById('faqQuestion_en')?.value,
                QuestionBg: document.getElementById('faqQuestion_bg')?.value,
                AnswerEn:   document.getElementById('faqAnswer_en')?.value,
                AnswerBg:   document.getElementById('faqAnswer_bg')?.value,
            }),
            tab:         'tab-faq',
            successMsg:  'FAQ saved successfully.',
        });
    });

    // ── Footer Content (tagline / org note / contact) ───────────────────────
    on('saveFooterContentBtn', 'click', async function () {
        // EMAIL_RE огледално следва regex-а в OnPostSaveFooterContentAsync
        // (Index.cshtml.cs) — държи ги в синхрон ръчно, ако някога се
        // променя единия, обнови и другия.
        const EMAIL_RE = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;
        const ok = validateForm('footer-content-validation-banner', [
            { id: 'footerBrandTagline_en',    label: 'Brand Tagline (EN)',        maxLength: 45 },
            { id: 'footerBrandTagline_bg',    label: 'Brand Tagline (BG)',        maxLength: 45 },
            { id: 'footerOrgNote_en',         label: '"Organized By" Note (EN)',  required: true, maxLength: 400 },
            { id: 'footerOrgNote_bg',         label: '"Organized By" Note (BG)',  required: true, maxLength: 400 },
            { id: 'footerContactLocation_en', label: 'Address / Location (EN)',   required: true, maxLength: 100 },
            { id: 'footerContactLocation_bg', label: 'Address / Location (BG)',   required: true, maxLength: 100 },
            { id: 'footerContactEmail',       label: 'Contact email',             required: true, maxLength: 150,
              pattern: EMAIL_RE, patternMsg: 'Please enter a valid email address.' },
            { id: 'footerContactPhone',       label: 'Contact phone',             required: true, maxLength: 30 },
        ]);
        if (!ok) return;

        await submitForm({
            btn:         this,
            originalText:'Save Footer Content',
            url:         '?handler=SaveFooterContent',
            formData:    buildFormData({
                brandTaglineEn:    document.getElementById('footerBrandTagline_en')?.value    || '',
                brandTaglineBg:    document.getElementById('footerBrandTagline_bg')?.value    || '',
                orgNoteEn:         document.getElementById('footerOrgNote_en')?.value          || '',
                orgNoteBg:         document.getElementById('footerOrgNote_bg')?.value          || '',
                contactLocationEn: document.getElementById('footerContactLocation_en')?.value  || '',
                contactLocationBg: document.getElementById('footerContactLocation_bg')?.value  || '',
                contactEmail:      document.getElementById('footerContactEmail')?.value        || '',
                contactPhone:      document.getElementById('footerContactPhone')?.value        || '',
            }),
            tab:         'tab-settings',
            successMsg:  'Footer content saved successfully.',
        });
    });

    // ── Footer Quick Link ────────────────────────────────────────────────────
    on('saveFooterLinkBtn', 'click', async function () {
        const ok = validateForm('footerlink-validation-banner', [
            { id: 'footerLinkLabel_en', label: 'Label (EN)',    required: true, maxLength: 60 },
            { id: 'footerLinkLabel_bg', label: 'Label (BG)',    required: true, maxLength: 60 },
            { id: 'footerLinkUrl',      label: 'URL / Path',    required: true, maxLength: 300 },
            { id: 'footerLinkIconSvg',  label: 'SVG icon code', required: true, maxLength: 2000 },
        ]);
        if (!ok) return;

        await submitForm({
            btn:         this,
            originalText:'Save Quick Link',
            url:         '?handler=SaveFooterLink',
            formData:    buildFormData({
                Id:      document.getElementById('footerLinkId')?.value || '0',
                LabelEn: document.getElementById('footerLinkLabel_en')?.value,
                LabelBg: document.getElementById('footerLinkLabel_bg')?.value,
                Url:     document.getElementById('footerLinkUrl')?.value,
                IconSvg: document.getElementById('footerLinkIconSvg')?.value,
            }),
            tab:         'tab-settings',
            successMsg:  'Quick link saved successfully.',
        });
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// 16. UTILITY HELPERS
// ─────────────────────────────────────────────────────────────────────────────

function getCsrfToken() {
    return document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
}

// UTF-8-safe base64 encode — plain btoa() only handles Latin1 and would
// corrupt/throw on Cyrillic (Bulgarian) content. Goes through TextEncoder to
// get real UTF-8 bytes first. Shared by initPrivacyEditor and
// initCookieNoticeEditor — both send rich-text HTML that needs this same
// WAF-safe wrapping before the request leaves the browser.
function utf8ToBase64(str) {
    const bytes = new TextEncoder().encode(str);
    let binary = '';
    bytes.forEach(function (b) { binary += String.fromCharCode(b); });
    return btoa(binary);
}

function buildFormData(fields = {}) {
    const fd = new FormData();
    fd.append('__RequestVerificationToken', getCsrfToken());
    for (const [k, v] of Object.entries(fields)) {
        if (v !== undefined && v !== null) fd.append(k, v);
    }
    return fd;
}

function appendFile(fd, inputId, fieldName) {
    const input = document.getElementById(inputId);
    if (input?.files?.length) fd.append(fieldName, input.files[0]);
}

async function postJson(url, formData) {
    const res = await fetch(url, { method: 'POST', body: formData });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return res.json();
}

function setText(id, text) {
    const el = document.getElementById(id);
    if (el) el.textContent = text ?? '—';
}

function setVal(id, val) {
    const el = document.getElementById(id);
    if (el) el.value = val ?? '';
}

function setChecked(id, checked) {
    const el = document.getElementById(id);
    if (el) el.checked = !!checked;
}

function setButtonLoading(btn, text) {
    btn.disabled     = true;
    btn.textContent  = text;
    btn.style.opacity = '0.7';
}

function resetButton(btn, text) {
    btn.disabled     = false;
    btn.innerHTML    = text;
    btn.style.opacity = '1';
}

function persistTabAndReload(tab, delay = 900) {
    if (tab) localStorage.setItem('activeAdminTab', tab);
    setTimeout(() => window.location.reload(), delay);
}

/** Attach event listener to element by ID (no-op if not found). */
function on(id, event, handler) {
    document.getElementById(id)?.addEventListener(event, handler);
}

/** Hide/show the required-star span based on whether record is new. */
function toggleRequiredStar(starId, recordId) {
    const star = document.getElementById(starId);
    if (star) star.style.display = recordId && recordId !== '0' ? 'none' : '';
}

/** Update a field's "N / 120" counter and colour it as the limit approaches. */
function updateCharCounter(fieldId) {
    const field   = document.getElementById(fieldId);
    const counter = document.getElementById(fieldId + '_counter');
    if (!field || !counter) return;

    const max = Number(field.getAttribute('maxlength')) || 120;
    const len = field.value.length;
    counter.textContent = `${len} / ${max}`;
    counter.classList.toggle('is-near-limit', len >= max - 15 && len < max);
    counter.classList.toggle('is-at-limit', len >= max);
}

function initCharCounters() {
    const fields = [
        'promoTitle_en', 'promoTitle_bg', 'promoDesc_en', 'promoDesc_bg',
        // Footer Content
        'footerBrandTagline_en', 'footerBrandTagline_bg',
        'footerOrgNote_en', 'footerOrgNote_bg',
        'footerContactLocation_en', 'footerContactLocation_bg',
        'footerContactEmail', 'footerContactPhone',
        // Footer Quick Link modal
        'footerLinkLabel_en', 'footerLinkLabel_bg', 'footerLinkUrl', 'footerLinkIconSvg',
    ];
    fields.forEach(id => {
        const field = document.getElementById(id);
        if (!field) return;
        field.addEventListener('input', () => updateCharCounter(id));
        // Обнови веднага при зареждане — иначе полета, вече попълнени от
        // базата (напр. Footer Content, което идва seed-нато), показват
        // "0 / 100" докато потребителят не напише нещо, макар полето да
        // не е празно.
        updateCharCounter(id);
    });
}

/** Set element text and colour based on a value-to-colour map. */
function setColoredStatus(id, value, colorMap, defaultColor = '#333') {
    const el = document.getElementById(id);
    if (!el) return;
    el.textContent = value ?? '—';
    el.style.color = colorMap[value] ?? defaultColor;
}

/** HTML-escape a string. */
function escHtml(str) {
    if (!str) return '';
    return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

// ─────────────────────────────────────────────────────────────────────────────
// 17. BOOTSTRAP
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// VERIFICATION CARD EXPAND / COLLAPSE
// ─────────────────────────────────────────────────────────────────────────────

// ─────────────────────────────────────────────────────────────────────────────
// PRIVACY POLICY / GDPR — Quill editors + save
// ─────────────────────────────────────────────────────────────────────────────

function initPrivacyEditor() {
    const enContainer = document.getElementById('privacyEditorEn');
    const bgContainer = document.getElementById('privacyEditorBg');
    if (!enContainer || !bgContainer || typeof Quill === 'undefined') return;

    const toolbarOptions = [
        [{ header: [2, 3, false] }],
        ['bold', 'italic', 'underline', 'strike'],
        [{ color: [] }, { background: [] }],
        [{ align: [] }],
        [{ list: 'ordered' }, { list: 'bullet' }, { indent: '-1' }, { indent: '+1' }],
        ['blockquote', 'link', 'image'],
        ['clean'],
    ];

    const quillEn = new Quill(enContainer, { theme: 'snow', modules: { toolbar: toolbarOptions } });
    const quillBg = new Quill(bgContainer, { theme: 'snow', modules: { toolbar: toolbarOptions } });

    // Seed from the hidden <textarea>s the server rendered — .innerHTML (not
    // .textContent) so Quill parses the existing HTML into its delta model
    // instead of showing the raw tags as literal text.
    const seedEn = document.getElementById('privacyContentEnSeed');
    const seedBg = document.getElementById('privacyContentBgSeed');
    if (seedEn) quillEn.root.innerHTML = seedEn.value;
    if (seedBg) quillBg.root.innerHTML = seedBg.value;

    on('savePrivacyPolicyBtn', 'click', async function () {
        // FIX: send base64, not raw HTML — a request body containing raw HTML
        // (especially with embedded base64 <img> data from the Quill image
        // button) is exactly the kind of payload WAF/Cloudflare managed rules
        // flag as a malware payload, same reason SendInvitations base64-encodes
        // its template. The server decodes this back before saving — nothing
        // else about the flow changes.
        const contentEn = utf8ToBase64(quillEn.root.innerHTML.trim());
        const contentBg = utf8ToBase64(quillBg.root.innerHTML.trim());

        setButtonLoading(this, 'Saving...');
        try {
            const result = await postJson('?handler=SavePrivacyPolicy',
                buildFormData({ contentEn, contentBg }));
            if (result.success) {
                showToast('Privacy Policy updated — live on the public page now.', 'success');
                // The hint text above the editors was rendered server-side at page
                // load — without this it would show the OLD save time until the
                // next full reload, which is exactly the "2 hours behind"-looking
                // staleness bug. The admin's own clock IS already local time, so
                // no timezone conversion is needed here (unlike the server-side
                // UTC values elsewhere).
                const stampEl = document.getElementById('privacyLastUpdatedText');
                if (stampEl) {
                    const now = new Date();
                    const day = String(now.getDate()).padStart(2, '0');
                    const month = now.toLocaleString('en-GB', { month: 'short' });
                    const year = now.getFullYear();
                    const time = now.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
                    stampEl.textContent = `${day} ${month} ${year}, ${time}`;
                }
            } else {
                showToast(`Error: ${result.message}`, 'error');
            }
        } catch {
            showToast('A server error occurred. Please try again.', 'error');
        } finally {
            resetButton(this, 'Save Privacy Policy');
        }
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// TERMS OF USE — Quill editors + save (mirrors initPrivacyEditor exactly)
// ─────────────────────────────────────────────────────────────────────────────

function initTermsEditor() {
    const enContainer = document.getElementById('termsEditorEn');
    const bgContainer = document.getElementById('termsEditorBg');
    if (!enContainer || !bgContainer || typeof Quill === 'undefined') return;

    const toolbarOptions = [
        [{ header: [2, 3, false] }],
        ['bold', 'italic', 'underline', 'strike'],
        [{ color: [] }, { background: [] }],
        [{ align: [] }],
        [{ list: 'ordered' }, { list: 'bullet' }, { indent: '-1' }, { indent: '+1' }],
        ['blockquote', 'link', 'image'],
        ['clean'],
    ];

    const quillEn = new Quill(enContainer, { theme: 'snow', modules: { toolbar: toolbarOptions } });
    const quillBg = new Quill(bgContainer, { theme: 'snow', modules: { toolbar: toolbarOptions } });

    const seedEn = document.getElementById('termsContentEnSeed');
    const seedBg = document.getElementById('termsContentBgSeed');
    if (seedEn) quillEn.root.innerHTML = seedEn.value;
    if (seedBg) quillBg.root.innerHTML = seedBg.value;

    on('saveTermsOfUseBtn', 'click', async function () {
        // Same base64 wrapping as Privacy Policy — WAF/Cloudflare managed
        // rules reason, see initPrivacyEditor above.
        const contentEn = utf8ToBase64(quillEn.root.innerHTML.trim());
        const contentBg = utf8ToBase64(quillBg.root.innerHTML.trim());

        setButtonLoading(this, 'Saving...');
        try {
            const result = await postJson('?handler=SaveTermsOfUse',
                buildFormData({ contentEn, contentBg }));
            if (result.success) {
                showToast('Terms of Use updated — live on the public page now.', 'success');
                const stampEl = document.getElementById('termsLastUpdatedText');
                if (stampEl) {
                    const now = new Date();
                    const day = String(now.getDate()).padStart(2, '0');
                    const month = now.toLocaleString('en-GB', { month: 'short' });
                    const year = now.getFullYear();
                    const time = now.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
                    stampEl.textContent = `${day} ${month} ${year}, ${time}`;
                }
            } else {
                showToast(`Error: ${result.message}`, 'error');
            }
        } catch {
            showToast('A server error occurred. Please try again.', 'error');
        } finally {
            resetButton(this, 'Save Terms of Use');
        }
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// COOKIE NOTICE — banner text Quill editor (mirrors initPrivacyEditor exactly)
// ─────────────────────────────────────────────────────────────────────────────

function initCookieNoticeEditor() {
    const enContainer = document.getElementById('cookieNoticeEditorEn');
    const bgContainer = document.getElementById('cookieNoticeEditorBg');
    if (!enContainer || !bgContainer || typeof Quill === 'undefined') return;

    const toolbarOptions = [
        [{ header: [2, 3, false] }],
        ['bold', 'italic', 'underline'],
        ['link'],
        ['clean'],
    ];

    const quillEn = new Quill(enContainer, { theme: 'snow', modules: { toolbar: toolbarOptions } });
    const quillBg = new Quill(bgContainer, { theme: 'snow', modules: { toolbar: toolbarOptions } });

    const seedEn = document.getElementById('cookieNoticeContentEnSeed');
    const seedBg = document.getElementById('cookieNoticeContentBgSeed');
    if (seedEn) quillEn.root.innerHTML = seedEn.value;
    if (seedBg) quillBg.root.innerHTML = seedBg.value;

    on('saveCookieNoticeBtn', 'click', async function () {
        // Same base64 wrapping as Privacy Policy — WAF/Cloudflare managed
        // rules flag raw HTML request bodies, this sidesteps that.
        const contentEn = utf8ToBase64(quillEn.root.innerHTML.trim());
        const contentBg = utf8ToBase64(quillBg.root.innerHTML.trim());

        setButtonLoading(this, 'Saving...');
        try {
            const result = await postJson('?handler=SaveCookieNotice',
                buildFormData({ contentEn, contentBg }));
            if (result.success) {
                showToast('Cookie banner text updated — live on the site now.', 'success');
                const stampEl = document.getElementById('cookieNoticeLastUpdatedText');
                if (stampEl) {
                    const now = new Date();
                    const day = String(now.getDate()).padStart(2, '0');
                    const month = now.toLocaleString('en-GB', { month: 'short' });
                    const year = now.getFullYear();
                    const time = now.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
                    stampEl.textContent = `${day} ${month} ${year}, ${time}`;
                }
            } else {
                showToast(`Error: ${result.message}`, 'error');
            }
        } catch {
            showToast('A server error occurred. Please try again.', 'error');
        } finally {
            resetButton(this, 'Save Banner Text');
        }
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// COOKIE POLICY PAGE — /Cookies page body content (mirrors initCookieNoticeEditor)
// ─────────────────────────────────────────────────────────────────────────────

function initCookiePolicyEditor() {
    const enContainer = document.getElementById('cookiePolicyEditorEn');
    const bgContainer = document.getElementById('cookiePolicyEditorBg');
    if (!enContainer || !bgContainer || typeof Quill === 'undefined') return;

    const toolbarOptions = [
        [{ header: [2, 3, false] }],
        ['bold', 'italic', 'underline'],
        ['link'],
        [{ list: 'ordered' }, { list: 'bullet' }],
        ['clean'],
    ];

    const quillEn = new Quill(enContainer, { theme: 'snow', modules: { toolbar: toolbarOptions } });
    const quillBg = new Quill(bgContainer, { theme: 'snow', modules: { toolbar: toolbarOptions } });

    const seedEn = document.getElementById('cookiePolicyContentEnSeed');
    const seedBg = document.getElementById('cookiePolicyContentBgSeed');
    if (seedEn) quillEn.root.innerHTML = seedEn.value;
    if (seedBg) quillBg.root.innerHTML = seedBg.value;

    on('saveCookiePolicyBtn', 'click', async function () {
        const contentEn = utf8ToBase64(quillEn.root.innerHTML.trim());
        const contentBg = utf8ToBase64(quillBg.root.innerHTML.trim());

        setButtonLoading(this, 'Saving...');
        try {
            const result = await postJson('?handler=SaveCookiePolicy',
                buildFormData({ contentEn, contentBg }));
            if (result.success) {
                showToast('Cookie Policy page updated — live on /Cookies now.', 'success');
                const stampEl = document.getElementById('cookiePolicyLastUpdatedText');
                if (stampEl) {
                    const now = new Date();
                    const day = String(now.getDate()).padStart(2, '0');
                    const month = now.toLocaleString('en-GB', { month: 'short' });
                    const year = now.getFullYear();
                    const time = now.toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' });
                    stampEl.textContent = `${day} ${month} ${year}, ${time}`;
                }
            } else {
                showToast(`Error: ${result.message}`, 'error');
            }
        } catch {
            showToast('A server error occurred. Please try again.', 'error');
        } finally {
            resetButton(this, 'Save Cookie Policy Page');
        }
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// COOKIE NOTICE — category CRUD (add/edit/delete)
// ─────────────────────────────────────────────────────────────────────────────

function initCookieCategories() {
    const list = document.getElementById('cookieCategoryList');
    const addBtn = document.getElementById('addCookieCategoryBtn');
    if (!list || !addBtn) return;

    const modalTitle = document.getElementById('cookieModalTitle');
    const keyInput = document.getElementById('cookieModalKeyInput');
    const keyBadge = document.getElementById('cookieModalKeyBadge');
    const nameEnInput = document.getElementById('cookieModalNameEn');
    const nameBgInput = document.getElementById('cookieModalNameBg');
    const visibleCheckbox = document.getElementById('cookieModalVisible');
    const toggleableCheckbox = document.getElementById('cookieModalToggleable');
    const lockedHint = document.getElementById('cookieModalLockedHint');
    const saveBtn = document.getElementById('cookieModalSaveBtn');

    let currentEditId = 0;   // 0 = creating a new category
    let currentEditKey = '';

    // ONE pair of Quill instances, created once and reused for every open —
    // we just swap .root.innerHTML for whichever category is being edited,
    // instead of paying the cost of creating/destroying editors repeatedly.
    const descEnContainer = document.getElementById('cookieModalDescEditorEn');
    const descBgContainer = document.getElementById('cookieModalDescEditorBg');
    let quillDescEn = null, quillDescBg = null;
    if (descEnContainer && descBgContainer && typeof Quill !== 'undefined') {
        const toolbarOptions = [['bold', 'italic', 'underline'], ['link'], ['clean']];
        quillDescEn = new Quill(descEnContainer, { theme: 'snow', modules: { toolbar: toolbarOptions } });
        quillDescBg = new Quill(descBgContainer, { theme: 'snow', modules: { toolbar: toolbarOptions } });
    }

    function openCategoryModal(row) {
        if (row) {
            currentEditId = parseInt(row.getAttribute('data-id'), 10);
            currentEditKey = row.getAttribute('data-key');
            modalTitle.textContent = 'Edit Category';
            keyInput.style.display = 'none';
            keyBadge.style.display = 'inline-flex';
            keyBadge.textContent = currentEditKey;
            nameEnInput.value = row.getAttribute('data-name-en') || '';
            nameBgInput.value = row.getAttribute('data-name-bg') || '';
            if (quillDescEn) quillDescEn.root.innerHTML = row.getAttribute('data-desc-en') || '';
            if (quillDescBg) quillDescBg.root.innerHTML = row.getAttribute('data-desc-bg') || '';
            visibleCheckbox.checked = row.getAttribute('data-visible') === 'true';
            toggleableCheckbox.checked = row.getAttribute('data-toggleable') === 'true';

            const isNecessary = currentEditKey === 'necessary';
            toggleableCheckbox.disabled = isNecessary;
            lockedHint.style.display = isNecessary ? 'block' : 'none';
        } else {
            currentEditId = 0;
            currentEditKey = '';
            modalTitle.textContent = 'Add New Category';
            keyInput.style.display = '';
            keyInput.value = '';
            keyBadge.style.display = 'none';
            nameEnInput.value = '';
            nameBgInput.value = '';
            if (quillDescEn) quillDescEn.root.innerHTML = '';
            if (quillDescBg) quillDescBg.root.innerHTML = '';
            visibleCheckbox.checked = true;
            toggleableCheckbox.checked = true;
            toggleableCheckbox.disabled = false;
            lockedHint.style.display = 'none';
        }
        openModal('cookieCategoryModal');
        setTimeout(() => (currentEditId === 0 ? keyInput : nameEnInput).focus(), 60);
    }

    list.querySelectorAll('.cookie-edit-btn').forEach(function (btn) {
        btn.addEventListener('click', function () { openCategoryModal(btn.closest('.cookie-category-row')); });
    });
    addBtn.addEventListener('click', function () { openCategoryModal(null); });

    list.querySelectorAll('.cookie-delete-btn').forEach(function (btn) {
        btn.addEventListener('click', async function () {
            if (!window.confirm('Delete this cookie category? This cannot be undone.')) return;
            const id = btn.getAttribute('data-id');
            setButtonLoading(btn, 'Deleting...');
            try {
                const result = await postJson('?handler=DeleteCookieCategory', buildFormData({ id }));
                if (result.success) {
                    btn.closest('.cookie-category-row').remove();
                    showToast('Category deleted.', 'success');
                } else {
                    showToast(`Error: ${result.message}`, 'error');
                    resetButton(btn, 'Delete');
                }
            } catch {
                showToast('A server error occurred. Please try again.', 'error');
                resetButton(btn, 'Delete');
            }
        });
    });

    saveBtn.addEventListener('click', async function () {
        const isNew = currentEditId === 0;
        const key = isNew ? keyInput.value.trim() : currentEditKey;
        const nameEn = nameEnInput.value.trim();
        const nameBg = nameBgInput.value.trim();

        if (isNew && !key) { showToast('Please enter a key for the new category (e.g. "functional").', 'error'); return; }
        if (!nameEn || !nameBg) { showToast('Both English and Bulgarian names are required.', 'error'); return; }

        const descriptionEn = quillDescEn ? utf8ToBase64(quillDescEn.root.innerHTML.trim()) : '';
        const descriptionBg = quillDescBg ? utf8ToBase64(quillDescBg.root.innerHTML.trim()) : '';

        setButtonLoading(this, 'Saving...');
        try {
            const result = await postJson('?handler=SaveCookieCategory', buildFormData({
                id: currentEditId, key, nameEn, nameBg, descriptionEn, descriptionBg,
                isVisible: visibleCheckbox.checked, isToggleable: toggleableCheckbox.checked,
            }));
            if (result.success) {
                showToast('Category saved.', 'success');
                closeModal();
                // Simplest reliable way to reflect every possible field change
                // (including a brand-new row) in the compact list — this is an
                // internal admin tool, a reload here is a fair trade for not
                // having to hand-patch the DOM for every field.
                persistTabAndReload('tab-cookies');
            } else {
                showToast(`Error: ${result.message}`, 'error');
                resetButton(this, 'Save Category');
            }
        } catch {
            showToast('A server error occurred. Please try again.', 'error');
            resetButton(this, 'Save Category');
        }
    });
}

function initVerifCardExpand() {
    // Toggle function — accessible globally for backward compat
    window.toggleVerifCard = function (userId) {
        const body   = document.getElementById('vpc-body-'   + userId);
        const toggle = document.getElementById('vpc-toggle-' + userId);
        if (!body || !toggle) return;
        const isOpen = body.classList.contains('open');
        body.classList.toggle('open', !isOpen);
        toggle.classList.toggle('open', !isOpen);
    };

    // Event delegation — handles header clicks and expand button
    document.addEventListener('click', function (e) {
        // 1. Direct click on expand button
        const expandBtn = e.target.closest('.verif-expand-btn');
        if (expandBtn) {
            e.stopPropagation();
            const id = expandBtn.id.replace('vpc-toggle-', '');
            window.toggleVerifCard(id);
            return;
        }

        // 2. Click on header — but NOT on action buttons/links inside it
        const header = e.target.closest('.verif-pending-card-header');
        if (header) {
            if (e.target.closest('.verif-pending-actions')) return;
            if (e.target.closest('a, button')) return;
            const id = header.id ? header.id.replace('vpc-header-', '') : null;
            if (id) window.toggleVerifCard(id);
        }
    });
}

document.addEventListener('DOMContentLoaded', () => {
    // Хамбургер менюто вече идва изцяло от scriptMain.js (зареден веднъж
    // чрез споделения _Layout.cshtml) — не го дублираме тук.

    initTabs();
    initModals();
    initDeleteConfirm();
    initPaymentActions();
    initVerificationActions();
    initFilters();
    initImageZoom();
    initImagePreviews();
    initCharCounters();
    
    // Toggles & Drag-and-drop
    initPromoToggleActive();
    initDragDrop('promoSlideList', savePromoOrder);
    
    initFaqToggleActive();
    initDragDrop('faqList', saveFaqOrder);

    initFooterLinkToggleActive();
    initEmailNotificationToggles();
    initPaymentGates();
    initClearInactiveCrypto();
    initFooterLinkIconPreview();

    initTicketPriceSync();
    initRealtimeValidation();
    initSaveHandlers();
    initVerifCardExpand();
    initPrivacyEditor();
    initTermsEditor();
    initCookieNoticeEditor();
    initCookiePolicyEditor();
    initCookieCategories();
});