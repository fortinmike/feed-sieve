const rulesForm = document.getElementById("rulesForm");
const saveRulesButton = document.getElementById("saveRulesButton");
const rulesSummary = document.getElementById("rulesSummary");
const rulesJsonInput = document.getElementById("RulesJson");
const initialRulesData = document.getElementById("initialRulesData");

const addGlobalFilterButton = document.getElementById("addGlobalFilterButton");
const globalFiltersList = document.getElementById("globalFiltersList");
const globalFiltersEmptyState = document.getElementById("globalFiltersEmptyState");

const addFeedRuleButton = document.getElementById("addFeedRuleButton");
const feedRulesList = document.getElementById("feedRulesList");
const feedRulesEmptyState = document.getElementById("feedRulesEmptyState");

const filterRowTemplate = document.getElementById("filterRowTemplate");
const feedRuleTemplate = document.getElementById("feedRuleTemplate");

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
    afterRender?.({ overlay, state });
    syncScroll();
    return state;
  };

  input.addEventListener("scroll", syncScroll);
  return { update, syncScroll };
};

const escapeHtml = (value) => {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;");
};

const getRegexTestOverlayState = (sample, pattern, isInvalidPattern, caseSensitive) => {
  if (sample === "") {
    return { html: "", isMatch: false };
  }
  if (pattern === "" || isInvalidPattern) {
    return { html: escapeHtml(sample), isMatch: false };
  }

  let regex;
  try {
    regex = new RegExp(pattern, caseSensitive ? "g" : "gi");
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

const normalizeFilterRule = (rule) => {
  return {
    name: typeof rule?.name === "string" ? rule.name : "",
    match: typeof rule?.match === "string" ? rule.match : "title",
    regex: typeof rule?.regex === "string" ? rule.regex : "",
    caseSensitive: rule?.caseSensitive === true,
    sample: typeof rule?.sample === "string" ? rule.sample : "",
  };
};

const normalizeFeedRule = (rule) => {
  return {
    name: typeof rule?.name === "string" ? rule.name : "",
    feed: typeof rule?.feed === "string" ? rule.feed : "",
    summaryEnabled: rule?.summaryEnabled === true,
    summaryPrompt: typeof rule?.summaryPrompt === "string" ? rule.summaryPrompt : "",
    filters: Array.isArray(rule?.filters) ? rule.filters.map(normalizeFilterRule) : [],
  };
};

const normalizeRulesConfig = (config) => {
  return {
    globalFilters: Array.isArray(config?.globalFilters) ? config.globalFilters.map(normalizeFilterRule) : [],
    feeds: Array.isArray(config?.feeds) ? config.feeds.map(normalizeFeedRule) : [],
  };
};

const parseInitialRules = () => {
  try {
    return normalizeRulesConfig(JSON.parse(initialRulesData.textContent ?? "{}"));
  } catch {
    return { globalFilters: [], feeds: [] };
  }
};

const normalizeMatch = (match) => {
  return match === "content" || match === "all" ? match : "title";
};

const getFilterRowElements = (rowElement) => {
  return {
    row: rowElement,
    nameInput: rowElement.querySelector(".rule-input-name"),
    matchInput: rowElement.querySelector(".rule-input-match"),
    regexInput: rowElement.querySelector(".rule-input-regex"),
    regexCaseToggle: rowElement.querySelector(".rule-regex-case-toggle"),
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

const readDraftFilterRule = (rowElement) => {
  const rowElements = getFilterRowElements(rowElement);
  const sample = rowElements.regexTestInput.value.trim();
  const rule = {
    name: rowElements.nameInput.value.trim(),
    match: rowElements.matchInput.value.trim(),
    regex: rowElements.regexInput.value.trim(),
    caseSensitive: rowElements.regexCaseToggle.classList.contains("is-case-sensitive"),
  };

  if (rule.name === "" && rule.regex === "") {
    return null;
  }

  if (sample !== "") {
    return { ...rule, sample };
  }

  return rule;
};

const writeFilterRule = (rowElements, rule) => {
  rowElements.nameInput.value = rule.name;
  rowElements.matchInput.value = normalizeMatch(rule.match);
  rowElements.regexInput.value = rule.regex;
  setCaseSensitive(rowElements, rule.caseSensitive);
  rowElements.regexTestInput.value = rule.sample;
  const hasSample = rule.sample !== "";
  rowElements.regexTestEditor.hidden = !hasSample;
  rowElements.regexTestToggle.setAttribute("aria-expanded", String(hasSample));
  rowElements.regexTestToggle.classList.toggle("is-active", hasSample);
};

const setCaseSensitive = (rowElements, isCaseSensitive) => {
  rowElements.regexCaseToggle.classList.toggle("is-case-sensitive", isCaseSensitive);
  rowElements.regexCaseToggle.setAttribute("aria-pressed", String(isCaseSensitive));
  rowElements.regexCaseToggle.title = isCaseSensitive ? "Case-sensitive" : "Case-insensitive";
};

const updateListEmptyState = (listElement, emptyStateElement, itemSelector) => {
  const hasRows = listElement.querySelector(itemSelector) !== null;
  listElement.classList.toggle("is-empty", !hasRows);
  emptyStateElement.hidden = hasRows;
};

const getDragAfterElement = (listElement, itemSelector, y) => {
  const rows = [...listElement.querySelectorAll(`${itemSelector}:not(.is-dragging)`)];
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

const setupSortableList = (listElement, itemSelector, onReorder) => {
  listElement.addEventListener("dragover", (event) => {
    event.preventDefault();
    const dragging = listElement.querySelector(`${itemSelector}.is-dragging`);
    if (!(dragging instanceof HTMLElement)) {
      return;
    }

    const afterElement = getDragAfterElement(listElement, itemSelector, event.clientY);
    if (!(afterElement instanceof HTMLElement)) {
      listElement.append(dragging);
      return;
    }

    listElement.insertBefore(dragging, afterElement);
  });

  listElement.addEventListener("dragend", () => {
    onReorder();
  });
};

const createFilterRow = (initialRule, onDelete) => {
  const rule = normalizeFilterRule(initialRule);
  const row = filterRowTemplate.content.firstElementChild.cloneNode(true);
  const rowElements = getFilterRowElements(row);
  let canDrag = false;

  writeFilterRule(rowElements, rule);

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
        rowElements.regexCaseToggle.classList.contains("is-case-sensitive"),
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
    const flags = rowElements.regexCaseToggle.classList.contains("is-case-sensitive") ? "" : "i";
    const search = new URLSearchParams({
      regex: rowElements.regexInput.value,
      testString: rowElements.regexTestInput.value,
      flavor: "dotnet",
      flags,
    });
    window.open(`https://regex101.com/?${search.toString()}`, "_blank", "noopener,noreferrer");
  };

  const updateRegexUi = () => {
    const regexState = regexOverlayField.update();
    rowElements.regexValidation.textContent = regexState.error ?? "";
    rowElements.regexInput.classList.toggle("is-invalid", regexState.invalid);
    updateRegexTestState();
  };

  rowElements.regexInput.addEventListener("input", updateRegexUi);
  rowElements.regexCaseToggle.addEventListener("pointerdown", (event) => {
    event.preventDefault();
  });
  rowElements.regexCaseToggle.addEventListener("click", () => {
    setCaseSensitive(rowElements, !rowElements.regexCaseToggle.classList.contains("is-case-sensitive"));
    updateRegexUi();
    refreshDirtyState();
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

  rowElements.reorderHandle.addEventListener("pointerdown", () => {
    canDrag = true;
  });

  rowElements.deleteButton.addEventListener("click", () => {
    const filterName = rowElements.nameInput.value.trim() || "Untitled filter";
    if (!window.confirm(`Delete "${filterName}"?`)) {
      return;
    }

    row.remove();
    onDelete();
    updateSummary();
    refreshDirtyState();
  });

  row.addEventListener("dragstart", (event) => {
    if (event.target !== row)
      return;

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

  requestAnimationFrame(() => {
    if (row.isConnected) {
      updateRegexUi();
    }
  });

  return row;
};

const getFeedRuleElements = (cardElement) => {
  return {
    card: cardElement,
    nameInput: cardElement.querySelector(".feed-rule-input-name"),
    feedInput: cardElement.querySelector(".feed-rule-input-feed"),
    summaryEnabledInput: cardElement.querySelector(".feed-rule-input-summary-enabled"),
    summaryPromptInput: cardElement.querySelector(".feed-rule-input-summary-prompt"),
    filtersList: cardElement.querySelector(".feed-rule-filters-list"),
    addFilterButton: cardElement.querySelector(".feed-rule-add-filter-button"),
    reorderHandle: cardElement.querySelector(".feed-rule-reorder-handle"),
    deleteButton: cardElement.querySelector(".feed-rule-delete"),
  };
};

const getDraftFilters = (listElement) => {
  return [...listElement.querySelectorAll(".rule-row")]
    .map(readDraftFilterRule)
    .filter((rule) => rule !== null);
};

const syncFeedSummaryState = (cardElements) => {
  const enabled = cardElements.summaryEnabledInput.checked;
  cardElements.summaryPromptInput.disabled = !enabled;
  cardElements.summaryPromptInput.classList.toggle("is-disabled", !enabled);
};

const readDraftFeedRule = (cardElement) => {
  const cardElements = getFeedRuleElements(cardElement);
  const filters = getDraftFilters(cardElements.filtersList);
  const rule = {
    name: cardElements.nameInput.value.trim(),
    feed: cardElements.feedInput.value.trim(),
    summaryEnabled: cardElements.summaryEnabledInput.checked,
    summaryPrompt: cardElements.summaryEnabledInput.checked ? cardElements.summaryPromptInput.value.trim() : "",
    filters,
  };

  const isEmpty =
    rule.name === ""
    && rule.feed === ""
    && !rule.summaryEnabled
    && rule.summaryPrompt === ""
    && rule.filters.length === 0;

  return isEmpty ? null : rule;
};

const createFeedRuleCard = (initialRule) => {
  const rule = normalizeFeedRule(initialRule);
  const card = feedRuleTemplate.content.firstElementChild.cloneNode(true);
  const cardElements = getFeedRuleElements(card);
  let canDrag = false;

  cardElements.nameInput.value = rule.name;
  cardElements.feedInput.value = rule.feed;
  cardElements.summaryEnabledInput.checked = rule.summaryEnabled;
  cardElements.summaryPromptInput.value = rule.summaryPrompt;
  syncFeedSummaryState(cardElements);

  setupSortableList(cardElements.filtersList, ".rule-row", () => {
    updateSummary();
    refreshDirtyState();
  });

  const appendFilterRow = (filterRule, shouldFocus) => {
    const row = createFilterRow(filterRule, () => {});
    cardElements.filtersList.append(row);
    if (shouldFocus) {
      row.querySelector(".rule-input-name")?.focus();
    }
  };

  rule.filters.forEach((filterRule) => appendFilterRow(filterRule, false));

  cardElements.addFilterButton.addEventListener("click", () => {
    appendFilterRow({ match: "title" }, true);
    updateSummary();
    refreshDirtyState();
  });

  cardElements.summaryEnabledInput.addEventListener("change", () => {
    syncFeedSummaryState(cardElements);
    refreshDirtyState();
  });

  cardElements.reorderHandle.addEventListener("pointerdown", () => {
    canDrag = true;
  });

  cardElements.deleteButton.addEventListener("click", () => {
    const ruleName = cardElements.nameInput.value.trim() || "Untitled feed rule";
    if (!window.confirm(`Delete "${ruleName}"?`)) {
      return;
    }

    card.remove();
    updateFeedRulesEmptyState();
    updateSummary();
    refreshDirtyState();
  });

  card.addEventListener("dragstart", (event) => {
    if (event.target !== card)
      return;

    if (!canDrag) {
      event.preventDefault();
      return;
    }

    card.classList.add("is-dragging");
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = "move";
      event.dataTransfer.setData("text/plain", "");
    }
  });

  card.addEventListener("pointerup", () => {
    canDrag = false;
  });
  card.addEventListener("pointercancel", () => {
    canDrag = false;
  });
  card.addEventListener("dragend", (event) => {
    if (event.target !== card)
      return;

    canDrag = false;
    card.classList.remove("is-dragging");
  });

  return card;
};

const updateGlobalFiltersEmptyState = () => {
  updateListEmptyState(globalFiltersList, globalFiltersEmptyState, ".rule-row");
};

const updateFeedRulesEmptyState = () => {
  updateListEmptyState(feedRulesList, feedRulesEmptyState, ".feed-rule-card");
};

const getDraftConfig = () => {
  return {
    globalFilters: getDraftFilters(globalFiltersList),
    feeds: [...feedRulesList.querySelectorAll(".feed-rule-card")]
      .map(readDraftFeedRule)
      .filter((rule) => rule !== null),
  };
};

const updateSummary = () => {
  const config = getDraftConfig();
  const feedFilterCount = config.feeds.reduce((total, feed) => total + feed.filters.length, 0);
  const summaryEnabledCount = config.feeds.filter((feed) => feed.summaryEnabled).length;

  rulesSummary.textContent =
    `You have ${config.globalFilters.length} global filter(s), ${config.feeds.length} feed rule(s), ` +
    `${feedFilterCount} feed-specific filter(s), and summaries enabled on ${summaryEnabledCount} feed(s)`;
};

const pageTitle = document.title;
let savedRulesSnapshot = "";
let isDirty = false;
let canNavigate = false;
let summaryDebounceHandle = 0;

const markDirtyState = () => {
  rulesForm.classList.toggle("is-dirty", isDirty);
  document.title = isDirty ? `* ${pageTitle}` : pageTitle;
};

const refreshDirtyState = () => {
  isDirty = JSON.stringify(getDraftConfig()) !== savedRulesSnapshot;
  markDirtyState();
};

const allowNavigationTemporarily = () => {
  canNavigate = true;
  window.setTimeout(() => {
    canNavigate = false;
  }, 0);
};

const allowNavigationForSubmit = () => {
  canNavigate = true;
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

const scheduleSummaryUpdate = () => {
  if (summaryDebounceHandle !== 0) {
    window.clearTimeout(summaryDebounceHandle);
  }

  summaryDebounceHandle = window.setTimeout(() => {
    summaryDebounceHandle = 0;
    updateSummary();
  }, 120);
};

const appendGlobalFilterRow = (rule, shouldFocus) => {
  const row = createFilterRow(rule, updateGlobalFiltersEmptyState);
  globalFiltersList.append(row);
  updateGlobalFiltersEmptyState();
  if (shouldFocus) {
    row.querySelector(".rule-input-name")?.focus();
  }
};

const appendFeedRuleCard = (rule, shouldFocus) => {
  const card = createFeedRuleCard(rule);
  feedRulesList.append(card);
  updateFeedRulesEmptyState();
  if (shouldFocus) {
    card.querySelector(".feed-rule-input-name")?.focus();
  }
};

addGlobalFilterButton.addEventListener("click", () => {
  appendGlobalFilterRow({ match: "title" }, true);
  updateSummary();
  refreshDirtyState();
});

addFeedRuleButton.addEventListener("click", () => {
  appendFeedRuleCard({}, true);
  updateSummary();
  refreshDirtyState();
});

saveRulesButton.addEventListener("click", () => {
  rulesForm.requestSubmit();
});

rulesForm.addEventListener("input", () => {
  scheduleSummaryUpdate();
  refreshDirtyState();
});

rulesForm.addEventListener("change", () => {
  scheduleSummaryUpdate();
  refreshDirtyState();
});

setupSortableList(globalFiltersList, ".rule-row", () => {
  updateSummary();
  refreshDirtyState();
});

setupSortableList(feedRulesList, ".feed-rule-card", () => {
  updateSummary();
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
  const config = getDraftConfig();

  for (let index = 0; index < config.globalFilters.length; index++) {
    const rule = config.globalFilters[index];
    if (rule.name === "" || rule.regex === "") {
      event.preventDefault();
      window.alert("Each global filter requires a name and regex");
      return;
    }

    const regexError = getRegexAnalysis(rule.regex).error;
    if (regexError !== null) {
      event.preventDefault();
      window.alert(`Global filter ${index + 1} has an invalid regex: ${regexError}`);
      return;
    }
  }

  const seenFeeds = new Set();
  for (let index = 0; index < config.feeds.length; index++) {
    const feedRule = config.feeds[index];
    if (feedRule.name === "" || feedRule.feed === "") {
      event.preventDefault();
      window.alert(`Feed rule ${index + 1} requires both a name and feed`);
      return;
    }
    if (!feedRule.summaryEnabled && feedRule.filters.length === 0) {
      event.preventDefault();
      window.alert(`Feed rule ${index + 1} must have at least one filter or summary enabled`);
      return;
    }

    const normalizedFeed = feedRule.feed.toLowerCase();
    if (seenFeeds.has(normalizedFeed)) {
      event.preventDefault();
      window.alert(`Feed rule ${index + 1} duplicates an existing feed`);
      return;
    }
    seenFeeds.add(normalizedFeed);

    for (let filterIndex = 0; filterIndex < feedRule.filters.length; filterIndex++) {
      const rule = feedRule.filters[filterIndex];
      if (rule.name === "" || rule.regex === "") {
        event.preventDefault();
        window.alert(`Feed rule ${index + 1}, filter ${filterIndex + 1} requires a name and regex`);
        return;
      }

      const regexError = getRegexAnalysis(rule.regex).error;
      if (regexError !== null) {
        event.preventDefault();
        window.alert(`Feed rule ${index + 1}, filter ${filterIndex + 1} has an invalid regex: ${regexError}`);
        return;
      }
    }
  }

  rulesJsonInput.value = JSON.stringify(config);
  allowNavigationForSubmit();
});

const initialRules = parseInitialRules();
initialRules.globalFilters.forEach((rule) => appendGlobalFilterRow(rule, false));
initialRules.feeds.forEach((rule) => appendFeedRuleCard(rule, false));

updateGlobalFiltersEmptyState();
updateFeedRulesEmptyState();

document.querySelectorAll("[data-save-toast]").forEach((toast) => {
  const dismiss = () => {
    if (toast.classList.contains("is-hiding")) {
      return;
    }

    toast.classList.add("is-hiding");
    window.setTimeout(() => toast.remove(), 140);
  };

  toast.querySelector(".rules-toast-close")?.addEventListener("click", dismiss);
  window.setTimeout(dismiss, 3200);
});

updateSummary();
savedRulesSnapshot = JSON.stringify(getDraftConfig());
markDirtyState();
