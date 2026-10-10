/* EntityTracker Project report. Draws everything from the embedded data; no network access. */
(function () {
  "use strict";

  var data = JSON.parse(document.getElementById("report-data").textContent);
  var root = document.getElementById("report");
  var SVG = "http://www.w3.org/2000/svg";
  var NO_MATCH = EntityTrackerSearch.NO_MATCH;

  var STATUS_COLORS = {
    "Not started": ["#A0AFAF", "#141E1E"],
    "In progress": ["#3D6A8A", "#FFFFFF"],
    "Reworking": ["#B58BD0", "#141E1E"],
    "Rework needed": ["#FF6359", "#141E1E"],
    "Blocked": ["#9E2B25", "#FFFFFF"],
    "Dev. completed": ["#A8D5A2", "#141E1E"],
    "Completed": ["#A8D5A2", "#141E1E"],
    "Reconciled": ["#123836", "#FFFFFF"],
    "Ready": ["#41605E", "#FFFFFF"],
    "Waiting on dependencies": ["#D9922E", "#141E1E"]
  };

  // Name matching lives in report-search.js, shared with the report's own tests.
  var matchPriority = EntityTrackerSearch.matchPriority;
  // Chart and filter arithmetic lives in report-charts.js, also tested on its own.
  var Charts = EntityTrackerCharts;
  // Graph highlighting and camera arithmetic lives in report-graph.js, tested against the app.
  var Graph = EntityTrackerGraph;

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
    var print = el("button", { type: "button", className: "print-button no-print", text: "Print / Save as PDF" });
    print.addEventListener("click", function () { window.print(); });
    return el("header", { className: "report-header" }, [
      print,
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
      var key = legend(chart, section.chartType);
      var drawn = section.chartType === "donut" ? donut(chart, key) : section.chartType === "bars" ? bars(chart, key) : lines(chart, key);
      drawn.setAttribute("aria-label", section.title + ". Use the left and right arrow keys to read the values.");
      body.appendChild(drawn);
      body.appendChild(key.node);
    }
    render(currentScope);
    scopeListeners.push(render);
    return el("section", { className: "report-section", id: section.key }, [el("h2", { text: section.title }), body]);
  }

  /** The legend; its entries and the chart's parts highlight each other. */
  function legend(chart, type) {
    var items = type === "donut"
      ? chart.labels.map(function (label, i) { return { name: label + " (" + chart.series[0].values[i] + ")", color: chart.series[0].pointColors[i] }; })
      : chart.series.map(function (s) { return { name: s.name, color: s.color }; });
    var spans = items.map(function (item) {
      var span = el("span", { text: item.name });
      span.style.setProperty("--swatch", item.color);
      return span;
    });
    return {
      node: el("div", { className: "legend" }, spans),
      /** Emphasises one entry, or none for -1. */
      emphasise: function (index) {
        spans.forEach(function (span, i) { span.classList.toggle("active", i === index); });
      },
      /** Calls enter(index) and leave() as the pointer moves over the entries. */
      onHover: function (enter, leave) {
        spans.forEach(function (span, i) {
          span.addEventListener("mouseenter", function () { enter(i); });
          span.addEventListener("mouseleave", leave);
        });
      }
    };
  }

  // ---- The hover card, shared by every chart ----

  var tip = el("div", { className: "chart-tip", role: "status", "aria-live": "polite" });
  document.body.appendChild(tip);

  /** Shows the card, its first line as a heading, next to the window point (x, y). */
  function showTip(lines, x, y) {
    tip.replaceChildren();
    lines.forEach(function (line, i) { tip.appendChild(el("div", { className: i === 0 ? "heading" : "", text: line })); });
    tip.classList.add("visible");
    var width = tip.offsetWidth, height = tip.offsetHeight;
    var left = x + 14, top = y - height - 12;
    if (left + width > document.documentElement.clientWidth - 8) left = x - width - 14;
    if (top < 8) top = y + 16;
    tip.style.left = Math.max(8, left) + window.scrollX + "px";
    tip.style.top = top + window.scrollY + "px";
  }

  function hideTip() { tip.classList.remove("visible"); }

  /**
   * Makes a chart answer the pointer and the keyboard. pick(x, y) gives the index under a point
   * in the chart's own coordinates, or -1; show(index) highlights it and returns the card's lines
   * and where to put the card; clear() removes the highlight.
   */
  function interactive(node, width, height, count, pick, show, clear) {
    var current = -1;
    node.setAttribute("tabindex", "0");

    function local(event) {
      var rect = node.getBoundingClientRect();
      return [(event.clientX - rect.left) / rect.width * width, (event.clientY - rect.top) / rect.height * height];
    }

    function client(point) {
      var rect = node.getBoundingClientRect();
      return [rect.left + point[0] / width * rect.width, rect.top + point[1] / height * rect.height];
    }

    function select(index, at) {
      if (index !== current) { clear(); current = index; }
      if (index < 0) { hideTip(); return; }
      var shown = show(index);
      var place = at || client(shown.anchor);
      showTip(shown.lines, place[0], place[1]);
    }

    node.addEventListener("mousemove", function (event) {
      var point = local(event);
      select(pick(point[0], point[1]), [event.clientX, event.clientY]);
    });
    node.addEventListener("mouseleave", function () { select(-1); });
    node.addEventListener("blur", function () { select(-1); });
    node.addEventListener("keydown", function (event) {
      var next = event.key === "ArrowRight" || event.key === "ArrowDown" ? Charts.stepIndex(current, 1, count)
        : event.key === "ArrowLeft" || event.key === "ArrowUp" ? Charts.stepIndex(current, -1, count)
        : event.key === "Home" ? 0
        : event.key === "End" ? count - 1
        : event.key === "Escape" ? -1
        : null;
      if (next === null) return;
      event.preventDefault();
      select(next);
    });
    return { select: select };
  }

  // ---- Entry motion: brief, and never when the reader asks for less motion ----

  var calm = !window.matchMedia || window.matchMedia("(prefers-reduced-motion: reduce)").matches || window.matchMedia("print").matches;

  function animate(node, frames, delay) {
    if (calm || !node.animate) return null;
    return node.animate(frames, { duration: 600, delay: delay || 0, easing: "cubic-bezier(0.2, 0.7, 0.3, 1)", fill: "backwards" });
  }

  // ---- Charts ----

  var uniqueId = 0;

  function donut(chart, key) {
    var series = chart.series[0];
    var total = series.values.reduce(function (a, b) { return a + b; }, 0);
    var root = svg("svg", { viewBox: "0 0 320 220", role: "img", "class": "donut" });
    var cx = 160, cy = 110, r = 90, inner = 55, angle = -Math.PI / 2;
    var slices = [];
    var group = svg("g");
    root.appendChild(group);
    series.values.forEach(function (value, i) {
      if (value <= 0) return;
      var sweep = value / total * Math.PI * 2;
      var end = angle + Math.min(sweep, Math.PI * 2 - 0.0001);
      var large = sweep > Math.PI ? 1 : 0;
      var d = ["M", cx + r * Math.cos(angle), cy + r * Math.sin(angle),
               "A", r, r, 0, large, 1, cx + r * Math.cos(end), cy + r * Math.sin(end),
               "L", cx + inner * Math.cos(end), cy + inner * Math.sin(end),
               "A", inner, inner, 0, large, 0, cx + inner * Math.cos(angle), cy + inner * Math.sin(angle), "Z"].join(" ");
      var path = svg("path", { d: d, fill: series.pointColors[i], stroke: "#FFFFFF", "stroke-width": 1, "class": "slice" });
      slices.push({ index: i, path: path, middle: (angle + end) / 2, start: angle, end: end });
      group.appendChild(path);
      angle = end;
    });
    var label = svg("text", { x: cx, y: cy + 6, "text-anchor": "middle", "font-size": 22, "font-weight": 600 });
    label.textContent = String(total);
    root.appendChild(label);

    // The slices sweep in clockwise behind a growing mask, which goes once it has finished.
    if (!calm && group.animate) {
      var id = "sweep-" + (++uniqueId);
      var reach = r + 12, maskRadius = reach / 2, circumference = 2 * Math.PI * maskRadius;
      var circle = svg("circle", { cx: cx, cy: cy, r: maskRadius, fill: "none", stroke: "#FFFFFF", "stroke-width": reach,
        "stroke-dasharray": circumference, transform: "rotate(-90 " + cx + " " + cy + ")" });
      var mask = svg("mask", { id: id, maskUnits: "userSpaceOnUse", x: 0, y: 0, width: 320, height: 220 });
      mask.appendChild(circle);
      var defs = svg("defs");
      defs.appendChild(mask);
      root.insertBefore(defs, group);
      group.setAttribute("mask", "url(#" + id + ")");
      circle.animate([{ strokeDashoffset: circumference }, { strokeDashoffset: 0 }],
        { duration: 600, easing: "cubic-bezier(0.2, 0.7, 0.3, 1)", fill: "forwards" })
        .onfinish = function () { group.removeAttribute("mask"); };
    }

    function sliceAt(x, y) {
      var dx = x - cx, dy = y - cy, distance = Math.sqrt(dx * dx + dy * dy);
      if (distance < inner - 4 || distance > r + 10) return -1;
      var a = Math.atan2(dy, dx);
      if (a < -Math.PI / 2) a += Math.PI * 2;
      for (var i = 0; i < slices.length; i++) if (a >= slices[i].start && a < slices[i].end) return i;
      return -1;
    }

    function show(position) {
      var slice = slices[position];
      slice.path.setAttribute("transform", "translate(" + 6 * Math.cos(slice.middle) + " " + 6 * Math.sin(slice.middle) + ")");
      slice.path.classList.add("active");
      key.emphasise(slice.index);
      return {
        lines: [Charts.sliceText(chart.labels[slice.index], series.values[slice.index], total)],
        anchor: [cx + (r + 8) * Math.cos(slice.middle), cy + (r + 8) * Math.sin(slice.middle)]
      };
    }

    function clear() {
      slices.forEach(function (slice) { slice.path.removeAttribute("transform"); slice.path.classList.remove("active"); });
      key.emphasise(-1);
    }

    var hover = interactive(root, 320, 220, slices.length, sliceAt, show, clear);
    key.onHover(function (index) {
      var position = slices.map(function (s) { return s.index; }).indexOf(index);
      if (position >= 0) hover.select(position);
    }, function () { hover.select(-1); });
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

  function lines(chart, key) {
    var e = extent(chart, true);
    var box = axes(chart, e[0], e[1]);
    var count = chart.labels.length;
    var x = function (i) { return box.left + (count === 1 ? box.plotWidth / 2 : i / (count - 1) * box.plotWidth); };
    var guide = svg("line", { y1: box.top, y2: box.top + box.plotHeight, "class": "guide" });
    box.node.appendChild(guide);
    var radius = count > 60 ? 1.5 : 3;
    var drawn = chart.series.map(function (series) {
      var points = series.values.map(function (v, i) { return [x(i), box.y(v)]; });
      var line = svg("polyline", {
        points: points.map(function (p) { return p.join(","); }).join(" "),
        fill: "none", stroke: series.color, "stroke-width": 2.5, "stroke-linejoin": "round", "class": "series"
      });
      box.node.appendChild(line);
      var dots = svg("g", { "class": "series" });
      var circles = points.map(function (p) {
        var circle = svg("circle", { cx: p[0], cy: p[1], r: radius, fill: series.color });
        dots.appendChild(circle);
        return circle;
      });
      box.node.appendChild(dots);
      // The line draws itself from left to right; its points fade in behind it.
      var length = points.reduce(function (sum, p, i) {
        return i === 0 ? 0 : sum + Math.sqrt(Math.pow(p[0] - points[i - 1][0], 2) + Math.pow(p[1] - points[i - 1][1], 2));
      }, 0);
      line.setAttribute("stroke-dasharray", length + " " + length);
      var drawing = animate(line, [{ strokeDashoffset: length }, { strokeDashoffset: 0 }]);
      if (drawing) drawing.onfinish = function () { line.removeAttribute("stroke-dasharray"); };
      else line.removeAttribute("stroke-dasharray");
      animate(dots, [{ opacity: 0 }, { opacity: 1 }], 300);
      return { line: line, dots: dots, circles: circles };
    });

    function show(index) {
      guide.setAttribute("x1", x(index));
      guide.setAttribute("x2", x(index));
      guide.classList.add("visible");
      var top = box.top + box.plotHeight;
      drawn.forEach(function (d, s) {
        d.circles[index].setAttribute("r", 5);
        d.circles[index].classList.add("active");
        top = Math.min(top, box.y(chart.series[s].values[index]));
      });
      return { lines: Charts.pointLines(formatDate(chart.labels[index]), chart.series, index), anchor: [x(index), top] };
    }

    function clear() {
      guide.classList.remove("visible");
      drawn.forEach(function (d) { d.circles.forEach(function (c) { c.setAttribute("r", radius); c.classList.remove("active"); }); });
    }

    interactive(box.node, box.width, box.height, count, function (px) {
      return px < box.left - 8 || px > box.width - box.right + 8 ? -1 : Charts.nearestIndex(count, box.left, box.plotWidth, px);
    }, show, clear);
    // Pointing at a legend entry brings its series forward and fades the others.
    key.onHover(function (index) {
      drawn.forEach(function (d, s) { d.line.classList.toggle("faded", s !== index); d.dots.classList.toggle("faded", s !== index); });
      key.emphasise(index);
    }, function () {
      drawn.forEach(function (d) { d.line.classList.remove("faded"); d.dots.classList.remove("faded"); });
      key.emphasise(-1);
    });
    return box.node;
  }

  function bars(chart, key) {
    var e = extent(chart, true);
    var box = axes(chart, e[0], e[1]);
    var series = chart.series[0];
    var count = chart.labels.length;
    var slot = box.plotWidth / Math.max(count, 1);
    var zero = box.y(0);
    var rects = series.values.map(function (v, i) {
      var rect = svg("rect", {
        x: box.left + i * slot + slot * 0.15, y: Math.min(zero, box.y(v)),
        width: Math.max(slot * 0.7, 1), height: Math.max(Math.abs(box.y(v) - zero), 1),
        fill: v < 0 ? "#FF6359" : series.color, rx: 2, "class": "bar"
      });
      box.node.appendChild(rect);
      // Each bar grows out of the zero line, a moment after the one before it.
      rect.style.transformBox = "fill-box";
      rect.style.transformOrigin = v < 0 ? "top" : "bottom";
      animate(rect, [{ transform: "scaleY(0)" }, { transform: "scaleY(1)" }], Math.min(i * 12, 300));
      return rect;
    });

    function show(index) {
      rects[index].classList.add("active");
      key.emphasise(0);
      var v = series.values[index];
      return {
        lines: ["Week of " + formatDate(chart.labels[index]), series.name + " " + Charts.formatNumber(v)],
        anchor: [box.left + (index + 0.5) * slot, Math.min(zero, box.y(v))]
      };
    }

    function clear() {
      rects.forEach(function (rect) { rect.classList.remove("active"); });
      key.emphasise(-1);
    }

    interactive(box.node, box.width, box.height, count, function (px) {
      return px < box.left || px > box.left + box.plotWidth ? -1 : Charts.barIndex(count, box.left, box.plotWidth, px);
    }, show, clear);
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
    // On paper the rows are the ones on screen; this line says which search and filters chose them.
    var printNote = el("p", { className: "print-only print-note" });
    columns.filter(function (c) { return c.filter; }).forEach(function (column) {
      var choices = Charts.filterOptions(column.options, section.rows, column.key);
      var select = el("select", { "aria-label": "Filter by " + column.header, "data-column": column.key },
        [el("option", { value: "", text: column.header + ": all" })].concat(choices.map(function (choice) {
          return el("option", { value: choice.value, text: choice.value + " (" + choice.count + ")" });
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
          if (column.key === "entity" && hasGraph && value) {
            var link = el("button", { type: "button", className: "entity-link", text: value, title: "Show in the dependency graph" });
            link.addEventListener("click", function () {
              var tracker = item.row.tracker || (scopes.length === 1 ? scopes[0].name : "");
              graphListeners.show.forEach(function (listener) { listener(tracker, value); });
            });
            cell.appendChild(link);
          } else if ((column.key === "status" || column.key === "work") && value) {
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
      var applied = [];
      if (query) applied.push("search “" + query + "”");
      columns.forEach(function (column) { if (filters[column.key]) applied.push(column.header + ": " + filters[column.key]); });
      printNote.textContent = applied.length
        ? "Showing " + shown.length + " of " + rows.length + " entities, limited by " + applied.join(", ") + "."
        : "All " + rows.length + " entities.";
    }

    search.addEventListener("input", function () {
      render();
      var query = search.value.trim();
      graphListeners.search.forEach(function (listener) { listener(query); });
    });
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
      el("h2", { text: section.title }), tools, printNote, el("div", { className: "table-wrap" }, [table])
    ]);
  }

  // ---- Dependency graph ----

  var graphListeners = { search: [], show: [] };

  /**
   * The dependency tree, one Tracker at a time, fitted to the width of the page. It does not move or
   * zoom: point at an entity for its card, click it to highlight what it leads to, Ctrl+click to add
   * more. The boxes carry no names; the card names the entity.
   */
  function graphSection(section) {
    var available = scopes.filter(function (scope) { return section.byScope[scope.key]; });
    if (!available.length) return null;
    var state = { key: available[0].key, mode: "direct", selected: [], hover: -1, query: "" };

    var trackerSelect = el("select", { "aria-label": "Tracker shown in the graph" }, available.map(function (scope) {
      return el("option", { value: scope.key, text: scope.name });
    }));
    var modeSelect = el("select", { "aria-label": "Highlight" }, [
      el("option", { value: "dependencies", text: "Highlight: Dependencies" }),
      el("option", { value: "dependents", text: "Highlight: Dependents" }),
      el("option", { value: "direct", text: "Highlight: Direct links" })
    ]);
    modeSelect.value = state.mode;
    var clearButton = el("button", { type: "button", text: "Clear selection" });
    var note = el("span", { className: "graph-note", role: "status" });
    var tools = el("div", { className: "graph-tools no-print" }, [
      available.length > 1 ? el("label", { className: "inline" }, [document.createTextNode("Tracker "), trackerSelect]) : null,
      modeSelect, clearButton, note
    ]);

    var host = el("div", { className: "graph-host" });
    var canvas = svg("svg", { "class": "graph", role: "img", tabindex: "0", preserveAspectRatio: "xMidYMin meet",
      "aria-label": "Dependency tree. Point at an entity for its details; click it to highlight its links." });
    host.appendChild(canvas);
    var legendNode = el("div", { className: "legend graph-legend" }, [
      "Not started", "Blocked", "In progress", "Rework needed", "Reworking", "Dev. completed", "Reconciled", "Missing"
    ].map(function (label) {
      var span = el("span", { text: label });
      span.style.setProperty("--swatch", label === "Missing" ? "#FFFFFF" : STATUS_COLORS[label][0]);
      if (label === "Missing") span.className = "missing";
      return span;
    }));

    var graph, nodeShapes = [], highlightPath, hoverPath, linkPath, matches = {};

    function linkD(link) {
      var route = link.route || [];
      var d = "M" + route[0] + " " + route[1];
      for (var i = 2; i < route.length; i += 2) {
        var middle = (route[i - 1] + route[i + 1]) / 2;
        d += "C" + route[i - 2] + " " + middle + " " + route[i] + " " + middle + " " + route[i] + " " + route[i + 1];
      }
      return d;
    }

    function draw() {
      graph = section.byScope[state.key];
      canvas.replaceChildren();
      nodeShapes = [];
      linkPath = svg("path", { "class": "links" });
      linkPath.setAttribute("d", graph.links.filter(function (link) { return link.essential; }).map(linkD).join(""));
      canvas.appendChild(linkPath);
      hoverPath = svg("path", { "class": "links hover" });
      highlightPath = svg("path", { "class": "links strong" });
      canvas.appendChild(hoverPath);
      canvas.appendChild(highlightPath);

      var minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
      graph.nodes.forEach(function (node, index) {
        var group = svg("g", { "class": "node" + (node.missing ? " missing" : ""), "data-index": index,
          transform: "translate(" + node.treeX + " " + node.treeY + ")" });
        // As in the app: an empty name area above a band in the status colour, 2/3 and 1/3 of the box.
        var color = node.missing ? null : (STATUS_COLORS[node.status] || ["#A0AFAF"])[0];
        group.appendChild(svg("rect", { "class": "halo", x: -6, y: -6, width: TREE_WIDTH + 12, height: TREE_HEIGHT + 12, rx: 14 }));
        group.appendChild(svg("rect", { "class": "card", width: TREE_WIDTH, height: TREE_HEIGHT, rx: TREE_RADIUS }));
        var band = svg("path", { "class": "band", d:
          "M0 " + TREE_NAME + "H" + TREE_WIDTH + "V" + (TREE_HEIGHT - TREE_RADIUS) +
          "Q" + TREE_WIDTH + " " + TREE_HEIGHT + " " + (TREE_WIDTH - TREE_RADIUS) + " " + TREE_HEIGHT +
          "H" + TREE_RADIUS + "Q0 " + TREE_HEIGHT + " 0 " + (TREE_HEIGHT - TREE_RADIUS) + "Z" });
        if (color) band.setAttribute("fill", color);
        group.appendChild(band);
        group.appendChild(svg("rect", { "class": "outline", width: TREE_WIDTH, height: TREE_HEIGHT, rx: TREE_RADIUS }));
        canvas.appendChild(group);
        nodeShapes.push(group);
        minX = Math.min(minX, node.treeX); minY = Math.min(minY, node.treeY);
        maxX = Math.max(maxX, node.treeX + TREE_WIDTH); maxY = Math.max(maxY, node.treeY + TREE_HEIGHT);
      });
      // The whole tree, with a margin, scaled to the width of the page; its height follows.
      var margin = 20;
      canvas.setAttribute("viewBox", [minX - margin, minY - margin, maxX - minX + margin * 2, maxY - minY + margin * 2].join(" "));
      paint();
    }

    /** The drawn links that start or end at a node; links implied by a longer chain are not drawn. */
    function drawnLinks(index) {
      return graph.links.filter(function (link) { return link.essential && (link.from === index || link.to === index); });
    }

    function paint() {
      if (!graph) return;
      var lit = Graph.highlight(graph.links, state.selected, state.mode);
      var litNodes = {};
      lit.nodes.forEach(function (index) { litNodes[index] = true; });
      var hasSelection = state.selected.length > 0;
      highlightPath.setAttribute("d", lit.links.map(function (index) { return linkD(graph.links[index]); }).join(""));
      var hoverLinks = !hasSelection && state.hover >= 0 ? drawnLinks(state.hover) : [];
      hoverPath.setAttribute("d", hoverLinks.map(linkD).join(""));
      var near = {};
      hoverLinks.forEach(function (link) { near[link.from] = true; near[link.to] = true; });
      linkPath.classList.toggle("dim", hasSelection);
      nodeShapes.forEach(function (shape, index) {
        shape.classList.toggle("selected", state.selected.indexOf(index) >= 0);
        shape.classList.toggle("lit", hasSelection && !!litNodes[index] && state.selected.indexOf(index) < 0);
        shape.classList.toggle("dim", hasSelection && !litNodes[index]);
        shape.classList.toggle("near", !!near[index] || index === state.hover);
        shape.classList.toggle("match", !!matches[index]);
      });
      var count = Object.keys(matches).length;
      note.textContent = state.query
        ? (count === 1 ? "1 entity matches the search" : count + " entities match the search")
        : hasSelection ? state.selected.length + " selected · Esc clears" : "";
      clearButton.disabled = !hasSelection;
    }

    function card(index) {
      var node = graph.nodes[index];
      var links = drawnLinks(index);
      var dependsOn = links.filter(function (link) { return link.to === index; }).length;
      var usedBy = links.filter(function (link) { return link.from === index; }).length;
      if (node.missing) return [node.name, "Missing: no entity has this name", usedBy + (usedBy === 1 ? " entity depends" : " entities depend") + " on it"];
      var lines = [node.name, node.status + " · " + node.work,
        "Depends on " + dependsOn + " · " + usedBy + (usedBy === 1 ? " depends" : " depend") + " on it"];
      if (node.waitingOn) lines.push("Waiting on " + node.waitingOn);
      return lines;
    }

    function nodeAt(target) {
      var group = target.closest ? target.closest("g.node") : null;
      return group ? Number(group.getAttribute("data-index")) : -1;
    }

    function select(index, add) {
      if (index < 0) state.selected = [];
      else if (add) {
        var at = state.selected.indexOf(index);
        if (at >= 0) state.selected.splice(at, 1); else state.selected.push(index);
      } else state.selected = state.selected.length === 1 && state.selected[0] === index ? [] : [index];
      paint();
    }

    canvas.addEventListener("pointermove", function (event) {
      var index = nodeAt(event.target);
      if (index !== state.hover) { state.hover = index; paint(); }
      if (index >= 0) showTip(card(index), event.clientX, event.clientY); else hideTip();
    });
    canvas.addEventListener("pointerleave", function () {
      state.hover = -1;
      hideTip();
      paint();
    });
    canvas.addEventListener("click", function (event) { select(nodeAt(event.target), event.ctrlKey || event.metaKey); });
    canvas.addEventListener("keydown", function (event) {
      if (event.key !== "Escape") return;
      event.preventDefault();
      select(-1);
    });
    clearButton.addEventListener("click", function () { select(-1); });
    modeSelect.addEventListener("change", function () { state.mode = modeSelect.value; paint(); });

    function showTracker(key) {
      state.key = key;
      trackerSelect.value = key;
      state.selected = [];
      refreshMatches();
      draw();
    }
    trackerSelect.addEventListener("change", function () { showTracker(trackerSelect.value); });

    function refreshMatches() {
      matches = {};
      if (!state.query) return;
      section.byScope[state.key].nodes.forEach(function (node, index) {
        if (matchPriority(node.name, state.query) < NO_MATCH) matches[index] = true;
      });
    }

    // The report's search marks matching entities in the tree too.
    graphListeners.search.push(function (query) {
      state.query = query;
      refreshMatches();
      paint();
    });
    // "Show in graph" from the entity table: switch to its Tracker, select it and scroll to it.
    graphListeners.show.push(function (trackerName, entityName) {
      var scope = available.filter(function (s) { return s.name === trackerName; })[0] || available[0];
      if (scope.key !== state.key) showTracker(scope.key);
      var index = -1;
      graph.nodes.forEach(function (node, i) { if (!node.missing && node.name === entityName) index = i; });
      if (index < 0) return;
      state.selected = [index];
      paint();
      nodeShapes[index].scrollIntoView({ behavior: calm ? "auto" : "smooth", block: "center" });
      canvas.focus({ preventScroll: true });
    });
    // Follow "Show progress for" when it names a Tracker the graph has.
    scopeListeners.push(function (scope) {
      if (scope !== state.key && section.byScope[scope]) showTracker(scope);
    });

    draw();
    return el("section", { className: "report-section", id: section.key }, [
      el("h2", { text: section.title }),
      el("p", { className: "section-hint no-print", text: "Point at an entity to see its name and status. Click it to highlight its links; Ctrl+click adds more." }),
      tools, host, legendNode
    ]);
  }

  // The app's tree box: 150 × 81 with the name area taking the top 54 (2/3).
  var TREE_WIDTH = 150, TREE_HEIGHT = 81, TREE_NAME = 54, TREE_RADIUS = 8;

  // ---- Page ----

  /** Links to every section; on paper it is the report's table of contents. */
  function contents() {
    var list = el("ol", {}, data.sections.map(function (section) {
      return el("li", {}, [el("a", { href: "#" + section.key, text: section.title })]);
    }));
    return el("nav", { className: "contents", "aria-label": "Contents" }, [el("h2", { text: "Contents" }), list]);
  }

  /** Puts the report's name and page numbers in the printed page margins. */
  function pageMargins() {
    var title = (data.projectName + " – " + (data.audience === "internal" ? "Internal report" : "Client report"))
      .replace(/\s+/g, " ");
    var style = document.createElement("style");
    // A JSON string is also a valid CSS string: quotes and backslashes arrive escaped.
    style.textContent = "@page { @top-left { content: " + JSON.stringify(title) + "; } }";
    document.head.appendChild(style);
  }

  var hasGraph = data.sections.some(function (section) { return section.kind === "graph"; });
  root.appendChild(header());
  root.appendChild(contents());
  var toolbar = scopeToolbar();
  if (toolbar) root.appendChild(toolbar);
  pageMargins();
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
    else if (section.kind === "graph") { var graphNode = graphSection(section); if (graphNode) root.appendChild(graphNode); }
  });
  var creator = root.getAttribute("data-creator");
  root.appendChild(el("footer", { text: "Generated by EntityTracker" + (creator ? ", created by " + creator : "") +
    ". This file works offline and can be printed to PDF." }));
})();
