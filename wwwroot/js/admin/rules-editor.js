(() => {
  const rulesForm = document.getElementById("rulesForm");
  const addRuleButton = document.getElementById("addRuleButton");
  const saveRulesButton = document.getElementById("saveRulesButton");
  const rulesList = document.getElementById("rulesList");
  const rulesEmptyState = document.getElementById("rulesEmptyState");
  const rulesSummary = document.getElementById("rulesSummary");
  const rulesJsonInput = document.getElementById("RulesJson");
  const initialRulesData = document.getElementById("initialRulesData");
  const ruleRowTemplate = document.getElementById("ruleRowTemplate");

  const regexErrorProbe = document.createElement("div");
  const getRegexAnalysis = (pattern) => {
    if (pattern === "") {
      return { html: "", error: null };
    }

    const html = window.RegexColorizer.colorizePattern(pattern);
    regexErrorProbe.innerHTML = html;
    const errorNode = regexErrorProbe.querySelector(".err");
    if (errorNode) {
      return {
        html,
        error: errorNode.getAttribute("title") || "Regex has an invalid token",
      };
    }

    return { html, error: null };
  };

  const tagRegexOverlayTokens = (regexOverlay) => {
    // RegexColorizer outputs the markup but doesn't provide dedicated classes for these token types,
    // so we tag them here to apply our custom theme colors in CSS
    regexOverlay.querySelectorAll("b").forEach((tokenNode) => {
      const token = tokenNode.textContent ?? "";
      tokenNode.classList.toggle("rule-alt", token === "|");
      tokenNode.classList.toggle("rule-anchor", token === "^" || token === "$");
    });
  };

  const createOverlayField = ({ input, overlay, render, afterRender }) => {
    const syncScroll = () => {
      overlay.scrollLeft = input.scrollLeft;
    };

    const update = () => {
      const state = render(input.value);
      overlay.innerHTML = state.html;
      if (afterRender) {
        afterRender({ overlay, state });
      }
      syncScroll();
      return state;
    };

    input.addEventListener("scroll", syncScroll);
    return { update, syncScroll };
  };

  const escapeHtml = (value) => {
    return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
  };

  const getRegexTestOverlayState = (sample, pattern, isInvalidPattern) => {
    if (sample === "") {
      return { html: "", isMatch: false };
    }
    if (pattern === "" || isInvalidPattern) {
      return { html: escapeHtml(sample), isMatch: false };
    }

    let regex;
    try {
      regex = new RegExp(pattern, "gi");
    } catch {
      return { html: escapeHtml(sample), isMatch: false };
    }

    const parts = [];
    let lastIndex = 0;
    let isMatch = false;
    let match = regex.exec(sample);
    while (match !== null) {
      const matchValue = match[0];
      const startIndex = match.index;
      const endIndex = startIndex + matchValue.length;

      if (endIndex > startIndex) {
        if (startIndex > lastIndex) {
          parts.push(escapeHtml(sample.slice(lastIndex, startIndex)));
        }

        parts.push(`<mark>${escapeHtml(matchValue)}</mark>`);
        lastIndex = endIndex;
        isMatch = true;
      }

      if (matchValue.length === 0) {
        if (regex.lastIndex >= sample.length) {
          break;
        }

        regex.lastIndex += 1;
      }

      match = regex.exec(sample);
    }

    if (lastIndex < sample.length) {
      parts.push(escapeHtml(sample.slice(lastIndex)));
    }

    return { html: parts.join(""), isMatch };
  };

  const parseInitialRules = () => {
    try {
      const parsed = JSON.parse(initialRulesData.textContent ?? "[]");
      return Array.isArray(parsed) ? parsed : [];
    } catch {
      return [];
    }
  };

  const normalizeRule = (rule) => {
    return {
      name: typeof rule?.name === "string" ? rule.name : "",
      feed: typeof rule?.feed === "string" ? rule.feed : "",
      match: typeof rule?.match === "string" ? rule.match : "title",
      regex: typeof rule?.regex === "string" ? rule.regex : "",
      sample: typeof rule?.sample === "string" ? rule.sample : "",
    };
  };

  const normalizeMatch = (match) => {
    return match === "content" || match === "all" ? match : "title";
  };

  const getRuleRowElements = (rowElement) => {
    return {
      row: rowElement,
      nameInput: rowElement.querySelector(".rule-input-name"),
      feedInput: rowElement.querySelector(".rule-input-feed"),
      matchInput: rowElement.querySelector(".rule-input-match"),
      regexInput: rowElement.querySelector(".rule-input-regex"),
      regexTestToggle: rowElement.querySelector(".rule-regex-test-toggle"),
      regexTestEditor: rowElement.querySelector(".rule-regex-test-editor"),
      regexTestInput: rowElement.querySelector(".rule-input-regex-test"),
      regexTestOverlay: rowElement.querySelector(".rule-regex-test-overlay"),
      regex101Link: rowElement.querySelector(".rule-regex101-link"),
      regexOverlay: rowElement.querySelector(".rule-regex-overlay"),
      regexValidation: rowElement.querySelector(".rule-regex-validation"),
      reorderHandle: rowElement.querySelector(".rule-reorder-handle"),
      deleteButton: rowElement.querySelector(".rule-delete"),
    };
  };

  const readDraftRule = (rowElements) => {
    const sample = rowElements.regexTestInput.value.trim();
    const rule = {
      name: rowElements.nameInput.value.trim(),
      feed: rowElements.feedInput.value.trim(),
      match: rowElements.matchInput.value.trim(),
      regex: rowElements.regexInput.value.trim(),
    };

    if (rule.name === "" && rule.feed === "" && rule.regex === "") {
      return null;
    }

    if (sample !== "") {
      return { ...rule, sample };
    }

    return rule;
  };

  const writeRule = (rowElements, rule) => {
    rowElements.nameInput.value = rule.name;
    rowElements.feedInput.value = rule.feed;
    rowElements.matchInput.value = normalizeMatch(rule.match);
    rowElements.regexInput.value = rule.regex;
    rowElements.regexTestInput.value = rule.sample;
    const hasSample = rule.sample !== "";
    rowElements.regexTestEditor.hidden = !hasSample;
    rowElements.regexTestToggle.setAttribute("aria-expanded", String(hasSample));
    rowElements.regexTestToggle.classList.toggle("is-active", hasSample);
  };

  const updateEmptyState = () => {
    const hasRows = rulesList.querySelector(".rule-row") !== null;
    rulesList.classList.toggle("is-empty", !hasRows);
    rulesEmptyState.hidden = hasRows;
  };

  const getDraftRules = () => {
    const rows = [...rulesList.querySelectorAll(".rule-row")];
    return rows
      .map(getRuleRowElements)
      .map(readDraftRule)
      .filter((rule) => rule !== null);
  };

  const updateSummary = () => {
    const draftRules = getDraftRules();
    const scopedRules = draftRules.filter((rule) => rule.feed !== "");
    const globalRules = draftRules.length - scopedRules.length;
    const distinctFeeds = new Set(scopedRules.map((rule) => rule.feed.toLowerCase()));
    const totalRules = draftRules.length;

    rulesSummary.textContent = `You have ${scopedRules.length} rules covering ${distinctFeeds.size} feeds plus ${globalRules} global rules (${totalRules} total)`;
  };

  const pageTitle = document.title;
  let savedRulesSnapshot = "";
  let isDirty = false;
  let canNavigate = false;

  const markDirtyState = () => {
    rulesForm.classList.toggle("is-dirty", isDirty);
    document.title = isDirty ? `* ${pageTitle}` : pageTitle;
  };

  const refreshDirtyState = () => {
    isDirty = JSON.stringify(getDraftRules()) !== savedRulesSnapshot;
    markDirtyState();
  };

  const allowNavigationTemporarily = () => {
    canNavigate = true;
    window.setTimeout(() => {
      canNavigate = false;
    }, 0);
  };

  const handleUnsafeNavigation = () => {
    if (!isDirty || canNavigate) {
      return true;
    }

    const shouldLeave = window.confirm("You have unsaved rule changes. Leave without saving?");
    if (shouldLeave) {
      allowNavigationTemporarily();
    }
    return shouldLeave;
  };

  let summaryDebounceHandle = 0;

  const scheduleSummaryUpdate = () => {
    if (summaryDebounceHandle !== 0) {
      window.clearTimeout(summaryDebounceHandle);
    }

    summaryDebounceHandle = window.setTimeout(() => {
      summaryDebounceHandle = 0;
      updateSummary();
    }, 120);
  };

  const createRuleRow = (initialRule) => {
    const rule = normalizeRule(initialRule);
    const templateRoot = ruleRowTemplate.content.firstElementChild;
    const row = templateRoot.cloneNode(true);

    let canDrag = false;
    const rowElements = getRuleRowElements(row);

    writeRule(rowElements, rule);

    const regexOverlayField = createOverlayField({
      input: rowElements.regexInput,
      overlay: rowElements.regexOverlay,
      render: (pattern) => {
        if (pattern === "") {
          return { html: "", error: null, invalid: false };
        }

        const analysis = getRegexAnalysis(pattern);
        return {
          html: analysis.html,
          error: analysis.error,
          invalid: analysis.error !== null,
        };
      },
      afterRender: ({ overlay }) => {
        tagRegexOverlayTokens(overlay);
      },
    });

    const regexTestOverlayField = createOverlayField({
      input: rowElements.regexTestInput,
      overlay: rowElements.regexTestOverlay,
      render: (sample) => {
        return getRegexTestOverlayState(
          sample,
          rowElements.regexInput.value,
          rowElements.regexInput.classList.contains("is-invalid"),
        );
      },
    });

    const updateRegexTestState = () => {
      const overlayState = regexTestOverlayField.update();
      rowElements.regexTestEditor.classList.toggle("is-match", overlayState.isMatch);
    };

    const toggleRegexTester = () => {
      const shouldShow = rowElements.regexTestEditor.hidden;
      rowElements.regexTestEditor.hidden = !shouldShow;
      rowElements.regexTestToggle.setAttribute("aria-expanded", String(shouldShow));
      rowElements.regexTestToggle.classList.toggle("is-active", shouldShow);

      updateRegexTestState();
    };

    const openInRegex101 = () => {
      const search = new URLSearchParams({
        regex: rowElements.regexInput.value,
        testString: rowElements.regexTestInput.value,
        flavor: "dotnet",
        flags: "i",
      });
      window.open(`https://regex101.com/?${search.toString()}`, "_blank", "noopener,noreferrer");
    };

    const updateRegexUi = () => {
      const regexState = regexOverlayField.update();
      rowElements.regexValidation.textContent = regexState.error ?? "";
      rowElements.regexInput.classList.toggle("is-invalid", regexState.invalid);
      updateRegexTestState();
    };

    const normalizeRegexInput = () => {
      const normalized = rowElements.regexInput.value.toLowerCase();
      if (normalized === rowElements.regexInput.value) {
        return;
      }

      const selectionStart = rowElements.regexInput.selectionStart;
      const selectionEnd = rowElements.regexInput.selectionEnd;
      rowElements.regexInput.value = normalized;
      if (selectionStart !== null && selectionEnd !== null) {
        rowElements.regexInput.setSelectionRange(selectionStart, selectionEnd);
      }
    };

    rowElements.regexInput.addEventListener("input", () => {
      normalizeRegexInput();
      updateRegexUi();
    });
    rowElements.regexTestToggle.addEventListener("pointerdown", (event) => {
      event.preventDefault();
    });
    rowElements.regexTestToggle.addEventListener("click", toggleRegexTester);
    rowElements.regex101Link.addEventListener("pointerdown", (event) => {
      event.preventDefault();
    });
    rowElements.regex101Link.addEventListener("click", openInRegex101);
    rowElements.regexTestInput.addEventListener("input", updateRegexTestState);
    requestAnimationFrame(() => {
      if (!row.isConnected) {
        return;
      }

      updateRegexUi();
    });

    rowElements.reorderHandle.addEventListener("pointerdown", () => {
      canDrag = true;
    });

    rowElements.deleteButton.addEventListener("click", () => {
      const ruleName = rowElements.nameInput.value.trim() || "Untitled rule";
      if (!window.confirm(`Delete "${ruleName}"?`)) {
        return;
      }

      row.remove();
      updateEmptyState();
      updateSummary();
      refreshDirtyState();
    });

    row.addEventListener("dragstart", (event) => {
      if (!canDrag) {
        event.preventDefault();
        return;
      }

      row.classList.add("is-dragging");
      if (event.dataTransfer) {
        event.dataTransfer.effectAllowed = "move";
        event.dataTransfer.setData("text/plain", "");
      }
    });

    row.addEventListener("pointerup", () => {
      canDrag = false;
    });

    row.addEventListener("pointercancel", () => {
      canDrag = false;
    });

    row.addEventListener("dragend", () => {
      canDrag = false;
      row.classList.remove("is-dragging");
    });

    return row;
  };

  const getDragAfterElement = (y) => {
    const rows = [...rulesList.querySelectorAll(".rule-row:not(.is-dragging)")];
    return rows.reduce(
      (closest, child) => {
        const box = child.getBoundingClientRect();
        const offset = y - box.top - box.height / 2;
        if (offset >= 0 || offset <= closest.offset) {
          return closest;
        }

        return { offset, element: child };
      },
      { offset: Number.NEGATIVE_INFINITY, element: null },
    ).element;
  };

  const appendRuleRow = (rule, focusNameInput) => {
    const row = createRuleRow(rule);
    rulesList.append(row);
    if (!focusNameInput) {
      return;
    }

    const rowElements = getRuleRowElements(row);
    rowElements.nameInput.focus();
  };

  addRuleButton.addEventListener("click", () => {
    appendRuleRow({ match: "title" }, true);
    updateEmptyState();
    updateSummary();
    refreshDirtyState();
  });

  saveRulesButton.addEventListener("click", () => {
    rulesForm.requestSubmit();
  });

  rulesList.addEventListener("input", () => {
    scheduleSummaryUpdate();
    refreshDirtyState();
  });

  rulesList.addEventListener("change", () => {
    scheduleSummaryUpdate();
    refreshDirtyState();
  });

  rulesList.addEventListener("dragover", (event) => {
    event.preventDefault();
    const dragging = rulesList.querySelector(".rule-row.is-dragging");
    if (!(dragging instanceof HTMLElement)) {
      return;
    }

    const afterElement = getDragAfterElement(event.clientY);
    if (!(afterElement instanceof HTMLElement)) {
      rulesList.append(dragging);
      return;
    }

    rulesList.insertBefore(dragging, afterElement);
  });

  rulesList.addEventListener("dragend", () => {
    scheduleSummaryUpdate();
    refreshDirtyState();
  });

  document.addEventListener("click", (event) => {
    if (!(event.target instanceof Element)) {
      return;
    }

    const link = event.target.closest("a[href]");
    if (!(link instanceof HTMLAnchorElement)) {
      return;
    }
    if (link.target === "_blank" || link.hasAttribute("download")) {
      return;
    }
    const href = link.getAttribute("href");
    if (href === null || href.startsWith("#") || href.startsWith("javascript:")) {
      return;
    }
    if (link.href === window.location.href) {
      return;
    }
    if (handleUnsafeNavigation()) {
      return;
    }

    event.preventDefault();
  });

  window.addEventListener("beforeunload", (event) => {
    if (canNavigate || !isDirty) {
      return;
    }

    event.preventDefault();
    event.returnValue = "";
  });

  rulesForm.addEventListener("submit", (event) => {
    const rules = getDraftRules();

    for (let index = 0; index < rules.length; index++) {
      const rule = rules[index];
      if (rule.name === "" || rule.regex === "") {
        event.preventDefault();
        window.alert("Each rule requires a name and regex");
        return;
      }

      const regexError = getRegexAnalysis(rule.regex).error;
      if (regexError !== null) {
        event.preventDefault();
        window.alert(`Rule ${index + 1} has an invalid regex: ${regexError}`);
        return;
      }
    }

    rulesJsonInput.value = JSON.stringify(rules);
    allowNavigationTemporarily();
  });

  const initialRules = parseInitialRules();
  if (initialRules.length === 0) {
    updateEmptyState();
  } else {
    initialRules.forEach((rule) => appendRuleRow(rule, false));
  }

  updateSummary();
  savedRulesSnapshot = JSON.stringify(getDraftRules());
  markDirtyState();
})();
