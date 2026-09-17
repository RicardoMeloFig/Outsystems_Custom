/* DocumentGenerationHTML — interactive module graph.
   Renders a clickable, explorable module map from a JSON data block.
   Data is authored inside the page as:
     <div class="module-graph" data-module-graph></div>
     <script type="application/json" class="module-graph-data">{ ... }</script>

   Data shape:
   {
     "layers": [
       { "name": "UI", "modules": ["UI_Module"] },
       { "name": "Logic", "modules": ["Process_Module"] }
     ],
     "modules": {
       "UI_Module": {
         "label": "UI Module",
         "description": "Screens and web flows",
         "depends_on": ["Process_Module"],
         "dependents": [],
         "details": ["5 screens", "12 web blocks"],
         "exposes": ["EntryPoint1", "EntryPoint2"]
       }
     }
   }
   Vanilla JS, no deps. Exposes window.MG. */
(function () {
  'use strict';
  window.MG = window.MG || {};

  function build(container, data) {
    if (!data || !data.layers || !data.modules) return;

    var toolbar = el('div', 'mg-toolbar');
    toolbar.innerHTML = '<span class="mg-hint">Click a module to explore. Click a dependency chip to jump.</span>' +
      '<button type="button" class="mg-reset" style="font-size:12px;background:transparent;border:1px solid var(--border);color:var(--text-muted);border-radius:5px;padding:4px 10px;cursor:pointer;">Reset</button>';

    var canvas = el('div', 'mg-canvas');
    var layerEls = {};
    var moduleEls = {};

    data.layers.forEach(function (layer) {
      var lEl = el('div', 'mg-layer');
      lEl.appendChild(el('div', 'mg-layer-name', layer.name));
      var mods = el('div', 'mg-modules');
      (layer.modules || []).forEach(function (key) {
        var m = data.modules[key];
        if (!m) return;
        var card = el('div', 'mg-module');
        card.dataset.key = key;
        card.innerHTML = '<div class="mg-name">' + esc(m.label || key) + '</div>' +
          (m.description ? '<div class="mg-desc">' + esc(m.description) + '</div>' : '') +
          depsBlock(m, data);
        card.addEventListener('click', function (e) {
          if (e.target.classList.contains('mg-dep-chip')) {
            e.stopPropagation();
            selectModule(e.target.dataset.dep);
            return;
          }
          selectModule(key);
        });
        mods.appendChild(card);
        moduleEls[key] = card;
      });
      lEl.appendChild(mods);
      canvas.appendChild(lEl);
      layerEls[layer.name] = lEl;
    });

    var detail = el('div', 'mg-detail');

    container.innerHTML = '';
    container.appendChild(toolbar);
    container.appendChild(canvas);
    container.appendChild(detail);

    var resetBtn = toolbar.querySelector('.mg-reset');
    resetBtn.addEventListener('click', function () { selectModule(null); });

    function selectModule(key) {
      Object.keys(moduleEls).forEach(function (k) {
        moduleEls[k].classList.remove('selected', 'highlighted', 'dimmed');
      });
      if (!key) { detail.classList.remove('open'); return; }
      var m = data.modules[key];
      if (!m) return;
      var card = moduleEls[key];
      card.classList.add('selected');

      var related = collectRelated(key, data);
      Object.keys(moduleEls).forEach(function (k) {
        if (k === key) return;
        if (related.has(k)) moduleEls[k].classList.add('highlighted');
        else moduleEls[k].classList.add('dimmed');
      });

      renderDetail(detail, key, m, data);
      detail.classList.add('open');
      card.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
  }

  function collectRelated(key, data) {
    var set = new Set();
    var m = data.modules[key];
    if (m && m.depends_on) m.depends_on.forEach(function (d) { set.add(d); });
    if (m && m.dependents) m.dependents.forEach(function (d) { set.add(d); });
    return set;
  }

  function depsBlock(m, data) {
    if (!m.depends_on || !m.depends_on.length) return '';
    var chips = m.depends_on.map(function (d) {
      var dep = data.modules[d];
      var label = dep ? (dep.label || d) : d;
      return '<span class="mg-dep-chip" data-dep="' + esc(d) + '">' + esc(label) + '</span>';
    }).join('');
    return '<div class="mg-deps">→ ' + chips + '</div>';
  }

  function renderDetail(detail, key, m, data) {
    var html = '<h4>' + esc(m.label || key) + '</h4>';
    if (m.description) html += '<p class="mg-detail-desc">' + esc(m.description) + '</p>';

    if (m.exposes && m.exposes.length) {
      html += section('Exposes', '<ul>' + m.exposes.map(function (x) { return '<li>' + esc(x) + '</li>'; }).join('') + '</ul>');
    }
    if (m.details && m.details.length) {
      html += section('Highlights', '<ul>' + m.details.map(function (x) { return '<li>' + esc(x) + '</li>'; }).join('') + '</ul>');
    }
    if (m.depends_on && m.depends_on.length) {
      html += section('Depends on', chips(m.depends_on, data));
    }
    if (m.dependents && m.dependents.length) {
      html += section('Dependents (inbound)', chips(m.dependents, data));
    }
    detail.innerHTML = html;
    detail.querySelectorAll('.mg-dep-chip').forEach(function (chip) {
      chip.addEventListener('click', function () {
        var k = chip.dataset.dep;
        detail.parentElement.querySelector('.mg-canvas').dispatchEvent(new CustomEvent('mgselect', { detail: k }));
      });
    });
  }

  function chips(keys, data) {
    return '<div class="mg-related">' + keys.map(function (d) {
      var dep = data.modules[d];
      var label = dep ? (dep.label || d) : d;
      return '<span class="mg-dep-chip" data-dep="' + esc(d) + '">' + esc(label) + '</span>';
    }).join('') + '</div>';
  }
  function section(title, inner) {
    return '<div class="mg-detail-section"><h5>' + esc(title) + '</h5>' + inner + '</div>';
  }
  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }
  function esc(s) {
    return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; });
  }

  function initAll() {
    document.querySelectorAll('[data-module-graph]').forEach(function (container) {
      var src = container.nextElementSibling;
      if (!src || !src.classList.contains('module-graph-data')) {
        src = container.parentElement.querySelector('.module-graph-data');
      }
      if (!src) return;
      try {
        var data = JSON.parse(src.textContent);
        build(container, data);
      } catch (e) {
        container.innerHTML = '<div class="callout danger"><div class="callout-icon">⚠</div><div class="callout-body"><div class="callout-title">Module graph data error</div><div>' + esc(e.message) + '</div></div></div>';
      }
    });
  }

  window.MG.initAll = initAll;
  window.MG.build = build;
})();
