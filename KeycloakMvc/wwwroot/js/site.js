(function () {
  "use strict";

  // Password visibility toggle: <button data-password-toggle="InputId">. The field stays
  // masked by default and is masked again before the form is submitted.
  document.querySelectorAll("[data-password-toggle]").forEach(function (button) {
    var input = document.getElementById(button.getAttribute("data-password-toggle"));
    var use = button.querySelector("use");
    if (!input) return;

    function setVisible(visible) {
      input.type = visible ? "text" : "password";
      button.setAttribute("aria-pressed", String(visible));
      button.setAttribute("aria-label", visible ? "Hide password" : "Show password");
      if (use) {
        use.setAttribute("href", use.getAttribute("href").replace(/#.*$/, visible ? "#eye-off" : "#eye"));
      }
    }

    button.addEventListener("click", function () {
      setVisible(input.type === "password");
    });
    if (input.form) {
      input.form.addEventListener("submit", function () {
        setVisible(false);
      });
    }
  });

  // Loading state for <form data-loading-form>: shows a spinner on the submit button and
  // blocks repeated submission while the request is pending.
  document.querySelectorAll("form[data-loading-form]").forEach(function (form) {
    var button = form.querySelector('button[type="submit"]');
    var label = button && button.querySelector(".btn-app__label");
    var idleText = label ? label.textContent : "";
    var pending = false;

    function reset() {
      pending = false;
      if (!button) return;
      button.disabled = false;
      button.removeAttribute("aria-busy");
      if (label) label.textContent = idleText;
    }

    form.addEventListener("submit", function (event) {
      if (pending) {
        event.preventDefault();
        return;
      }
      // Leave invalid forms alone so client-side validation can show its messages.
      var $ = window.jQuery;
      if ($ && $.fn.valid && !$(form).valid()) return;

      pending = true;
      if (!button) return;
      // Disable after the browser has captured the submission.
      setTimeout(function () {
        button.disabled = true;
        button.setAttribute("aria-busy", "true");
        if (label) label.textContent = button.getAttribute("data-loading-text") || idleText;
      }, 0);
    });

    // Restore the button when the page is shown again from the back/forward cache.
    window.addEventListener("pageshow", function (event) {
      if (event.persisted) reset();
    });
  });
})();
