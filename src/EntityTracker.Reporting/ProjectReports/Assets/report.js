/* EntityTracker Project report. Draws everything from the embedded data; no network access. */
(function () {
  "use strict";

  var data = JSON.parse(document.getElementById("report-data").textContent);
  var root = document.getElementById("report");
  var SVG = "http://www.w3.org/2000/svg";
  var NO_MATCH = EntityTrackerSearch.NO_MATCH;

  var STATUS_COLORS = {
    "Not started": ["#A0AFAF", "#141E1E"],
    "In progress": ["#41605E", "#FFFFFF"],
    "Reworking": ["#41605E", "#FFFFFF"],
    "Rework needed": ["#FF6359", "#141E1E"],
    "Blocked": ["#FF6359", "#141E1E"],
    "Dev. completed": ["#718886", "#FFFFFF"],
    "Completed": ["#718886", "#FFFFFF"],
    "Reconciled": ["#123836", "#FFFFFF"],
    "Ready": ["#D0D7D7", "#141E1E"],
    "Waiting on dependencies": ["#F6D9A8", "#141E1E"]
  };

  // Name matching lives in report-search.js, shared with the report's own tests.
  var matchPriority = EntityTrackerSearch.matchPriority;

  // ---- Small DOM helpers ----

  function el(tag, attributes, children) {
    var node = document.createElement(tag);
    for (var name in attributes || {}) {
      if (name === "text") node.textContent = attributes[name];
      else if (name === "className") node.className = attributes[name];
      else node.setAttribute(name, attributes[name]);
    }
    (children || []).forEach(function (child) { if (child) node.appendChild(child); });
    return node;
  }

  function svg(tag, attributes) {
    var node = document.createElementNS(SVG, tag);
    for (var name in attributes || {}) node.setAttribute(name, attributes[name]);
    return node;
  }

  function titled(node, text) {
    var title = svg("title");
    title.textContent = text;
    node.appendChild(title);
    return node;
  }

  function formatDate(iso) {
    if (!iso) return "";
    var date = new Date(iso);
    return isNaN(date) ? iso : date.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
  }

  /** A short axis date: "Jun 11", with the year only on the first and last label. */
  function axisDate(iso, withYear) {
    var date = new Date(iso);
    if (isNaN(date)) return iso;
    return date.toLocaleDateString(undefined, withYear
      ? { year: "numeric", month: "short", day: "numeric" }
      : { month: "short", day: "numeric" });
  }

  // ---- Header and scope ----

  var scopes = data.scopes;
  var currentScope = scopes[0].key;
  var scopeListeners = [];

  function header() {
    var internal = data.audience === "internal";
    var meta = [
      "Trackers: " + (scopes.length > 1 ? scopes.slice(1).map(function (s) { return s.name; }).join(", ") : scopes[0].name),
      "Generated " + formatDate(data.generatedAt)
    ];
    if (data.dataTo) meta.push("Progress data " + (data.dataFrom ? formatDate(data.dataFrom) + " – " : "to ") + formatDate(data.dataTo));
    return el("header", { className: "report-header" }, [
      el("h1", {}, [
        document.createTextNode(data.projectName),
        el("span", { className: "audience" + (internal ? " internal" : ""), text: internal ? "Internal report" : "Client report" })
      ]),
      el("div", { className: "header-meta" }, meta.map(function (text) { return el("span", { text: text }); }))
    ]);
  }

  function scopeToolbar() {
    if (scopes.length < 2) return null;
    var select = el("select", { id: "scope", "aria-label": "Show progress for" },
      scopes.map(function (scope) { return el("option", { value: scope.key, text: scope.name }); }));
    select.addEventListener("change", function () {
      currentScope = select.value;
      scopeListeners.forEach(function (listener) { listener(currentScope); });
    });
    return el("div", { className: "toolbar no-print" }, [el("label", { "for": "scope", text: "Show progress for" }), select]);
  }

  // ---- Sections ----

  function summarySection(section) {
    var cards = el("div", { className: "cards" });
    function render(scope) {
      cards.replaceChildren();
      (section.byScope[scope] || []).forEach(function (card) {
        cards.appendChild(el("div", { className: "card" + (card.attention ? " attention" : "") }, [
          el("div", { className: "label", text: card.label }),
          el("div", { className: "value", text: String(card.value) })
        ]));
      });
    }
    render(currentScope);
    scopeListeners.push(render);
    return el("section", { className: "report-section", id: section.key }, [el("h2", { text: section.title }), cards]);
  }

  function chartSection(section) {
    var body = el("div", { className: "chart" });
    function render(scope) {
      body.replaceChildren();
      var chart = section.byScope[scope];
      var hasData = chart && chart.series.some(function (s) { return s.values.some(function (v) { return v !== 0; }); });
      if (!hasData) { body.appendChild(el("div", { className: "empty", text: "No progress history for this selection yet." })); return; }
      body.appendChild(section.chartType === "donut" ? donut(chart) : section.chartType === "bars" ? bars(chart) : lines(chart));
      body.appendChild(legend(chart, section.chartType));
    }
    render(currentScope);
    scopeListeners.push(render);
    return el("section", { className: "report-section", id: section.key }, [el("h2", { text: section.title }), body]);
  }

  function legend(chart, type) {
    var items = type === "donut"
      ? chart.labels.map(function (label, i) { return { name: label + " (" + chart.series[0].values[i] + ")", color: chart.series[0].pointColors[i] }; })
      : chart.series.map(function (s) { return { name: s.name, color: s.color }; });
    return el("div", { className: "legend" }, items.map(function (item) {
      var span = el("span", { text: item.name });
      span.style.setProperty("--swatch", item.color);
      return span;
    }));
  }

  function donut(chart) {
    var series = chart.series[0];
    var total = series.values.reduce(function (a, b) { return a + b; }, 0);
    var root = svg("svg", { viewBox: "0 0 320 220", role: "img", "aria-label": "Entities by status" });
    var cx = 160, cy = 110, r = 90, inner = 55, angle = -Math.PI / 2;
    series.values.forEach(function (value, i) {
      if (value <= 0) return;
      var sweep = value / total * Math.PI * 2;
      var end = angle + Math.min(sweep, Math.PI * 2 - 0.0001);
      var large = sweep > Math.PI ? 1 : 0;
      var d = ["M", cx + r * Math.cos(angle), cy + r * Math.sin(angle),
               "A", r, r, 0, large, 1, cx + r * Math.cos(end), cy + r * Math.sin(end),
               "L", cx + inner * Math.cos(end), cy + inner * Math.sin(end),
               "A", inner, inner, 0, large, 0, cx + inner * Math.cos(angle), cy + inner * Math.sin(angle), "Z"].join(" ");
      root.appendChild(titled(svg("path", { d: d, fill: series.pointColors[i], stroke: "#FFFFFF", "stroke-width": 1 }),
        chart.labels[i] + ": " + value));
      angle = end;
    });
    var label = svg("text", { x: cx, y: cy + 6, "text-anchor": "middle", "font-size": 22, "font-weight": 600 });
    label.textContent = String(total);
    root.appendChild(label);
    return root;
  }

  function axes(chart, minValue, maxValue) {
    var box = { left: 44, right: 12, top: 12, bottom: 36, width: 640, height: 260 };
    box.plotWidth = box.width - box.left - box.right;
    box.plotHeight = box.height - box.top - box.bottom;
    var range = maxValue - minValue || 1;
    box.y = function (v) { return box.top + box.plotHeight - (v - minValue) / range * box.plotHeight; };
    var node = svg("svg", { viewBox: "0 0 " + box.width + " " + box.height, role: "img" });
    var ticks = 4;
    for (var t = 0; t <= ticks; t++) {
      var value = minValue + range * t / ticks;
      var y = box.y(value);
      node.appendChild(svg("line", { x1: box.left, x2: box.width - box.right, y1: y, y2: y, stroke: "#D0D7D7" }));
      var text = svg("text", { x: box.left - 6, y: y + 4, "text-anchor": "end" });
      text.textContent = String(Math.round(value));
      node.appendChild(text);
    }
    var last = chart.labels.length - 1;
    var step = Math.max(1, Math.ceil(chart.labels.length / 6));
    chart.labels.forEach(function (label, i) {
      // Every step-th date, plus the last one unless a regular label sits right before it.
      var regular = i % step === 0 && (i === last || last - i >= step * 0.75);
      if (!regular && i !== last) return;
      var x = box.left + (chart.labels.length === 1 ? box.plotWidth / 2 : i / Math.max(last, 1) * box.plotWidth);
      var anchor = chart.labels.length === 1 ? "middle" : i === 0 ? "start" : i === last ? "end" : "middle";
      var text = svg("text", { x: x, y: box.height - 12, "text-anchor": anchor });
      text.textContent = axisDate(label, i === 0 || i === last);
      node.appendChild(text);
    });
    box.node = node;
    return box;
  }

  function extent(chart, includeZero) {
    var values = [];
    chart.series.forEach(function (s) { values = values.concat(s.values); });
    var min = Math.min.apply(null, values.concat(includeZero ? [0] : []));
    var max = Math.max.apply(null, values.concat([includeZero ? 0 : min + 1]));
    return [Math.min(0, min), max === min ? max + 1 : max];
  }

  function lines(chart) {
    var e = extent(chart, true);
    var box = axes(chart, e[0], e[1]);
    var count = chart.labels.length;
    chart.series.forEach(function (series) {
      var points = series.values.map(function (v, i) {
        var x = box.left + (count === 1 ? box.plotWidth / 2 : i / (count - 1) * box.plotWidth);
        return [x, box.y(v)];
      });
      box.node.appendChild(svg("polyline", {
        points: points.map(function (p) { return p.join(","); }).join(" "),
        fill: "none", stroke: series.color, "stroke-width": 2.5, "stroke-linejoin": "round"
      }));
      points.forEach(function (p, i) {
        box.node.appendChild(titled(svg("circle", { cx: p[0], cy: p[1], r: count > 60 ? 1.5 : 3, fill: series.color }),
          series.name + " · " + formatDate(chart.labels[i]) + ": " + series.values[i]));
      });
    });
    return box.node;
  }

  function bars(chart) {
    var e = extent(chart, true);
    var box = axes(chart, e[0], e[1]);
    var series = chart.series[0];
    var count = chart.labels.length;
    var slot = box.plotWidth / Math.max(count, 1);
    var zero = box.y(0);
    series.values.forEach(function (v, i) {
      var x = box.left + i * slot + slot * 0.15;
      var y = Math.min(zero, box.y(v));
      box.node.appendChild(titled(svg("rect", {
        x: x, y: y, width: Math.max(slot * 0.7, 1), height: Math.max(Math.abs(box.y(v) - zero), 1),
        fill: v < 0 ? "#FF6359" : series.color, rx: 2
      }), "Week of " + formatDate(chart.labels[i]) + ": " + v));
    });
    return box.node;
  }

  function tableSection(section) {
    var columns = section.columns;
    var rows = section.rows.map(function (row, index) { return { row: row, index: index }; });
    var searchable = columns.filter(function (c) { return c.searchable; });
    var filters = {};
    var sort = { key: null, descending: false };

    var search = el("input", { type: "search", id: "search", placeholder: "Search entities, groups, dependencies…",
      "aria-label": "Search the report" });
    var count = el("span", { className: "result-count", role: "status" });
    var tools = el("div", { className: "table-tools no-print" }, [search]);
    columns.filter(function (c) { return c.filter; }).forEach(function (column) {
      var values = {};
      section.rows.forEach(function (row) {
        String(row[column.key] || "").split(", ").forEach(function (v) { if (v) values[v] = true; });
      });
      var select = el("select", { "aria-label": "Filter by " + column.header, "data-column": column.key },
        [el("option", { value: "", text: column.header + ": all" })].concat(Object.keys(values).sort().map(function (v) {
          return el("option", { value: v, text: v });
        })));
      select.addEventListener("change", function () { filters[column.key] = select.value; render(); });
      if (column.scope) select.id = "scope-filter";
      tools.appendChild(select);
    });
    tools.appendChild(count);

    var head = el("tr", {}, columns.map(function (column) {
      var th = el("th", { scope: "col", tabindex: "0", text: column.header, "data-column": column.key });
      function toggle() {
        sort = { key: column.key, descending: sort.key === column.key ? !sort.descending : false };
        render();
      }
      th.addEventListener("click", toggle);
      th.addEventListener("keydown", function (event) { if (event.key === "Enter" || event.key === " ") { event.preventDefault(); toggle(); } });
      return th;
    }));
    var body = el("tbody");
    var table = el("table", {}, [el("thead", {}, [head]), body]);

    function score(row, query) {
      var best = NO_MATCH;
      searchable.forEach(function (column) {
        String(row[column.key] || "").split(", ").forEach(function (value) {
          if (value) best = Math.min(best, matchPriority(value, query));
        });
      });
      return best;
    }

    function render() {
      var query = search.value.trim();
      var shown = rows.filter(function (item) {
        for (var key in filters) {
          if (filters[key] && String(item.row[key] || "").split(", ").indexOf(filters[key]) < 0) return false;
        }
        item.score = query ? score(item.row, query) : 0;
        return item.score < NO_MATCH;
      });
      shown.sort(function (a, b) {
        if (sort.key) {
          var left = String(a.row[sort.key] || ""), right = String(b.row[sort.key] || "");
          var numeric = left !== "" && right !== "" && !isNaN(left) && !isNaN(right);
          var order = numeric ? Number(left) - Number(right) : left.localeCompare(right, undefined, { sensitivity: "base" });
          if (order !== 0) return sort.descending ? -order : order;
        }
        return a.score - b.score || a.index - b.index;
      });
      Array.prototype.forEach.call(head.children, function (th) {
        if (th.getAttribute("data-column") === sort.key) th.setAttribute("aria-sort", sort.descending ? "descending" : "ascending");
        else th.removeAttribute("aria-sort");
      });
      body.replaceChildren();
      shown.forEach(function (item) {
        body.appendChild(el("tr", {}, columns.map(function (column) {
          var value = item.row[column.key] || "";
          var cell = el("td", { className: column.key === "entity" ? "entity" : column.scope ? "scope" : "" });
          if ((column.key === "status" || column.key === "work") && value) {
            var chip = el("span", { className: "status", text: value });
            var colors = STATUS_COLORS[value];
            if (colors) { chip.style.setProperty("--swatch", colors[0]); chip.style.setProperty("--swatch-ink", colors[1]); }
            cell.appendChild(chip);
          } else {
            cell.textContent = value;
          }
          return cell;
        })));
      });
      count.textContent = "Showing " + shown.length + " of " + rows.length;
    }

    search.addEventListener("input", render);
    document.addEventListener("keydown", function (event) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "f" && document.activeElement !== search) {
        event.preventDefault();
        search.focus();
        search.select();
      }
    });
    scopeListeners.push(function (scope) {
      var scopeFilter = tools.querySelector("#scope-filter");
      if (!scopeFilter) return;
      var match = scopes.filter(function (s) { return s.key === scope; })[0];
      scopeFilter.value = scope === "all" || !match ? "" : match.name;
      filters[scopeFilter.getAttribute("data-column")] = scopeFilter.value;
      render();
    });
    render();
    return el("section", { className: "report-section", id: section.key }, [
      el("h2", { text: section.title }), tools, el("div", { className: "table-wrap" }, [table])
    ]);
  }

  // ---- Page ----

  root.appendChild(header());
  var toolbar = scopeToolbar();
  if (toolbar) root.appendChild(toolbar);
  var chartGrid = null;
  data.sections.forEach(function (section) {
    if (section.kind === "chart") {
      if (!chartGrid) { chartGrid = el("div", { className: "charts" }); root.appendChild(chartGrid); }
      chartGrid.appendChild(chartSection(section));
      return;
    }
    chartGrid = null;
    if (section.kind === "summary") root.appendChild(summarySection(section));
    else if (section.kind === "table") root.appendChild(tableSection(section));
  });
  root.appendChild(el("footer", { text: "Generated by EntityTracker. This file works offline and can be printed to PDF." }));
})();
