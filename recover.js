// The Lost Key page: asks the license server to email the keys bought with an address again.
// The server answers the same whether or not the address bought Pro.

const form = document.getElementById("recover");
const status = document.getElementById("recoverStatus");
const server = form.dataset.server;
const button = form.querySelector("button");

if (!server) {
  button.disabled = true;
  status.textContent = "Key recovery opens when Tidy Pro goes on sale. Until then, write to the address below.";
}

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  if (!server) return;
  const email = form.email.value.trim();
  button.disabled = true;
  status.textContent = "Sending…";
  try {
    const response = await fetch(`${server}/recover`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email }),
    });
    const answer = await response.json().catch(() => ({}));
    status.textContent = response.ok
      ? `If ${email} bought Tidy Pro, its key is on the way. It can take a minute, and may land in spam.`
      : answer.error ?? "That didn't work. Try again in a few minutes.";
  } catch {
    status.textContent = "Couldn't reach the server. Check your connection and try again.";
  } finally {
    button.disabled = false;
  }
});
