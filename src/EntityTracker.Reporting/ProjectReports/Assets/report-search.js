/* EntityTracker report search: the same name matching as the app (EntityNameWords.MatchPriority).
   0 exact, 1 prefix, 2 word match from the first word, 3 from a later word (also written without
   spaces), 4 text anywhere, NO_MATCH otherwise. */
var EntityTrackerSearch = (function () {
  "use strict";

  var NO_MATCH = Number.MAX_SAFE_INTEGER;


  function isLetter(c) { return /\p{L}/u.test(c); }
  function isDigit(c) { return /\p{Nd}/u.test(c); }
  function isUpper(c) { return /\p{Lu}/u.test(c); }
  function isLower(c) { return /\p{Ll}/u.test(c); }
  function isLetterOrDigit(c) { return isLetter(c) || isDigit(c); }

  function startsWord(value, index) {
    var current = value[index];
    var previous = value[index - 1];
    return (isUpper(current) &&
            (isLower(previous) || isDigit(previous) ||
             (isUpper(previous) && index + 1 < value.length && isLower(value[index + 1])))) ||
           (isDigit(current) && isLetter(previous)) ||
           (isLetter(current) && isDigit(previous));
  }

  function words(value) {
    var result = [];
    var start = -1;
    for (var index = 0; index < value.length; index++) {
      var current = value[index];
      if (!isLetterOrDigit(current)) {
        if (start >= 0) result.push(value.slice(start, index));
        start = -1;
        continue;
      }
      if (start < 0) { start = index; continue; }
      if (!startsWord(value, index)) continue;
      result.push(value.slice(start, index));
      start = index;
    }
    if (start >= 0) result.push(value.slice(start));
    return result;
  }

  function matchPriority(name, query) {
    var lowerName = name.toLowerCase();
    var lowerQuery = query.toLowerCase();
    if (lowerName === lowerQuery) return 0;
    if (lowerName.indexOf(lowerQuery) === 0) return 1;
    var nameWords = words(name);
    var queryWords = words(query);
    if (queryWords.length === 0) return lowerName.indexOf(lowerQuery) >= 0 ? 4 : NO_MATCH;
    for (var start = 0; start <= nameWords.length - queryWords.length; start++) {
      var matches = true;
      for (var i = 0; i < queryWords.length; i++) {
        if (nameWords[start + i].toLowerCase().indexOf(queryWords[i].toLowerCase()) !== 0) { matches = false; break; }
      }
      if (matches) return start === 0 ? 2 : 3;
    }
    var compactQuery = queryWords.join("").toLowerCase();
    var compactName = nameWords.join("").toLowerCase();
    var offset = 0;
    for (var word = 0; word < nameWords.length; word++) {
      if (compactName.indexOf(compactQuery, offset) === offset) return word === 0 ? 2 : 3;
      offset += nameWords[word].length;
    }
    return lowerName.indexOf(lowerQuery) >= 0 ? 4 : NO_MATCH;
  }

  return { matchPriority: matchPriority, words: words, NO_MATCH: NO_MATCH };
})();
