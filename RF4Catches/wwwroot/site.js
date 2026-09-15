(() => {
  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  // ---------- Balatro card tilt ----------
  const initialiseBalatroCards = () => {
    if (reduceMotion) return;

    document.querySelectorAll("[data-balatro-card]").forEach(card => {
      if (card.dataset.balatroReady) return;
      card.dataset.balatroReady = "true";

      const state = {
        targetX: 0, targetY: 0, x: 0, y: 0,
        velocityX: 0, velocityY: 0, active: false
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

  // ---------- Confirmation modal (drives data-confirm buttons) ----------
  const initialiseConfirmModal = () => {
    const modal = document.getElementById("confirm-modal");
    if (!modal || modal.dataset.confirmReady) return;
    modal.dataset.confirmReady = "true";

    const modalMessage = document.getElementById("confirm-modal-message");
    const modalConfirm = document.getElementById("confirm-modal-confirm");
    const modalCancel = document.getElementById("confirm-modal-cancel");

    let pendingButton = null;

    // Capture phase — runs before HTMX's own click handler on document.body.
    document.body.addEventListener("click", e => {
      const btn = e.target.closest("[data-confirm]");
      if (!btn) return;

      // Second pass: user confirmed. Let the event through so HTMX issues
      // the request this time.
      if (btn.dataset.confirmApproved === "1") {
        delete btn.dataset.confirmApproved;
        return;
      }

      // First pass: intercept, show modal.
      e.preventDefault();
      e.stopPropagation();

      pendingButton = btn;
      modalMessage.textContent = btn.dataset.confirm;

      const cleanup = () => {
        modalConfirm.removeEventListener("click", onConfirm);
        modalCancel.removeEventListener("click", onCancel);
        modal.removeEventListener("close", onCancel);
      };
      const onConfirm = () => {
        const b = pendingButton;
        pendingButton = null;
        cleanup();
        modal.close();
        if (b) {
          b.dataset.confirmApproved = "1";
          b.click(); // re-dispatch — second pass lets HTMX through
        }
      };
      const onCancel = () => {
        pendingButton = null;
        cleanup();
        modal.close();
      };

      modalConfirm.addEventListener("click", onConfirm);
      modalCancel.addEventListener("click", onCancel);
      modal.addEventListener("close", onCancel);

      modal.showModal();
    }, true); // <- capture phase, crucial
  };

  // ---------- Toasts ----------
  const initialiseToasts = () => {
    const toastContainer = document.getElementById("toast-container");
    if (!toastContainer || toastContainer.dataset.toastReady) return;
    toastContainer.dataset.toastReady = "true";

    const showToast = (message, type) => {
      type = type || "success";
      const alertClass =
          type === "error" ? "alert-error" :
              type === "warning" ? "alert-warning" :
                  type === "info" ? "alert-info" :
                      "alert-success";

      const el = document.createElement("div");
      el.className = "alert " + alertClass + " shadow-lg";
      el.setAttribute("role", "status");
      const span = document.createElement("span");
      span.textContent = message;
      el.appendChild(span);
      toastContainer.appendChild(el);

      setTimeout(() => {
        el.style.transition = "opacity 300ms ease, transform 300ms ease";
        el.style.opacity = "0";
        el.style.transform = "translateY(8px)";
        setTimeout(() => el.remove(), 320);
      }, 3000);
    };

    window.rf4ShowToast = showToast;

    document.body.addEventListener("showToast", e => {
      const d = e.detail || {};
      showToast(d.message || "Done", d.type || "success");
    });

    document.body.addEventListener("htmx:afterRequest", e => {
      if (!e.detail.successful) return;
      const verb = ((e.detail.requestConfig && e.detail.requestConfig.verb) || "get").toLowerCase();
      if (verb === "get") return;
      const trigger = e.detail.xhr.getResponseHeader("HX-Trigger");
      if (trigger && trigger.indexOf("showToast") !== -1) return;
      showToast("Done", "success");
    });

    document.body.addEventListener("htmx:responseError", e => {
      const trigger = e.detail.xhr.getResponseHeader("HX-Trigger");
      if (trigger && trigger.indexOf("showToast") !== -1) return;

      const status = e.detail.xhr.status;
      const message =
          status === 404 ? "Not found" :
              status === 400 ? "Invalid request" :
                  status >= 500 ? "Server error" :
                      "Request failed (" + status + ")";
      showToast(message, "error");
    });

    document.body.addEventListener("htmx:sendError", () => {
      showToast("Network error", "error");
    });
  };

  // ---------- Dashboard version poller ----------
  // Polls a lightweight /dashboard/version endpoint and triggers a content
  // refresh only when the version string changes. Avoids refetching the full
  // dashboard every 5 seconds when nothing has actually changed.
  const initialiseDashboardVersionPoller = () => {
    let lastVersion = null;

    document.body.addEventListener("htmx:afterRequest", e => {
      const el = e.detail.elt;
      if (!el || el.id !== "dashboard-version") return;
      if (!e.detail.successful) return;

      let version;
      try {
        version = JSON.parse(e.detail.xhr.responseText).version;
      } catch {
        return;
      }

      // First response establishes the baseline without triggering a refresh;
      // the initial /dashboard/content load already handles that.
      if (lastVersion === null) {
        lastVersion = version;
        return;
      }

      if (version !== lastVersion) {
        lastVersion = version;
        htmx.trigger(document.body, "refreshDashboard");
      }
    });
  };

  // ---------- Boot ----------
  const initialise = () => {
    initialiseBalatroCards();
    initialiseConfirmModal();
    initialiseToasts();
    initialiseDashboardVersionPoller();
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialise);
  } else {
    initialise();
  }

  document.addEventListener("htmx:afterSwap", initialiseBalatroCards);
})();