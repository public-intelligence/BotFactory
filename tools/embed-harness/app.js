const PRODUCT_URL = "https://bf.example.com";

const selectEl = document.getElementById("bot-select");
const frameEl = document.getElementById("embed-frame");
const directLinkEl = document.getElementById("direct-link");
const statusEl = document.getElementById("status");
const helpBtn = document.getElementById("help-btn");
const helpDialog = document.getElementById("help-dialog");
const harnessOriginEl = document.getElementById("harness-origin");
const snippetEl = document.getElementById("snippet");
const copySnippetBtn = document.getElementById("copy-snippet");

/** @type {{ name: string, slug?: string, publicId: string }[]} */
let bots = [];
/** @type {{ name: string, slug?: string, publicId: string } | null} */
let current = null;

harnessOriginEl.textContent = location.origin;

function queryPublicId() {
  const params = new URLSearchParams(location.search);
  return (params.get("publicId") || params.get("id") || "").trim();
}

function showError(message) {
  statusEl.hidden = !message;
  statusEl.textContent = message || "";
  statusEl.classList.toggle("error", !!message);
}

function findKnown(key) {
  const k = (key || "").trim();
  return bots.find((b) => b.publicId === k) || null;
}

function embedUrl(publicId) {
  return `${PRODUCT_URL}/api/embed/${encodeURIComponent(publicId)}`;
}

function directChatUrl(bot) {
  // Direct chat accepts slug or publicId; prefer slug when we have it.
  const key = bot.slug || bot.publicId;
  return `${PRODUCT_URL}/chat/${encodeURIComponent(key)}`;
}

function buildSnippet(bot) {
  const publicId = bot?.publicId || "PUBLIC_ID";
  const title = (bot?.name || "Asistente virtual").replace(/"/g, "&quot;");
  return [
    "<iframe",
    `  src="${embedUrl(publicId)}"`,
    `  title="${title}"`,
    '  width="100%"',
    '  height="600"',
    '  style="border: 0; border-radius: 12px"',
    '  allow="microphone; autoplay"',
    "></iframe>",
  ].join("\n");
}

function syncUrl(publicId) {
  const url = new URL(location.href);
  if (publicId) {
    url.searchParams.set("publicId", publicId);
    url.searchParams.delete("id");
    url.searchParams.delete("slug");
  } else {
    url.searchParams.delete("publicId");
    url.searchParams.delete("id");
    url.searchParams.delete("slug");
  }
  history.replaceState(null, "", url);
}

function fillSelect(selectedId) {
  selectEl.innerHTML = "";
  const placeholder = document.createElement("option");
  placeholder.value = "";
  placeholder.textContent = bots.length ? "Elegir bot…" : "No hay bots en la lista";
  selectEl.appendChild(placeholder);

  for (const bot of bots) {
    const opt = document.createElement("option");
    opt.value = bot.publicId;
    opt.textContent = bot.name;
    if (bot.publicId === selectedId) opt.selected = true;
    selectEl.appendChild(opt);
  }
}

function updateSnippet() {
  snippetEl.textContent = buildSnippet(current);
}

function loadBot(key) {
  const known = findKnown(key);
  current = known;
  updateSnippet();

  if (!key) {
    frameEl.removeAttribute("src");
    directLinkEl.href = "#";
    syncUrl("");
    showError("");
    return;
  }

  if (!known) {
    // Allow loading any publicId even if it isn't in bots.json.
    current = { name: key, publicId: key };
    updateSnippet();
    syncUrl(key);
    fillSelect("");
    frameEl.src = embedUrl(key);
    directLinkEl.href = `${PRODUCT_URL}/chat/${encodeURIComponent(key)}`;
    showError("");
    return;
  }

  syncUrl(known.publicId);
  fillSelect(known.publicId);
  frameEl.src = embedUrl(known.publicId);
  directLinkEl.href = directChatUrl(known);
  showError("");
}

async function init() {
  try {
    const res = await fetch("./bots.json", { cache: "no-store" });
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.json();
    bots = Array.isArray(data)
      ? data.filter((b) => b && b.publicId)
      : [];
  } catch {
    bots = [];
    showError("No se pudo cargar la lista de bots.");
  }

  const fromQuery = queryPublicId();
  const initial = fromQuery || bots[0]?.publicId || "";
  fillSelect(initial);
  if (initial) loadBot(initial);
  else updateSnippet();
}

selectEl.addEventListener("change", () => {
  if (selectEl.value) loadBot(selectEl.value);
});

helpBtn.addEventListener("click", () => {
  updateSnippet();
  helpDialog.showModal();
});

copySnippetBtn.addEventListener("click", async () => {
  try {
    await navigator.clipboard.writeText(snippetEl.textContent);
    copySnippetBtn.textContent = "Copiado";
    setTimeout(() => {
      copySnippetBtn.textContent = "Copiar código";
    }, 1500);
  } catch {
    copySnippetBtn.textContent = "No se pudo copiar";
    setTimeout(() => {
      copySnippetBtn.textContent = "Copiar código";
    }, 1500);
  }
});

helpDialog.addEventListener("click", (e) => {
  if (e.target === helpDialog) helpDialog.close();
});

init();
