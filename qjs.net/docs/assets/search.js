// Documentation search.
// Two layers:
//   1) In-page filter — hides <section data-keywords="..."> blocks that don't
//      match the current query. Blocks without data-keywords stay visible.
//   2) Cross-page index — loaded once from search-index.json. Renders hits
//      below the search bar with links to the matching section.
(function () {
    const input = document.getElementById('docsearch');
    const results = document.getElementById('search-results');
    if (!input || !results) return;

    let index = null;
    let indexLoaded = false;

    // Try to fetch the cross-page index. May fail under file:// in some
    // browsers; we degrade gracefully to in-page filtering only.
    function loadIndex() {
        if (indexLoaded) return Promise.resolve();
        indexLoaded = true;
        return fetch('search-index.json', { cache: 'no-cache' })
            .then(r => r.ok ? r.json() : null)
            .then(j => { index = j; })
            .catch(() => { index = null; });
    }

    function tokenize(q) {
        return q.toLowerCase().split(/\s+/).filter(Boolean);
    }

    function matches(haystack, tokens) {
        for (const t of tokens) if (haystack.indexOf(t) === -1) return false;
        return true;
    }

    function filterPage(tokens) {
        const sections = document.querySelectorAll('section[data-keywords]');
        let anyMatch = false;
        sections.forEach(s => {
            if (tokens.length === 0) {
                s.style.display = '';
                s.classList.remove('hit-highlight');
                return;
            }
            const hay = (s.dataset.keywords + ' ' + s.textContent).toLowerCase();
            const ok = matches(hay, tokens);
            s.style.display = ok ? '' : 'none';
            s.classList.toggle('hit-highlight', ok);
            if (ok) anyMatch = true;
        });
        return anyMatch;
    }

    function renderResults(tokens) {
        if (tokens.length === 0 || !index) {
            results.classList.add('hidden');
            results.innerHTML = '';
            return;
        }
        const hits = [];
        for (const entry of index) {
            const hay = (entry.title + ' ' + entry.keywords + ' ' + entry.body).toLowerCase();
            if (matches(hay, tokens)) hits.push(entry);
            if (hits.length >= 30) break;
        }
        results.classList.remove('hidden');
        if (hits.length === 0) {
            results.innerHTML = '<div class="empty">No cross-page matches.</div>';
            return;
        }
        results.innerHTML = hits.map(h =>
            `<div class="row"><a href="${h.page}#${h.id}"><span class="ttl">${escape(h.title)}</span><span class="pg">${escape(h.pageTitle)}</span></a></div>`
        ).join('');
    }

    function escape(s) {
        return s.replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
    }

    let timer = null;
    input.addEventListener('input', () => {
        clearTimeout(timer);
        timer = setTimeout(() => {
            const tokens = tokenize(input.value);
            filterPage(tokens);
            loadIndex().then(() => renderResults(tokens));
        }, 80);
    });

    // Restore query from URL hash like #q=...
    const m = location.hash.match(/q=([^&]+)/);
    if (m) {
        input.value = decodeURIComponent(m[1]);
        const tokens = tokenize(input.value);
        filterPage(tokens);
        loadIndex().then(() => renderResults(tokens));
    }
})();
