/*
 * Pure helpers for the report's dependency graph: highlighting and the camera. No DOM, so the
 * report's tests run them as they are and compare the highlighting with the app's.
 */
var EntityTrackerGraph = (function () {
  "use strict";

  /**
   * What a selection highlights, as the app does, over the union of the selected entities:
   * "dependencies" follows links upward to everything they depend on, "dependents" follows them
   * downward, and "direct" takes their own drawn links and the entities at the other ends.
   * Implied links still lead to entities but are never highlighted, since they are not drawn.
   * links are { from, to, essential } with from the dependency and to the entity using it.
   * Returns the highlighted node and link indexes, each sorted.
   */
  function highlight(links, selected, mode) {
    var nodes = {};
    var edges = {};
    var chosen = {};
    selected.forEach(function (node) { nodes[node] = true; chosen[node] = true; });
    if (mode === "direct") {
      links.forEach(function (link, index) {
        if (!link.essential || !(chosen[link.from] || chosen[link.to])) return;
        edges[index] = true;
        nodes[link.from] = true;
        nodes[link.to] = true;
      });
    } else if (selected.length > 0) {
      var upward = mode === "dependencies";
      var byNode = {};
      links.forEach(function (link, index) {
        var key = upward ? link.to : link.from;
        (byNode[key] = byNode[key] || []).push(index);
      });
      var pending = selected.slice();
      while (pending.length) {
        var node = pending.shift();
        (byNode[node] || []).forEach(function (index) {
          var link = links[index];
          if (link.essential) edges[index] = true;
          var next = upward ? link.from : link.to;
          if (!nodes[next]) { nodes[next] = true; pending.push(next); }
        });
      }
    }
    return { nodes: sortedKeys(nodes), links: sortedKeys(edges) };
  }

  function sortedKeys(set) {
    return Object.keys(set).map(Number).sort(function (a, b) { return a - b; });
  }

  /** The scale and offset that fit bounds { x, y, width, height } into a width × height view. */
  function fit(bounds, width, height, padding, maxScale) {
    var usableWidth = Math.max(width - padding * 2, 1), usableHeight = Math.max(height - padding * 2, 1);
    var scale = Math.min(usableWidth / Math.max(bounds.width, 1), usableHeight / Math.max(bounds.height, 1), maxScale);
    return {
      scale: scale,
      x: width / 2 - (bounds.x + bounds.width / 2) * scale,
      y: height / 2 - (bounds.y + bounds.height / 2) * scale
    };
  }

  /** Zooms by factor around the view point (px, py), keeping that point still; the scale stays in [min, max]. */
  function zoomAt(view, factor, px, py, min, max) {
    var scale = Math.max(min, Math.min(max, view.scale * factor));
    var applied = scale / view.scale;
    return { scale: scale, x: px - (px - view.x) * applied, y: py - (py - view.y) * applied };
  }

  /** The view that centres the graph point (gx, gy) in a width × height view at the given scale. */
  function centreOn(gx, gy, width, height, scale) {
    return { scale: scale, x: width / 2 - gx * scale, y: height / 2 - gy * scale };
  }

  return { highlight: highlight, fit: fit, zoomAt: zoomAt, centreOn: centreOn };
})();
