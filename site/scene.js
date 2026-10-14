// The home page's scroll story: the sun sets over the landscape, the sky turns to night, then the
// content slides in. Landscape (inline in index.html) and choreography by isladjan (https://isladjan.com/work/4/), MIT.
//
// Everything is timed against .scrollElement, which is 6000px tall (as is .spacer). Change that
// height and every start/end below has to move with it, or scenes overlap.

gsap.registerPlugin(ScrollTrigger);

const speed = 100;
const height = document.querySelector("svg.parallax").getBBox().height;
const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

gsap.set("#h2-1", { opacity: 0 });
gsap.set("#bg_grad", { attr: { cy: "-50" } });
gsap.set(["#dinoL", "#dinoR"], { y: 80 });
gsap.set("#dinoL", { x: -10 });
gsap.matchMedia().add("(max-width: 1922px)", () => {
  gsap.set(["#cloudStart-L", "#cloudStart-R"], { x: 10, opacity: 1 });
});

/* Scene 1: hills and clouds part */
const scene1 = gsap.timeline();
ScrollTrigger.create({ animation: scene1, trigger: ".scrollElement", start: "top top", end: "45% 100%", scrub: 3 });
scene1.to("#h1-1", { y: 3 * speed, x: 1 * speed, scale: 0.9, ease: "power1.in" }, 0);
scene1.to("#h1-2", { y: 2.6 * speed, x: -0.6 * speed, ease: "power1.in" }, 0);
scene1.to("#h1-3", { y: 1.7 * speed, x: 1.2 * speed }, 0.03);
scene1.to("#h1-4", { y: 3 * speed, x: 1 * speed }, 0.03);
scene1.to("#h1-5", { y: 2 * speed, x: 1 * speed }, 0.03);
scene1.to("#h1-6", { y: 2.3 * speed, x: -2.5 * speed }, 0);
scene1.to("#h1-7", { y: 5 * speed, x: 1.6 * speed }, 0);
scene1.to("#h1-8", { y: 3.5 * speed, x: 0.2 * speed }, 0);
scene1.to("#h1-9", { y: 3.5 * speed, x: -0.2 * speed }, 0);
scene1.to("#cloudsBig-L", { y: 4.5 * speed, x: -0.2 * speed }, 0);
scene1.to("#cloudsBig-R", { y: 4.5 * speed, x: -0.2 * speed }, 0);
scene1.to("#cloudStart-L", { x: -300 }, 0);
scene1.to("#cloudStart-R", { x: 300 }, 0);

gsap.fromTo("#bird", { opacity: 1 }, {
  y: -250, x: 800, ease: "power2.out",
  scrollTrigger: {
    trigger: ".scrollElement", start: "15% top", end: "60% 100%", scrub: 4,
    onEnter: () => gsap.to("#bird", { scaleX: 1, rotation: 0 }),
    onLeave: () => gsap.to("#bird", { scaleX: -1, rotation: -15 })
  }
});

const clouds = gsap.timeline();
ScrollTrigger.create({ animation: clouds, trigger: ".scrollElement", start: "top top", end: "70% 100%", scrub: 1 });
clouds.to("#cloud1", { x: 500 }, 0);
clouds.to("#cloud2", { x: 1000 }, 0);
clouds.to("#cloud3", { x: -1000 }, 0);
clouds.to("#cloud4", { x: -700, y: 25 }, 0);

/* The sun sets */
const sun = gsap.timeline();
ScrollTrigger.create({ animation: sun, trigger: ".scrollElement", start: "1% top", end: "2150 100%", scrub: 2 });
sun.fromTo("#bg_grad", { attr: { cy: "-50" } }, { attr: { cy: "330" } }, 0);
sun.to("#bg_grad stop:nth-child(2)", { attr: { offset: "0.15" } }, 0);
sun.to("#bg_grad stop:nth-child(3)", { attr: { offset: "0.18" } }, 0);
sun.to("#bg_grad stop:nth-child(4)", { attr: { offset: "0.25" } }, 0);
sun.to("#bg_grad stop:nth-child(5)", { attr: { offset: "0.46" } }, 0);
sun.to("#bg_grad stop:nth-child(6)", { attr: { "stop-color": "#FF9171" } }, 0);

/* Scene 2: new hills rise */
const scene2 = gsap.timeline();
ScrollTrigger.create({ animation: scene2, trigger: ".scrollElement", start: "15% top", end: "40% 100%", scrub: 3 });
scene2.fromTo("#h2-1", { y: 500, opacity: 0 }, { y: 0, opacity: 1 }, 0);
scene2.fromTo("#h2-2", { y: 500 }, { y: 0 }, 0.1);
scene2.fromTo("#h2-3", { y: 700 }, { y: 0 }, 0.1);
scene2.fromTo("#h2-4", { y: 700 }, { y: 0 }, 0.2);
scene2.fromTo("#h2-5", { y: 800 }, { y: 0 }, 0.3);
scene2.fromTo("#h2-6", { y: 900 }, { y: 0 }, 0.3);

