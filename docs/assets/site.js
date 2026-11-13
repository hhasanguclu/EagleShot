(function () {
  var root = document.documentElement;
  var I18N = window.SITE_I18N || {};
  var LANGS = [
    ["en", "English"], ["tr", "Türkçe"], ["zh-CN", "简体中文"], ["zh-TW", "繁體中文"], ["ja", "日本語"], ["ko", "한국어"],
    ["de", "Deutsch"], ["fr", "Français"], ["pl", "Polski"], ["uk", "Українська"], ["ru", "Русский"]
  ];
  var codes = LANGS.map(function (l) { return l[0]; });
  var latest = null; // filled from the GitHub API

  // ── Translations: English is in the page, other languages come from i18n.js ──
  var nodes = Array.prototype.slice.call(document.querySelectorAll("[data-i18n],[data-i18n-html]"));
  nodes.forEach(function (n) { n._en = n.innerHTML; });

  function t(key, fallback) {
    var d = I18N[root.lang] || {};
    return d[key] != null ? d[key] : fallback;
  }

  function match(name) {
    if (!name) return null;
    var n = name.toLowerCase().replace("_", "-");
    if (n.indexOf("zh") === 0) return /hant|-tw|-hk|-mo/.test(n) ? "zh-TW" : "zh-CN";
    var base = n.split("-")[0];
    return codes.indexOf(base) >= 0 ? base : null;
  }

  function detect() {
    var list = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language];
    for (var i = 0; i < list.length; i++) { var c = match(list[i]); if (c) return c; }
    return "en";
  }

  function stored() { try { return localStorage.getItem("eagleshot-lang"); } catch (e) { return null; } }

  function apply(lang) {
    root.lang = lang;
    var d = I18N[lang] || {};
    nodes.forEach(function (n) {
      var key = n.getAttribute("data-i18n") || n.getAttribute("data-i18n-html");
      var v = d[key];
      if (v == null) n.innerHTML = n._en;
      else if (n.hasAttribute("data-i18n-html")) n.innerHTML = v;
      else n.textContent = v;
    });
    document.getElementById("lang-select").value = lang;
    document.getElementById("hero-shot").src = "screenshots/languages/" + lang + ".png";
    document.getElementById("readme-link").href = "https://github.com/hhasanguclu/EagleShot/blob/master/" +
      (lang === "en" ? "README.md" : "README." + lang + ".md") + "#build-from-source";
    if (d["meta.description"]) document.querySelector('meta[name="description"]').content = d["meta.description"];
    updateDownloads();
    renderGallery();
  }

  function setLang(lang) {
    try { localStorage.setItem("eagleshot-lang", lang); } catch (e) {}
    apply(lang);
  }

  document.getElementById("lang-select").addEventListener("change", function (e) { setLang(e.target.value); });

  // ── Language gallery: a screenshot of the app in every language ──
  function renderGallery() {
    var g = document.getElementById("lang-gallery");
    g.innerHTML = "";
    LANGS.forEach(function (l) {
      var a = document.createElement("a");
      a.href = "#top";
      a.className = "langcard" + (l[0] === root.lang ? " current" : "");
      a.innerHTML = '<span class="sunken"><img loading="lazy" width="790" height="600" src="screenshots/languages/' + l[0] +
        '.png" alt="EagleShot – ' + l[1] + '"></span><span class="name">' + l[1] + "</span>";
      a.addEventListener("click", function () { setLang(l[0]); });
      g.appendChild(a);
    });
  }

  // ── Clock in the tray ──
  var clock = document.getElementById("clock");
  function tick() {
    var d = new Date();
    clock.textContent = d.getHours() + ":" + String(d.getMinutes()).padStart(2, "0");
  }
  tick(); setInterval(tick, 15000);

  // ── Start menu ──
  var start = document.getElementById("start-btn"), menu = document.getElementById("startmenu");
  function toggleMenu(open) {
    menu.classList.toggle("open", open);
    start.classList.toggle("pressed", open);
    start.setAttribute("aria-expanded", open ? "true" : "false");
  }
  start.addEventListener("click", function (e) { e.stopPropagation(); toggleMenu(!menu.classList.contains("open")); });
  document.addEventListener("click", function (e) { if (!menu.contains(e.target)) toggleMenu(false); });
  document.addEventListener("keydown", function (e) { if (e.key === "Escape") toggleMenu(false); });
  menu.addEventListener("click", function (e) { if (e.target.closest("a")) toggleMenu(false); });

  // ── Screenshot tabs ──
  var tabs = Array.prototype.slice.call(document.querySelectorAll('[role="tab"]'));
  function select(tab) {
    tabs.forEach(function (x) {
      var on = x === tab;
      x.setAttribute("aria-selected", on ? "true" : "false");
      x.tabIndex = on ? 0 : -1;
      document.getElementById(x.getAttribute("aria-controls")).hidden = !on;
    });
  }
  tabs.forEach(function (x, i) {
    x.addEventListener("click", function () { select(x); });
    x.addEventListener("keydown", function (e) {
      var n = e.key === "ArrowRight" ? 1 : e.key === "ArrowLeft" ? -1 : 0;
      if (!n) return;
      var next = tabs[(i + n + tabs.length) % tabs.length];
      select(next); next.focus();
    });
  });

  // ── Decorative block progress bar in the setup window ──
  var bar = document.getElementById("progress");
  for (var i = 0; i < 60; i++) bar.appendChild(document.createElement("i"));

  // ── The visitor's platform goes first ──
  var ua = (navigator.userAgent || "").toLowerCase();
  var os = ua.indexOf("windows") >= 0 ? "win"
    : (ua.indexOf("mac") >= 0 && ua.indexOf("iphone") < 0 && ua.indexOf("ipad") < 0) ? "mac"
    : (ua.indexOf("linux") >= 0 && ua.indexOf("android") < 0) ? "linux" : null;
  if (os) {
    var row = document.querySelector('.platform[data-os="' + os + '"]');
    if (row) { row.classList.add("detected"); row.parentNode.insertBefore(row, row.parentNode.firstChild); }
  }

  // ── Real download links and version from the latest GitHub release ──
  var patterns = {
    "win-setup": /_Setup_.*win-x64\.exe$/,
    "win-zip": /win-x64\.zip$/,
    "mac-arm": /osx-arm64\.dmg$/,
    "mac-x64": /osx-x64\.dmg$/,
    "linux": /linux-x64\.tar\.gz$/
  };
  var primaryFor = { win: "win-setup", mac: "mac-arm", linux: "linux" };
  var forKey = { win: ["dl.for.win", "for Windows (installer)"], mac: ["dl.for.mac", "for macOS (Apple Silicon)"], linux: ["dl.for.linux", "for Linux (.tar.gz)"] };

  function updateDownloads() {
    if (!latest) return;
    var status = document.getElementById("status-text");
    status.textContent = t("status.latest", "Ready. Latest release: v{0}").replace("{0}", latest.version);
    if (os && latest.urls[primaryFor[os]]) {
      document.getElementById("hero-primary").href = latest.urls[primaryFor[os]];
      document.getElementById("hero-primary-sub").textContent = t(forKey[os][0], forKey[os][1]) + " · v" + latest.version;
    }
  }

  fetch("https://api.github.com/repos/hhasanguclu/EagleShot/releases/latest")
    .then(function (r) { return r.ok ? r.json() : Promise.reject(r.status); })
    .then(function (rel) {
      var urls = {};
      (rel.assets || []).forEach(function (a) {
        for (var k in patterns) if (patterns[k].test(a.name)) urls[k] = a.browser_download_url;
      });
      document.querySelectorAll("[data-asset]").forEach(function (el) {
        var u = urls[el.getAttribute("data-asset")];
        if (u) el.href = u;
      });
      var v = (rel.tag_name || "").replace(/^v/, "");
      if (v) document.querySelectorAll(".version-text").forEach(function (el) {
        el.textContent = /^\d/.test(el.textContent) ? v : "v" + v;
      });
      latest = { version: v, urls: urls };
      updateDownloads();
    })
    .catch(function () { /* the links already point to the releases page */ });

  // ── Start in the remembered language, else the browser's ──
  var initial = stored();
  apply(codes.indexOf(initial) >= 0 ? initial : detect());
})();
