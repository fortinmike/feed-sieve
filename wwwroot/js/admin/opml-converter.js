(() => {
  const fileInput = document.getElementById("OpmlFile");
  const fileName = document.getElementById("opml-file-name");
  if (!(fileInput instanceof HTMLInputElement)) {
    return;
  }
  if (!(fileName instanceof HTMLElement)) {
    return;
  }

  fileInput.addEventListener("change", () => {
    const selectedFile = fileInput.files?.[0];
    fileName.textContent = selectedFile?.name ?? "No file selected";
  });
})();
