/* DocumentGenerationHTML — site interactivity.
   Theme toggle, mobile sidebar, scroll progress, scrollspy TOC,
   copy-to-clipboard, client-side search, mermaid init. Vanilla JS, no deps. */
(function () {
  'use strict';

  // ---------- Theme ----------
  var THEME_KEY = 'dgh-theme';
  var root = document.documentElement;

  function applyTheme(theme) {
    root.setAttribute('data-theme', theme);
    var meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute('content', theme === 'dark' ? '#0f1115' : '#ffffff');
    if (window.mermaid) {
      try { window.mermaid.initialize({ startOnLoad: false, theme: theme === 'dark' ? 'dark' : 'default', securityLevel: 'loose' }); }
      catch (e) {}
      reRenderMermaid();
    }
  }
  function reRenderMermaid() {
    if (!window.mermaid) return;
    var blocks = document.querySelectorAll('.mermaid');
    blocks.forEach(function (b) {
      if (b.dataset.orig === undefined) b.dataset.orig = b.textContent;
      b.removeAttribute('data-processed');
      b.innerHTML = b.dataset.orig;
    });
    try { window.mermaid.run({ nodes: Array.from(blocks) }); } catch (e) {}
  }
  function initTheme() {
    var saved = localStorage.getItem(THEME_KEY);
    var prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    var theme = saved || (prefersDark ? 'dark' : 'light');
    applyTheme(theme);
    var btn = document.getElementById('themeToggle');
    if (btn) btn.addEventListener('click', function () {
      var next = root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
      localStorage.setItem(THEME_KEY, next);
      applyTheme(next);
    });
  }

  // ---------- Mobile sidebar ----------
  function initSidebar() {
    var toggle = document.getElementById('sidebarToggle');
    var sidebar = document.getElementById('sidebar');
    var overlay = document.getElementById('sidebarOverlay');
    if (!toggle || !sidebar) return;
    function open() { sidebar.classList.add('open'); overlay.classList.add('show'); toggle.setAttribute('aria-expanded', 'true'); }
    function close() { sidebar.classList.remove('open'); overlay.classList.remove('show'); toggle.setAttribute('aria-expanded', 'false'); }
    toggle.addEventListener('click', function () { sidebar.classList.contains('open') ? close() : open(); });
    if (overlay) overlay.addEventListener('click', close);
    document.addEventListener('keydown', function (e) { if (e.key === 'Escape') close(); });
  }

  // ---------- Scroll progress ----------
  function initProgress() {
    var fill = document.getElementById('progressFill');
    if (!fill) return;
    function update() {
      var h = document.documentElement;
      var scrolled = h.scrollTop;
      var total = h.scrollHeight - h.clientHeight;
      fill.style.width = (total > 0 ? (scrolled / total) * 100 : 0) + '%';
    }
    window.addEventListener('scroll', update, { passive: true });
    update();
  }

  // ---------- Scrollspy TOC ----------
  function initTOC() {
    var tocNav = document.getElementById('tocNav');
    var toc = document.getElementById('toc');
    var content = document.getElementById('content');
    if (!tocNav || !toc || !content) return;
    var headings = content.querySelectorAll('h2, h3');
    if (!headings.length) { toc.style.display = 'none'; return; }

    headings.forEach(function (h, i) {
      if (!h.id) h.id = 'sec-' + i;
      var a = document.createElement('a');
      a.href = '#' + h.id;
      a.className = 'toc-link' + (h.tagName === 'H3' ? ' toc-h3' : '');
      a.textContent = h.textContent;
      a.addEventListener('click', function (e) {
        e.preventDefault();
        h.scrollIntoView({ behavior: 'smooth', block: 'start' });
        history.replaceState(null, '', '#' + h.id);
      });
      tocNav.appendChild(a);
    });

    var links = tocNav.querySelectorAll('.toc-link');
    var observer = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          links.forEach(function (l) { l.classList.remove('active'); });
          var active = tocNav.querySelector('a[href="#' + entry.target.id + '"]');
          if (active) active.classList.add('active');
        }
      });
    }, { rootMargin: '-80px 0px -70% 0px', threshold: 0 });
    headings.forEach(function (h) { observer.observe(h); });
  }

  // ---------- Copy buttons ----------
  function initCopy() {
    document.querySelectorAll('pre').forEach(function (pre) {
      if (pre.classList.contains('mermaid-pre') || pre.querySelector('.copy-btn')) return;
      var btn = document.createElement('button');
      btn.className = 'copy-btn';
      btn.type = 'button';
      btn.textContent = 'Copy';
      btn.addEventListener('click', function () {
        var code = pre.querySelector('code');
        var text = code ? code.textContent : pre.textContent;
        navigator.clipboard.writeText(text).then(function () {
          btn.textContent = 'Copied!';
          setTimeout(function () { btn.textContent = 'Copy'; }, 1500);
        }).catch(function () { btn.textContent = 'Failed'; });
      });
      pre.appendChild(btn);
    });
  }

  // ---------- Mermaid ----------
  function initMermaid() {
    if (!window.mermaid) return;
    var theme = root.getAttribute('data-theme') === 'dark' ? 'dark' : 'default';
    try {
      window.mermaid.initialize({
        startOnLoad: false,
        theme: theme,
        securityLevel: 'loose',
        flowchart: { useMaxWidth: true, htmlLabels: true, curve: 'basis' },
        er: { useMaxWidth: true },
        sequence: { useMaxWidth: true }
      });
      var blocks = document.querySelectorAll('.mermaid');
      blocks.forEach(function (b) { b.dataset.orig = b.textContent; });
      window.mermaid.run({ nodes: Array.from(blocks) }).catch(function () {});
    } catch (e) {}
  }

  // ---------- Search (client-side, cross-page) ----------
  var searchIndex = null;
  function initSearch() {
    var input = document.getElementById('searchInput');
    var results = document.getElementById('searchResults');
    if (!input || !results) return;

    var navLinks = Array.from(document.querySelectorAll('.nav-link'));
    var pages = navLinks.map(function (a) { return { href: a.getAttribute('href'), label: a.textContent.trim() }; });

    var slashListener = function (e) {
      if (e.key === '/' && document.activeElement !== input) { e.preventDefault(); input.focus(); }
      if (e.key === 'Escape' && document.activeElement === input) { input.blur(); results.hidden = true; }
    };
    document.addEventListener('keydown', slashListener);

    input.addEventListener('input', function () { doSearch(input.value.trim(), results, pages); });
    input.addEventListener('focus', function () { if (input.value.trim()) doSearch(input.value.trim(), results, pages); });
    document.addEventListener('click', function (e) {
      if (!e.target.closest('.search')) results.hidden = true;
    });
  }

  function doSearch(q, resultsEl, pages) {
    if (!q) { resultsEl.hidden = true; resultsEl.innerHTML = ''; return; }
    if (!searchIndex) { resultsEl.innerHTML = '<div class="search-empty">Loading index…</div>'; resultsEl.hidden = false; buildIndex(pages, function () { renderResults(q, resultsEl); }); return; }
    renderResults(q, resultsEl);
  }

  function buildIndex(pages, done) {
    searchIndex = [];
    var pending = pages.length;
    if (!pending) { done(); return; }
    pages.forEach(function (p) {
      fetch(p.href).then(function (r) { return r.text(); }).then(function (html) {
        var doc = new DOMParser().parseFromString(html, 'text/html');
        var article = doc.getElementById('content') || doc.body;
        var headings = article.querySelectorAll('h1, h2, h3');
        headings.forEach(function (h) {
          var text = h.textContent.trim();
          var ctx = collectContext(h);
          searchIndex.push({ page: p.href, label: p.label, title: text, context: ctx, level: h.tagName });
        });
        article.querySelectorAll('p, li').forEach(function (el) {
          var t = el.textContent.trim();
          if (t.length > 12) searchIndex.push({ page: p.href, label: p.label, title: findNearestHeading(el), context: t.slice(0, 140), level: 'P' });
        });
        pending--;
        if (pending === 0) done();
      }).catch(function () { pending--; if (pending === 0) done(); });
    });
  }

  function collectContext(h) {
    var sib = h.nextElementSibling;
    if (sib && (sib.tagName === 'P' || sib.tagName === 'UL')) return sib.textContent.trim().slice(0, 140);
    return '';
  }
  function findNearestHeading(el) {
    var node = el.previousElementSibling;
    while (node) { if (node.tagName === 'H2' || node.tagName === 'H3') return node.textContent.trim(); node = node.previousElementSibling; }
    return el.ownerDocument.querySelector('h1') ? el.ownerDocument.querySelector('h1').textContent.trim() : '';
  }

  function renderResults(q, resultsEl) {
    var ql = q.toLowerCase();
    var matches = searchIndex.filter(function (e) {
      return e.title.toLowerCase().indexOf(ql) !== -1 || e.context.toLowerCase().indexOf(ql) !== -1;
    }).slice(0, 12);

    if (!matches.length) { resultsEl.innerHTML = '<div class="search-empty">No matches for "' + escapeHtml(q) + '"</div>'; resultsEl.hidden = false; return; }

    resultsEl.innerHTML = matches.map(function (m, i) {
      var title = highlight(m.title, q);
      var ctx = m.context ? '<div class="sr-context">' + highlight(m.context, q) + '</div>' : '';
      return '<a class="search-result' + (i === 0 ? ' active' : '') + '" href="' + m.page + '"' +
        ' data-page="' + m.page + '"><div class="sr-title">' + title + ' <span class="nav-badge">' + escapeHtml(m.label) + '</span></div>' + ctx + '</a>';
    }).join('');
    resultsEl.hidden = false;

    resultsEl.querySelectorAll('.search-result').forEach(function (a) {
      a.addEventListener('mouseenter', function () {
        resultsEl.querySelectorAll('.search-result').forEach(function (x) { x.classList.remove('active'); });
        a.classList.add('active');
      });
    });
  }
  function escapeHtml(s) { return s.replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }
  function highlight(text, q) {
    var esc = escapeHtml(text);
    var re = new RegExp('(' + q.replace(/[.*+?^${}()|[\]\\]/g, '\\$&') + ')', 'gi');
    return esc.replace(re, '<mark>$1</mark>');
  }

  // ---------- Init ----------
  document.addEventListener('DOMContentLoaded', function () {
    initTheme();
    initSidebar();
    initProgress();
    initTOC();
    initCopy();
    initMermaid();
    initSearch();
    if (window.MG && window.MG.initAll) window.MG.initAll();
  });
})();
