const copyButtons = document.querySelectorAll(".converter-copy-button");
copyButtons.forEach((button) => {
  const markCopied = () => {
    button.classList.add("is-copied");
    window.setTimeout(() => button.classList.remove("is-copied"), 1200);
  };

  button.addEventListener("click", async () => {
    const targetId = button.getAttribute("data-copy-target");
    if (!targetId) {
      return;
    }

    const input = document.getElementById(targetId);
    if (!(input instanceof HTMLInputElement)) {
      return;
    }

    try {
      await navigator.clipboard.writeText(input.value);
      markCopied();
    } catch {
      input.select();
      document.execCommand("copy");
      input.setSelectionRange(0, 0);
      markCopied();
    }
  });
});
