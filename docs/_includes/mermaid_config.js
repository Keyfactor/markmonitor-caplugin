/* The theme's default mermaid_config.js is just `{}`, which renders with Mermaid's light-oriented
   "default" theme regardless of the page's color scheme - illegible against our dark background.
   data-color-scheme is set by head_custom.html before this script runs (see components/mermaid.html).
   NOTE: this repo's layout chain runs through a compress.html layout that strips newlines from the
   final HTML, so double-slash line comments here would swallow the rest of the script - block
   comments only, and never write the close-comment delimiter inside this comment's text. */
(function () {
  var scheme = document.documentElement.getAttribute("data-color-scheme");
  return { theme: scheme === "keyfactor" ? "default" : "dark" };
})()
