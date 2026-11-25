// The pricing section's Buy button. It opens Paddle's checkout once the site was built with
// Paddle's client token and price (see scripts/build-site.mjs); until then it says Pro goes on
// sale soon. Paddle.js loads only on the first click, so the page loads nothing from Paddle.

const buy = document.getElementById("buyPro");
const { paddleEnvironment, paddleToken, paddlePrice } = buy.dataset;

if (!paddleToken || !paddlePrice) {
  buy.setAttribute("aria-disabled", "true");
  buy.textContent = "On sale soon";
  buy.removeAttribute("href");
} else {
  let ready;
  const loadPaddle = () =>
    (ready ??= new Promise((resolve, reject) => {
      const script = document.createElement("script");
      script.src = "https://cdn.paddle.com/paddle/v2/paddle.js";
      script.onload = () => {
        if (paddleEnvironment === "sandbox") window.Paddle.Environment.set("sandbox");
        window.Paddle.Initialize({ token: paddleToken });
        resolve(window.Paddle);
      };
      script.onerror = () => {
        ready = undefined;
        reject(new Error("Paddle didn't load"));
      };
      document.head.append(script);
    }));

  // Paddle sends people to the default payment link with ?_ptxn=txn_... (say from an invoice);
  // Paddle.js opens that checkout by itself once it loads.
  if (new URLSearchParams(location.search).has("_ptxn")) loadPaddle().catch(() => {});

  buy.addEventListener("click", async (event) => {
    event.preventDefault();
    try {
      (await loadPaddle()).Checkout.open({ items: [{ priceId: paddlePrice, quantity: 1 }] });
    } catch {
      buy.textContent = "Checkout didn't load. Try again";
    }
  });
}
