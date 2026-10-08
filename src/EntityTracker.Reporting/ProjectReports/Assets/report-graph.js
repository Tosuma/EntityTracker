/*
 * The report's dependency-tree highlighting. No DOM, so the report's tests run it as it is and
 * compare it with the app's.
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

  return { highlight: highlight };
})();
