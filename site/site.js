// Shared by every page: the mobile menu and the install command's Copy button.

const nav = document.getElementById("nav");
const toggle = document.getElementById("navToggle");

function setMenu(open) {
  nav.classList.toggle("open", open);
  toggle.setAttribute("aria-expanded", open);
  toggle.setAttribute("aria-label", open ? "Close menu" : "Open menu");
  document.documentElement.style.overflow = open ? "hidden" : "";
}
toggle.addEventListener("click", () => setMenu(!nav.classList.contains("open")));
nav.querySelectorAll(".nav-links a, .nav-cta").forEach((a) => a.addEventListener("click", () => setMenu(false)));
document.addEventListener("keydown", (e) => {
  if (e.key === "Escape" && nav.classList.contains("open")) { setMenu(false); toggle.focus(); }
});
window.matchMedia("(min-width: 761px)").addEventListener("change", (e) => { if (e.matches) setMenu(false); });

for (const box of document.querySelectorAll(".command")) {
  const button = box.querySelector(".copy");
  button.addEventListener("click", async () => {
    try {
      await navigator.clipboard.writeText(box.dataset.copy);
      button.textContent = "Copied";
    } catch {
      button.textContent = "Select and copy";
    }
    setTimeout(() => { button.textContent = "Copy"; }, 1600);
  });
}
