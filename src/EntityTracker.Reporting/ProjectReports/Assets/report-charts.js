/*
 * Pure helpers for the report's charts and filters: no DOM, so the report's tests run them as they are.
 */
var EntityTrackerCharts = (function () {
  "use strict";

  /** The index of the data point nearest to x, for points spread evenly over [left, left + width]. */
  function nearestIndex(count, left, width, x) {
    if (count <= 1) return 0;
    var index = Math.round((x - left) / width * (count - 1));
    return Math.max(0, Math.min(count - 1, index));
  }

  /** The index of the bar under x, for count bars filling [left, left + width] in equal slots. */
  function barIndex(count, left, width, x) {
    if (count <= 1) return 0;
    var index = Math.floor((x - left) / width * count);
    return Math.max(0, Math.min(count - 1, index));
  }

  /** The next index when stepping by delta with the arrow keys; stays within [0, count - 1]. */
  function stepIndex(current, delta, count) {
    if (count <= 0) return -1;
    if (current < 0) return delta < 0 ? count - 1 : 0;
    return Math.max(0, Math.min(count - 1, current + delta));
  }

  /** Each value's share of the total as a percentage, or 0 when there is nothing. */
  function shares(values) {
    var total = values.reduce(function (sum, value) { return sum + Math.max(value, 0); }, 0);
    return values.map(function (value) { return total > 0 ? Math.max(value, 0) / total * 100 : 0; });
  }

  /** A percentage with one decimal, without a trailing ".0": "5.6%", "50%". */
  function formatShare(percent) {
    var rounded = Math.round(percent * 10) / 10;
    return (rounded % 1 === 0 ? rounded.toFixed(0) : rounded.toFixed(1)) + "%";
  }

  /** A donut slice's card: "Rework needed · 14 · 5.6%". */
  function sliceText(label, value, total) {
    return label + " · " + value + " · " + formatShare(total > 0 ? value / total * 100 : 0);
  }

  /** A data point's card lines: the heading, then one "Name value" line per series. */
  function pointLines(heading, series, index) {
    return [heading].concat(series.map(function (s) { return s.name + " " + formatNumber(s.values[index]); }));
  }

  function formatNumber(value) {
    if (value === undefined || value === null || isNaN(value)) return "–";
    return String(Math.round(value * 100) / 100);
  }

  /**
   * The choices for a filter: the column's fixed options in their own order when it has them
   * (kept even when no row has the value), otherwise the values that occur, sorted. Each has
   * the number of rows that hold it; a cell may hold several values separated by ", ".
   */
  function filterOptions(options, rows, key) {
    var counts = {};
    var occurring = [];
    rows.forEach(function (row) {
      String(row[key] || "").split(", ").forEach(function (value) {
        if (!value) return;
        if (!Object.prototype.hasOwnProperty.call(counts, value)) { counts[value] = 0; occurring.push(value); }
        counts[value]++;
      });
    });
    var values;
    if (options && options.length) {
      values = options.slice();
      // A value the fixed list doesn't know still gets a choice, after the known ones.
      occurring.forEach(function (value) { if (values.indexOf(value) < 0) values.push(value); });
    } else {
      values = occurring.slice().sort(function (a, b) { return a.localeCompare(b, undefined, { sensitivity: "base" }); });
    }
    return values.map(function (value) { return { value: value, count: counts[value] || 0 }; });
  }

  return {
    nearestIndex: nearestIndex,
    barIndex: barIndex,
    stepIndex: stepIndex,
    shares: shares,
    formatShare: formatShare,
    sliceText: sliceText,
    pointLines: pointLines,
    formatNumber: formatNumber,
    filterOptions: filterOptions
  };
})();