gsap.set("#bats", { transformOrigin: "50% 50%" });
gsap.fromTo("#bats", { opacity: 1, y: 400, scale: 0 }, {
  y: 20, scale: 0.8, ease: "power3.out",
  scrollTrigger: {
    trigger: ".scrollElement", start: "40% top", end: "70% 100%", scrub: 3,
    onEnter: () => {
      gsap.utils.toArray("#bats path").forEach((item, i) => {
        gsap.to(item, { scaleX: 0.5, yoyo: true, repeat: 9, transformOrigin: "50% 50%", duration: 0.15, delay: 0.7 + i / 10 });
      });
      gsap.set("#bats", { opacity: 1 });
    }
  }
});

/* Dusk: the sun swells and the sky darkens */
const sun2 = gsap.timeline();
ScrollTrigger.create({ animation: sun2, trigger: ".scrollElement", start: "2000 top", end: "5000 100%", scrub: 2 });
sun2.to("#sun", { attr: { offset: "1.4" } }, 0);
sun2.to("#bg_grad stop:nth-child(2)", { attr: { offset: "0.7" } }, 0);
sun2.to("#sun", { attr: { "stop-color": "#ffff00" } }, 0);
sun2.to("#lg4 stop:nth-child(1)", { attr: { "stop-color": "#623951" } }, 0);
sun2.to("#lg4 stop:nth-child(2)", { attr: { "stop-color": "#261F36" } }, 0);
sun2.to("#bg_grad stop:nth-child(6)", { attr: { "stop-color": "#45224A" } }, 0);

/* Scene 2 to scene 3 */
gsap.set("#scene3", { y: height - 40, visibility: "visible" });
const sceneTransition = gsap.timeline();
ScrollTrigger.create({ animation: sceneTransition, trigger: ".scrollElement", start: "60% top", end: "bottom 100%", scrub: 3 });
sceneTransition.to("#h2-1", { y: -height - 100, scale: 1.5, transformOrigin: "50% 50%" }, 0);
sceneTransition.to("#bg_grad", { attr: { cy: "-80" } }, 0.0);
sceneTransition.to("#bg2", { y: 0 }, 0);

/* Scene 3: night */
const scene3 = gsap.timeline();
ScrollTrigger.create({ animation: scene3, trigger: ".scrollElement", start: "70% 50%", end: "bottom 100%", scrub: 3 });
scene3.fromTo("#h3-1", { y: 300 }, { y: -550 }, 0);
scene3.fromTo("#h3-2", { y: 800 }, { y: -550 }, 0.03);
scene3.fromTo("#h3-3", { y: 600 }, { y: -550 }, 0.06);
scene3.fromTo("#h3-4", { y: 800 }, { y: -550 }, 0.09);
scene3.fromTo("#h3-5", { y: 1000 }, { y: -550 }, 0.12);
scene3.fromTo("#stars", { opacity: 0 }, { opacity: 0.5, y: -500 }, 0);
scene3.to("#bg2-grad", { attr: { cy: 600 } }, 0);
scene3.to("#bg2-grad", { attr: { r: 500 } }, 0);

/* Falling star */
gsap.set("#fstar", { y: -400 });
const fstar = gsap.timeline();
ScrollTrigger.create({
  animation: fstar, trigger: ".scrollElement", start: "4200 top", end: "6000 bottom", scrub: 2,
  onEnter: () => gsap.set("#fstar", { opacity: 1 }),
  onLeave: () => gsap.set("#fstar", { opacity: 0 })
});
fstar.to("#fstar", { x: -700, y: -250, ease: "power2.out" }, 0);

if (!reduceMotion) {
  [[1, 0.8], [3, 1.8], [5, 1], [8, 1.2], [11, 0.5], [15, 2], [17, 1.1], [18, 1.4],
   [25, 1.1], [28, 0.9], [30, 1.3], [35, 2], [40, 0.8], [45, 1.8], [48, 1]].forEach(([n, d]) => {
    gsap.fromTo(`#stars path:nth-of-type(${n})`, { opacity: 0.3 }, { opacity: 1, duration: 0.3, repeat: -1, repeatDelay: d });
  });
}

/* ===== The page over the landscape ===== */

// The hero lifts away as the landscape starts moving.
gsap.to(".hero", {
  autoAlpha: 0, y: -80, ease: "none",
  scrollTrigger: { trigger: ".scrollElement", start: "top top", end: "9% top", scrub: 1 }
});

// Story captions fade in, hold, and fade out within their scroll window.
function caption(selector, start, end) {
  gsap.timeline({ scrollTrigger: { trigger: ".scrollElement", start, end, scrub: 1 } })
    .fromTo(selector, { autoAlpha: 0, y: 40 }, { autoAlpha: 1, y: 0, duration: 0.3 })
    .to(selector, { autoAlpha: 1, duration: 0.4 })
    .to(selector, { autoAlpha: 0, y: -40, duration: 0.3 });
}
caption("#cap1", "20% top", "44% top");
caption("#cap2", "76% top", "95% top");

// The nav is plum over the sunset and turns to glass once the page scrolls.
const header = document.getElementById("nav");
const setSolid = () => header.classList.toggle("solid", window.scrollY > 140);
window.addEventListener("scroll", setSolid, { passive: true });
setSolid();

if (!reduceMotion) {
  gsap.utils.toArray(".reveal").forEach((el) => {
    gsap.from(el, { y: 36, opacity: 0, duration: 0.9, ease: "power2.out", scrollTrigger: { trigger: el, start: "top 88%", once: true } });
  });
}

// Start at the top on reload, so the story doesn't open half way through a scene.
window.addEventListener("beforeunload", () => window.scrollTo(0, 0));
