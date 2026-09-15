(() => {
  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  if (reduceMotion) return;

  const initialiseBalatroCards = () => {
    document.querySelectorAll("[data-balatro-card]").forEach(card => {
      if (card.dataset.balatroReady) return;
      card.dataset.balatroReady = "true";

      const state = {
        targetX: 0,
        targetY: 0,
        x: 0,
        y: 0,
        velocityX: 0,
        velocityY: 0,
        active: false
      };

      const animate = () => {
        const force = 0.13;
        const damping = 0.78;
        state.velocityX = (state.velocityX + (state.targetX - state.x) * force) * damping;
        state.velocityY = (state.velocityY + (state.targetY - state.y) * force) * damping;
        state.x += state.velocityX;
        state.y += state.velocityY;

        const rotateY = state.x * 9;
        const rotateX = state.y * -9;
        const lift = state.active ? 7 : 0;
        const scale = state.active ? 1.018 : 1;
        card.style.setProperty("--balatro-rotate-x", `${rotateX.toFixed(2)}deg`);
        card.style.setProperty("--balatro-rotate-y", `${rotateY.toFixed(2)}deg`);
        card.style.setProperty("--balatro-lift", `${lift.toFixed(2)}px`);
        card.style.setProperty("--balatro-scale", scale.toFixed(3));
        card.style.setProperty("--balatro-shine-x", `${((state.x + 1) * 50).toFixed(1)}%`);
        card.style.setProperty("--balatro-shine-y", `${((state.y + 1) * 50).toFixed(1)}%`);

        if (state.active || Math.abs(state.x) > 0.01 || Math.abs(state.y) > 0.01) {
          requestAnimationFrame(animate);
        }
      };

      card.addEventListener("pointerenter", () => {
        state.active = true;
        requestAnimationFrame(animate);
      });

      card.addEventListener("pointermove", event => {
        const bounds = card.getBoundingClientRect();
        state.targetX = ((event.clientX - bounds.left) / bounds.width - 0.5) * 2;
        state.targetY = ((event.clientY - bounds.top) / bounds.height - 0.5) * 2;
      });

      card.addEventListener("pointerleave", () => {
        state.active = false;
        state.targetX = 0;
        state.targetY = 0;
        requestAnimationFrame(animate);
      });
    });
  };

  document.addEventListener("DOMContentLoaded", initialiseBalatroCards);
  document.addEventListener("htmx:afterSwap", initialiseBalatroCards);
})();
