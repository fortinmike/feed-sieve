const fileInput = document.getElementById("OpmlFile");
const fileName = document.getElementById("opml-file-name");
if (!(fileInput instanceof HTMLInputElement)) {
  throw new Error("Expected #OpmlFile input to exist");
}
if (!(fileName instanceof HTMLElement)) {
  throw new Error("Expected #opml-file-name element to exist");
}

fileInput.addEventListener("change", () => {
  const selectedFile = fileInput.files?.[0];
  fileName.textContent = selectedFile?.name ?? "No file selected";
});
